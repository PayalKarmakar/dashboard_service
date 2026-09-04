using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

/// <summary>
/// Correlates camera door-line ENTRY / EXIT crossings with RFID scans
/// inside the configured match window.
/// </summary>
public sealed class CameraDoorVerificationService
{
    private readonly ConfigurationService _configurationService = new();
    private readonly object _sync = new();
    private readonly List<PendingCrossing> _pending = new();
    private readonly HashSet<string> _raisedKeys = new();

    private MasterCameraConfig? _camera;
    private string? _linkedReaderIp;
    private int? _linkedReaderPort;
    private int _lastEntryCount = -1;
    private int _lastExitCount = -1;

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
            _lastExitCount = -1;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _pending.Clear();
            _raisedKeys.Clear();
            _lastEntryCount = -1;
            _lastExitCount = -1;
        }
    }

    public void ObserveCameraEntryCount(int entryCount) =>
        ObserveCrossingDelta(entryCount, CrossingDirection.Entry, ref _lastEntryCount);

    public void ObserveCameraExitCount(int exitCount) =>
        ObserveCrossingDelta(exitCount, CrossingDirection.Exit, ref _lastExitCount);

    private void ObserveCrossingDelta(
        int count,
        CrossingDirection direction,
        ref int lastCount)
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

            if (lastCount < 0)
            {
                lastCount = count;
                return;
            }

            if (string.Equals(camera.CameraPurpose, "MONITORING", StringComparison.OrdinalIgnoreCase))
            {
                lastCount = count;
                return;
            }

            if (count <= lastCount)
            {
                return;
            }

            delta = count - lastCount;
            lastCount = count;

            if (delta <= 0)
            {
                return;
            }

            if (!camera.AlertOnNoRfid && !camera.AlertOnTailgate)
            {
                return;
            }

            DateTime now = DateTime.Now;
            _pending.Add(new PendingCrossing
            {
                Direction = direction,
                DetectedAt = now,
                DueAt = now.AddSeconds(Math.Max(1, camera.MatchWindowSeconds)),
                PersonCount = delta
            });
        }
    }

    public async Task ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        List<PendingCrossing> due;
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

            bool isExit = pending.Direction == CrossingDirection.Exit;
            int rfidCount = isExit
                ? await CountRfidExitsAsync(
                    camera.ChamberId,
                    pending.DetectedAt.AddSeconds(-2),
                    pending.DueAt,
                    cancellationToken)
                : await CountRfidEntriesAsync(
                    camera.ChamberId,
                    pending.DetectedAt.AddSeconds(-2),
                    pending.DueAt,
                    readerIp,
                    readerPort,
                    cancellationToken);

            string actionWord = isExit ? "exited" : "entered";
            string noRfidType = isExit ? "NO_RFID_EXIT" : "NO_RFID";
            string tailgateType = isExit ? "EXIT_TAILGATE" : "TAILGATE";
            string matchedType = isExit ? "EXIT_MATCHED" : "MATCHED";

            if (camera.AlertOnNoRfid && rfidCount == 0)
            {
                RaiseAlert(new CameraDoorAlert
                {
                    RaisedAt = DateTime.Now,
                    AlertType = noRfidType,
                    CameraPersonCount = pending.PersonCount,
                    RfidScanCount = 0,
                    Message =
                        $"{camera.CameraName}: {pending.PersonCount} person(s) {actionWord} {camera.ChamberName} " +
                        $"but no RFID {(isExit ? "exit" : "entry")} scan within {camera.MatchWindowSeconds}s."
                }, pending);
                continue;
            }

            if (camera.AlertOnTailgate && pending.PersonCount > rfidCount)
            {
                RaiseAlert(new CameraDoorAlert
                {
                    RaisedAt = DateTime.Now,
                    AlertType = tailgateType,
                    CameraPersonCount = pending.PersonCount,
                    RfidScanCount = rfidCount,
                    Message =
                        $"{camera.CameraName}: possible {(isExit ? "exit " : "")}tailgating at {camera.ChamberName} — " +
                        $"camera saw {pending.PersonCount} person(s), RFID {(isExit ? "exits" : "scans")} {rfidCount}."
                }, pending);
                continue;
            }

            RaiseAlert(new CameraDoorAlert
            {
                RaisedAt = DateTime.Now,
                AlertType = matchedType,
                CameraPersonCount = pending.PersonCount,
                RfidScanCount = rfidCount,
                Message =
                    $"{camera.CameraName}: {(isExit ? "exit " : "")}verified — camera {pending.PersonCount}, " +
                    $"RFID {rfidCount} within {camera.MatchWindowSeconds}s."
            }, pending);
        }
    }

    private void RaiseAlert(CameraDoorAlert alert, PendingCrossing pending)
    {
        string key =
            $"{pending.Direction}|{alert.AlertType}|{pending.DetectedAt:O}|{pending.PersonCount}|{alert.RfidScanCount}";

        lock (_sync)
        {
            if (!_raisedKeys.Add(key))
            {
                return;
            }

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

    private async Task<int> CountRfidExitsAsync(
        long chamberId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT COUNT(*)
            FROM public.rfid_transactions t
            WHERE t.chamber_id = @chamberId
              AND t.exit_time IS NOT NULL
              AND t.exit_time >= @fromTime
              AND t.exit_time <= @toTime;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("chamberId", chamberId);
        command.Parameters.AddWithValue("fromTime", from);
        command.Parameters.AddWithValue("toTime", to);

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private enum CrossingDirection
    {
        Entry,
        Exit
    }

    private sealed class PendingCrossing
    {
        public CrossingDirection Direction { get; init; }

        public DateTime DetectedAt { get; init; }

        public DateTime DueAt { get; init; }

        public int PersonCount { get; init; }
    }
}
