using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

/// <summary>
/// For MONITORING cameras: compare live person count in frame
/// with RFID open (inside) count for the chamber.
/// </summary>
public sealed class CameraOccupancyVerificationService
{
    private readonly ConfigurationService _configurationService = new();
    private readonly object _sync = new();
    private readonly HashSet<string> _activeAlertKeys = new();

    private MasterCameraConfig? _camera;
    private int _lastCameraCount = -1;
    private int _lastRfidCount = -1;
    private DateTime? _mismatchSince;
    private string? _currentMismatchSignature;
    private DateTime _lastMatchedAnnounceAt = DateTime.MinValue;

    public event Action<CameraDoorAlert>? AlertRaised;

    public void Configure(MasterCameraConfig camera)
    {
        lock (_sync)
        {
            _camera = camera;
            ResetState();
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            ResetState();
        }
    }

    public async Task EvaluateAsync(
        int cameraPersonCount,
        CancellationToken cancellationToken = default)
    {
        MasterCameraConfig? camera;
        lock (_sync)
        {
            camera = _camera;
        }

        if (camera == null
            || !camera.PersonDetectionEnabled
            || !string.Equals(camera.CameraPurpose, "MONITORING", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!camera.AlertOnNoRfid && !camera.AlertOnTailgate)
        {
            return;
        }

        int rfidInside = await CountOpenRfidAsync(camera.ChamberId, cancellationToken);
        int cameraCount = Math.Max(0, cameraPersonCount);
        DateTime now = DateTime.Now;

        bool mismatch = cameraCount > rfidInside;
        string signature = $"{cameraCount}|{rfidInside}";

        CameraDoorAlert? alertToRaise = null;

        lock (_sync)
        {
            _lastCameraCount = cameraCount;
            _lastRfidCount = rfidInside;

            if (!mismatch)
            {
                _mismatchSince = null;
                _currentMismatchSignature = null;
                _activeAlertKeys.Clear();

                // Soft OK status occasionally when values change after a mismatch window.
                if ((now - _lastMatchedAnnounceAt).TotalSeconds >= Math.Max(15, camera.MatchWindowSeconds)
                    && (cameraCount > 0 || rfidInside > 0))
                {
                    _lastMatchedAnnounceAt = now;
                    alertToRaise = new CameraDoorAlert
                    {
                        RaisedAt = now,
                        AlertType = "OCCUPANCY_MATCHED",
                        CameraPersonCount = cameraCount,
                        RfidScanCount = rfidInside,
                        Message =
                            $"{camera.CameraName}: occupancy matched in {camera.ChamberName} — " +
                            $"camera {cameraCount}, RFID {rfidInside}."
                    };
                }
            }
            else
            {
                if (_currentMismatchSignature != signature)
                {
                    _currentMismatchSignature = signature;
                    _mismatchSince = now;
                }

                _mismatchSince ??= now;
                int stableSeconds = (int)(now - _mismatchSince.Value).TotalSeconds;

                if (stableSeconds < Math.Max(1, camera.MatchWindowSeconds))
                {
                    return;
                }

                string alertType;
                if (rfidInside == 0 && camera.AlertOnNoRfid)
                {
                    alertType = "OCCUPANCY_NO_RFID";
                }
                else if (camera.AlertOnTailgate)
                {
                    alertType = "OCCUPANCY_MISMATCH";
                }
                else
                {
                    return;
                }

                string key = $"{alertType}|{signature}";
                if (!_activeAlertKeys.Add(key))
                {
                    return;
                }

                alertToRaise = new CameraDoorAlert
                {
                    RaisedAt = now,
                    AlertType = alertType,
                    CameraPersonCount = cameraCount,
                    RfidScanCount = rfidInside,
                    Message = alertType == "OCCUPANCY_NO_RFID"
                        ? $"{camera.CameraName}: {cameraCount} person(s) visible in {camera.ChamberName} but no RFID entry is open."
                        : $"{camera.CameraName}: occupancy mismatch in {camera.ChamberName} — " +
                          $"camera sees {cameraCount}, RFID inside {rfidInside}."
                };
            }
        }

        if (alertToRaise != null)
        {
            AlertRaised?.Invoke(alertToRaise);
        }
    }

    public (int CameraCount, int RfidCount, string StatusText) GetLastStatus()
    {
        lock (_sync)
        {
            if (_camera == null)
            {
                return (0, 0, "Idle");
            }

            if (_lastCameraCount < 0)
            {
                return (0, 0, "Waiting for detections...");
            }

            if (_lastCameraCount > _lastRfidCount)
            {
                int wait = 0;
                if (_mismatchSince.HasValue)
                {
                    wait = Math.Max(
                        0,
                        _camera.MatchWindowSeconds - (int)(DateTime.Now - _mismatchSince.Value).TotalSeconds);
                }

                return (
                    _lastCameraCount,
                    _lastRfidCount,
                    wait > 0
                        ? $"Mismatch pending ({wait}s): camera {_lastCameraCount} > RFID {_lastRfidCount}"
                        : $"Mismatch: camera {_lastCameraCount} > RFID {_lastRfidCount}");
            }

            return (
                _lastCameraCount,
                _lastRfidCount,
                $"Matched: camera {_lastCameraCount} = RFID {_lastRfidCount}");
        }
    }

    private void ResetState()
    {
        _lastCameraCount = -1;
        _lastRfidCount = -1;
        _mismatchSince = null;
        _currentMismatchSignature = null;
        _activeAlertKeys.Clear();
        _lastMatchedAnnounceAt = DateTime.MinValue;
    }

    private async Task<int> CountOpenRfidAsync(long chamberId, CancellationToken cancellationToken)
    {
        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT COUNT(*)
            FROM public.rfid_transactions t
            WHERE t.chamber_id = @chamberId
              AND t.status = 'OPEN'
              AND t.exit_time IS NULL;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("chamberId", chamberId);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }
}
