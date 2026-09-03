using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

/// <summary>
/// Correlates camera door-line ENTRY events with RFID scans
/// inside the configured match window.
/// </summary>
public sealed class CameraDoorVerificationService
{
    private readonly ConfigurationService _configurationService = new();
    private readonly object _sync = new();
    private readonly List<PendingCameraEntry> _pending = new();
    private readonly HashSet<string> _raisedKeys = new();

    private MasterCameraConfig? _camera;
    private string? _linkedReaderIp;
    private int? _linkedReaderPort;
    private int _lastEntryCount = -1;

    public event Action<CameraDoorAlert>? AlertRaised;

    public void Configure(MasterCameraConfig camera, RfidReader? linkedReader)
    {
        lock (_sync)
        {
            _camera = camera;
            _linkedReaderIp = string.IsNullOrWhiteSpace(linkedReader?.IpAddress)
                ? null
                : linkedReader.IpAddress.Trim();
            _linkedReaderPort = linkedReader is { Port: > 0 } ? linkedReader.Port : null;
            _pending.Clear();
            _raisedKeys.Clear();
            _lastEntryCount = -1;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _pending.Clear();
            _raisedKeys.Clear();
            _lastEntryCount = -1;
        }
    }

    public void ObserveCameraEntryCount(int entryCount)
    {
        MasterCameraConfig? camera;
        int delta;

        lock (_sync)
        {
            camera = _camera;
            if (camera == null || !camera.PersonDetectionEnabled)
            {
                return;
            }

            if (_lastEntryCount < 0)
            {
                _lastEntryCount = entryCount;
                return;
            }

            // Door/Entry/Exit cameras use line-crossing deltas.
            // Monitoring cameras use occupancy matching instead.
            if (string.Equals(camera.CameraPurpose, "MONITORING", StringComparison.OrdinalIgnoreCase))
            {
                _lastEntryCount = entryCount;
                return;
            }

            if (entryCount <= _lastEntryCount)
            {
                return;
            }

            delta = entryCount - _lastEntryCount;
            _lastEntryCount = entryCount;

            if (delta <= 0)
            {
                return;
            }

            // MONITORING cameras still can verify if alerts are enabled.
            if (!camera.AlertOnNoRfid && !camera.AlertOnTailgate)
            {
                return;
            }

            DateTime now = DateTime.Now;
            _pending.Add(new PendingCameraEntry
            {
                DetectedAt = now,
                DueAt = now.AddSeconds(Math.Max(1, camera.MatchWindowSeconds)),
                PersonCount = delta
            });
        }
    }

    public async Task ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        List<PendingCameraEntry> due;
        MasterCameraConfig camera;
        string? readerIp;
        int? readerPort;

        lock (_sync)
        {
            if (_camera == null)
            {
                return;
            }

            camera = _camera;
            readerIp = _linkedReaderIp;
            readerPort = _linkedReaderPort;
            DateTime now = DateTime.Now;
            due = _pending.Where(p => p.DueAt <= now).ToList();
            if (due.Count == 0)
            {
                return;
            }

            foreach (var item in due)
            {
                _pending.Remove(item);
            }
        }

        foreach (var pending in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int rfidCount = await CountRfidEntriesAsync(
                camera.ChamberId,
                pending.DetectedAt.AddSeconds(-2),
                pending.DueAt,
                readerIp,
                readerPort,
                cancellationToken);

            if (camera.AlertOnNoRfid && rfidCount == 0)
            {
                RaiseAlert(new CameraDoorAlert
                {
                    RaisedAt = DateTime.Now,
                    AlertType = "NO_RFID",
                    CameraPersonCount = pending.PersonCount,
                    RfidScanCount = 0,
                    Message =
                        $"{camera.CameraName}: {pending.PersonCount} person(s) entered {camera.ChamberName} " +
                        $"but no RFID scan within {camera.MatchWindowSeconds}s."
                }, pending);
                continue;
            }

            if (camera.AlertOnTailgate && pending.PersonCount > rfidCount)
            {
                RaiseAlert(new CameraDoorAlert
                {
                    RaisedAt = DateTime.Now,
                    AlertType = "TAILGATE",
                    CameraPersonCount = pending.PersonCount,
                    RfidScanCount = rfidCount,
                    Message =
                        $"{camera.CameraName}: possible tailgating at {camera.ChamberName} — " +
                        $"camera saw {pending.PersonCount} person(s), RFID scanned {rfidCount}."
                }, pending);
                continue;
            }

            RaiseAlert(new CameraDoorAlert
            {
                RaisedAt = DateTime.Now,
                AlertType = "MATCHED",
                CameraPersonCount = pending.PersonCount,
                RfidScanCount = rfidCount,
                Message =
                    $"{camera.CameraName}: verified — camera {pending.PersonCount}, RFID {rfidCount} " +
                    $"within {camera.MatchWindowSeconds}s."
            }, pending);
        }
    }

    private void RaiseAlert(CameraDoorAlert alert, PendingCameraEntry pending)
    {
        string key =
            $"{alert.AlertType}|{pending.DetectedAt:O}|{pending.PersonCount}|{alert.RfidScanCount}";

        lock (_sync)
        {
            if (!_raisedKeys.Add(key))
            {
                return;
            }

            // Keep memory bounded.
            if (_raisedKeys.Count > 200)
            {
                _raisedKeys.Clear();
            }
        }

        AlertRaised?.Invoke(alert);
    }

    private async Task<int> CountRfidEntriesAsync(
        long chamberId,
        DateTime from,
        DateTime to,
        string? readerIp,
        int? readerPort,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT COUNT(*)
            FROM public.rfid_transactions t
            WHERE t.chamber_id = @chamberId
              AND t.entry_time IS NOT NULL
              AND t.entry_time >= @fromTime
              AND t.entry_time <= @toTime
              AND (
                    @readerIp = ''
                    OR (
                        COALESCE(t.entry_reader_ip, '') = @readerIp
                        AND (@readerPort = 0 OR COALESCE(t.entry_reader_port, 0) = @readerPort)
                    )
                  );
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("chamberId", chamberId);
        command.Parameters.AddWithValue("fromTime", from);
        command.Parameters.AddWithValue("toTime", to);
        command.Parameters.AddWithValue("readerIp", readerIp ?? string.Empty);
        command.Parameters.AddWithValue("readerPort", readerPort ?? 0);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private sealed class PendingCameraEntry
    {
        public DateTime DetectedAt { get; init; }

        public DateTime DueAt { get; init; }

        public int PersonCount { get; init; }
    }
}
