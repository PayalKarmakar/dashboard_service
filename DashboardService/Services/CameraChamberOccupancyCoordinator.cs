using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

/// <summary>
/// Chamber-level MONITORING occupancy (Sum/Max across cameras) vs RFID inside count.
/// </summary>
public sealed class CameraChamberOccupancyCoordinator
{
    private readonly ConfigurationService _configurationService = new();
    private readonly object _sync = new();
    private readonly Dictionary<long, ChamberOccupancyState> _states = new();

    public event Action<long, CameraDoorAlert>? AlertRaised;

    public async Task EvaluateChamberAsync(
        long chamberId,
        string chamberName,
        string sourceLabel,
        int aggregatePersonCount,
        int matchWindowSeconds,
        bool alertOnNoRfid,
        bool alertOnTailgate,
        CancellationToken cancellationToken = default)
    {
        if (!alertOnNoRfid && !alertOnTailgate)
        {
            return;
        }

        int rfidInside = await CountOpenRfidAsync(chamberId, cancellationToken);
        int cameraCount = Math.Max(0, aggregatePersonCount);
        DateTime now = DateTime.Now;
        bool mismatch = cameraCount > rfidInside;
        string signature = $"{cameraCount}|{rfidInside}";

        CameraDoorAlert? alertToRaise = null;
        lock (_sync)
        {
            if (!_states.TryGetValue(chamberId, out ChamberOccupancyState? state))
            {
                state = new ChamberOccupancyState();
                _states[chamberId] = state;
            }

            state.LastCameraCount = cameraCount;
            state.LastRfidCount = rfidInside;

            if (!mismatch)
            {
                state.MismatchSince = null;
                state.CurrentMismatchSignature = null;
                state.ActiveAlertKeys.Clear();

                if ((now - state.LastMatchedAnnounceAt).TotalSeconds >= Math.Max(15, matchWindowSeconds)
                    && (cameraCount > 0 || rfidInside > 0))
                {
                    state.LastMatchedAnnounceAt = now;
                    alertToRaise = new CameraDoorAlert
                    {
                        RaisedAt = now,
                        AlertType = "OCCUPANCY_MATCHED",
                        CameraPersonCount = cameraCount,
                        RfidScanCount = rfidInside,
                        Message =
                            $"{sourceLabel}: occupancy matched in {chamberName} — " +
                            $"cameras {cameraCount}, RFID {rfidInside}."
                    };
                }
            }
            else
            {
                if (state.CurrentMismatchSignature != signature)
                {
                    state.CurrentMismatchSignature = signature;
                    state.MismatchSince = now;
                }

                state.MismatchSince ??= now;
                int stableSeconds = (int)(now - state.MismatchSince.Value).TotalSeconds;
                if (stableSeconds < Math.Max(1, matchWindowSeconds))
                {
                    return;
                }

                string alertType;
                if (rfidInside == 0 && alertOnNoRfid)
                {
                    alertType = "OCCUPANCY_NO_RFID";
                }
                else if (alertOnTailgate)
                {
                    alertType = "OCCUPANCY_MISMATCH";
                }
                else
                {
                    return;
                }

                string key = $"{alertType}|{signature}";
                if (!state.ActiveAlertKeys.Add(key))
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
                        ? $"{sourceLabel}: {cameraCount} person(s) visible in {chamberName} but no RFID entry is open."
                        : $"{sourceLabel}: occupancy mismatch in {chamberName} — " +
                          $"cameras see {cameraCount}, RFID inside {rfidInside}."
                };
            }
        }

        if (alertToRaise != null)
        {
            AlertRaised?.Invoke(chamberId, alertToRaise);
        }
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

    private sealed class ChamberOccupancyState
    {
        public int LastCameraCount { get; set; } = -1;

        public int LastRfidCount { get; set; } = -1;

        public DateTime? MismatchSince { get; set; }

        public string? CurrentMismatchSignature { get; set; }

        public HashSet<string> ActiveAlertKeys { get; } = new(StringComparer.Ordinal);

        public DateTime LastMatchedAnnounceAt { get; set; } = DateTime.MinValue;
    }
}
