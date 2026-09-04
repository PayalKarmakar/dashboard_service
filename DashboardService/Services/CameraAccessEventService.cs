using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

/// <summary>
/// Persists camera ENTRY / EXIT crossings and unauthorized alerts with date/time.
/// </summary>
public sealed class CameraAccessEventService
{
    private readonly ConfigurationService _configurationService = new();
    private static int _schemaReady;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _schemaReady, 1, 0) == 1)
        {
            return;
        }

        try
        {
            await using var connection =
                new NpgsqlConnection(_configurationService.GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            const string sql = @"
                CREATE TABLE IF NOT EXISTS public.camera_access_events (
                    event_id          BIGSERIAL PRIMARY KEY,
                    camera_id         BIGINT NOT NULL,
                    chamber_id        BIGINT NOT NULL,
                    camera_name       VARCHAR(200) NOT NULL DEFAULT '',
                    chamber_name      VARCHAR(200) NOT NULL DEFAULT '',
                    event_type        VARCHAR(40) NOT NULL,
                    person_count      INT NOT NULL DEFAULT 1,
                    rfid_scan_count   INT NULL,
                    message           TEXT NULL,
                    occurred_at       TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT NOW()
                );

                CREATE INDEX IF NOT EXISTS ix_camera_access_events_occurred_at
                    ON public.camera_access_events (occurred_at DESC);

                CREATE INDEX IF NOT EXISTS ix_camera_access_events_type
                    ON public.camera_access_events (event_type);

                CREATE INDEX IF NOT EXISTS ix_camera_access_events_camera
                    ON public.camera_access_events (camera_id, occurred_at DESC);
            ";

            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            Interlocked.Exchange(ref _schemaReady, 0);
            throw;
        }
    }

    public async Task LogCrossingAsync(
        MasterCameraConfig camera,
        string eventType,
        int personCount,
        CancellationToken cancellationToken = default)
    {
        if (personCount <= 0)
        {
            return;
        }

        string type = eventType.Trim().ToUpperInvariant();
        if (type is not ("ENTRY" or "EXIT"))
        {
            return;
        }

        await InsertAsync(
            camera,
            type,
            personCount,
            rfidScanCount: null,
            message: $"{personCount} person(s) crossed ({type}) at {camera.CameraName}",
            occurredAt: DateTime.Now,
            cancellationToken);
    }

    public async Task LogAlertAsync(
        MasterCameraConfig camera,
        CameraDoorAlert alert,
        CancellationToken cancellationToken = default)
    {
        string type = alert.AlertType.Trim().ToUpperInvariant();
        if (type is not ("NO_RFID" or "TAILGATE" or "MATCHED"))
        {
            return;
        }

        await InsertAsync(
            camera,
            type,
            alert.CameraPersonCount,
            alert.RfidScanCount,
            alert.Message,
            alert.RaisedAt,
            cancellationToken);
    }

    public async Task<List<CameraAccessEventRow>> GetReportAsync(
        DateTime fromDate,
        DateTime toDate,
        string? eventFilter = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        var rows = new List<CameraAccessEventRow>();
        DateTime from = fromDate.Date;
        DateTime to = toDate.Date.AddDays(1).AddTicks(-1);
        string filter = (eventFilter ?? string.Empty).Trim().ToUpperInvariant();

        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT
                event_id,
                occurred_at,
                camera_id,
                chamber_id,
                camera_name,
                chamber_name,
                event_type,
                person_count,
                rfid_scan_count,
                COALESCE(message, '')
            FROM public.camera_access_events
            WHERE occurred_at >= @fromDate
              AND occurred_at <= @toDate
              AND (
                    @filter = ''
                    OR (@filter = 'ENTRY' AND event_type = 'ENTRY')
                    OR (@filter = 'EXIT' AND event_type = 'EXIT')
                    OR (@filter = 'UNAUTHORIZED'
                        AND event_type IN ('NO_RFID', 'TAILGATE'))
                    OR (@filter = 'NO_RFID' AND event_type = 'NO_RFID')
                    OR (@filter = 'TAILGATE' AND event_type = 'TAILGATE')
                    OR (@filter = 'MATCHED' AND event_type = 'MATCHED')
                  )
            ORDER BY occurred_at DESC, event_id DESC;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("fromDate", from);
        command.Parameters.AddWithValue("toDate", to);
        command.Parameters.AddWithValue("filter", filter);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CameraAccessEventRow
            {
                EventId = reader.GetInt64(0),
                OccurredAt = reader.GetDateTime(1),
                CameraId = reader.GetInt64(2),
                ChamberId = reader.GetInt64(3),
                CameraName = reader.GetString(4),
                ChamberName = reader.GetString(5),
                EventType = reader.GetString(6),
                PersonCount = reader.GetInt32(7),
                RfidScanCount = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                Message = reader.GetString(9)
            });
        }

        return rows;
    }

    private async Task InsertAsync(
        MasterCameraConfig camera,
        string eventType,
        int personCount,
        int? rfidScanCount,
        string message,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT INTO public.camera_access_events (
                camera_id,
                chamber_id,
                camera_name,
                chamber_name,
                event_type,
                person_count,
                rfid_scan_count,
                message,
                occurred_at
            ) VALUES (
                @cameraId,
                @chamberId,
                @cameraName,
                @chamberName,
                @eventType,
                @personCount,
                @rfidScanCount,
                @message,
                @occurredAt
            );
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("cameraId", camera.CameraId);
        command.Parameters.AddWithValue("chamberId", camera.ChamberId);
        command.Parameters.AddWithValue("cameraName", camera.CameraName ?? string.Empty);
        command.Parameters.AddWithValue("chamberName", camera.ChamberName ?? string.Empty);
        command.Parameters.AddWithValue("eventType", eventType);
        command.Parameters.AddWithValue("personCount", Math.Max(1, personCount));
        command.Parameters.AddWithValue(
            "rfidScanCount",
            rfidScanCount.HasValue ? rfidScanCount.Value : DBNull.Value);
        command.Parameters.AddWithValue("message", message ?? string.Empty);
        command.Parameters.AddWithValue("occurredAt", occurredAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
