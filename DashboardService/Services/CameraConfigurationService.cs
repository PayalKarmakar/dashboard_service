using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

public sealed class CameraConfigurationService
{
    private readonly ConfigurationService _configurationService = new();

    public async Task<List<MasterCameraConfig>> GetAllAsync()
    {
        var cameras = new List<MasterCameraConfig>();

        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                c.camera_id,
                c.chamber_id,
                COALESCE(ch.chamber_name, ''),
                c.camera_name,
                c.camera_purpose,
                c.ip_address,
                c.rtsp_url,
                c.rfid_reader_id,
                COALESCE(r.reader_name, ''),
                c.person_detection_enabled,
                c.match_window_seconds,
                c.alert_on_no_rfid,
                c.alert_on_tailgate,
                c.is_active
            FROM public.master_cameras c
            LEFT JOIN public.master_chambers ch
                ON ch.chamber_id = c.chamber_id
            LEFT JOIN public.master_rfid_readers r
                ON r.reader_id = c.rfid_reader_id
            ORDER BY c.chamber_id, c.camera_id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            cameras.Add(MapCamera(reader));
        }

        return cameras;
    }

    public async Task AddAsync(MasterCameraConfig camera, long changedBy)
    {
        ValidateCamera(camera);

        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO public.master_cameras
            (
                chamber_id,
                camera_name,
                camera_purpose,
                ip_address,
                rtsp_url,
                rfid_reader_id,
                person_detection_enabled,
                match_window_seconds,
                alert_on_no_rfid,
                alert_on_tailgate,
                is_active,
                last_updated_by,
                updated_at
            )
            VALUES
            (
                @chamberId,
                @cameraName,
                @cameraPurpose,
                @ipAddress,
                @rtspUrl,
                @rfidReaderId,
                @personDetectionEnabled,
                @matchWindowSeconds,
                @alertOnNoRfid,
                @alertOnTailgate,
                TRUE,
                @changedBy,
                NOW()
            );
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        BindCameraParameters(command, camera, changedBy);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateAsync(MasterCameraConfig camera, long changedBy)
    {
        ValidateCamera(camera);

        if (camera.CameraId <= 0)
        {
            throw new InvalidOperationException("Camera ID is required for update.");
        }

        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            UPDATE public.master_cameras
            SET chamber_id = @chamberId,
                camera_name = @cameraName,
                camera_purpose = @cameraPurpose,
                ip_address = @ipAddress,
                rtsp_url = @rtspUrl,
                rfid_reader_id = @rfidReaderId,
                person_detection_enabled = @personDetectionEnabled,
                match_window_seconds = @matchWindowSeconds,
                alert_on_no_rfid = @alertOnNoRfid,
                alert_on_tailgate = @alertOnTailgate,
                last_updated_by = @changedBy,
                updated_at = NOW()
            WHERE camera_id = @cameraId;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("cameraId", camera.CameraId);
        BindCameraParameters(command, camera, changedBy);
        await command.ExecuteNonQueryAsync();
    }

    public async Task SetActiveAsync(long cameraId, bool isActive, long changedBy)
    {
        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            UPDATE public.master_cameras
            SET is_active = @isActive,
                last_updated_by = @changedBy,
                updated_at = NOW()
            WHERE camera_id = @cameraId;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("cameraId", cameraId);
        command.Parameters.AddWithValue("isActive", isActive);
        command.Parameters.AddWithValue("changedBy", changedBy);
        await command.ExecuteNonQueryAsync();
    }

    private static void ValidateCamera(MasterCameraConfig camera)
    {
        if (string.IsNullOrWhiteSpace(camera.CameraName))
        {
            throw new InvalidOperationException("Camera name is required.");
        }

        if (camera.ChamberId <= 0)
        {
            throw new InvalidOperationException("Chamber is required.");
        }

        if (string.IsNullOrWhiteSpace(camera.RtspUrl))
        {
            throw new InvalidOperationException("RTSP URL is required.");
        }

        if (camera.MatchWindowSeconds <= 0 || camera.MatchWindowSeconds > 120)
        {
            throw new InvalidOperationException("Match window must be between 1 and 120 seconds.");
        }

        string purpose = camera.CameraPurpose.Trim().ToUpperInvariant();
        if (purpose is not ("ENTRY" or "EXIT" or "DOOR" or "MONITORING"))
        {
            throw new InvalidOperationException("Invalid camera purpose.");
        }

        camera.CameraPurpose = purpose;
    }

    private static void BindCameraParameters(
        NpgsqlCommand command,
        MasterCameraConfig camera,
        long changedBy)
    {
        command.Parameters.AddWithValue("chamberId", camera.ChamberId);
        command.Parameters.AddWithValue("cameraName", camera.CameraName.Trim());
        command.Parameters.AddWithValue("cameraPurpose", camera.CameraPurpose.Trim());
        command.Parameters.AddWithValue(
            "ipAddress",
            string.IsNullOrWhiteSpace(camera.IpAddress)
                ? DBNull.Value
                : camera.IpAddress.Trim());
        command.Parameters.AddWithValue("rtspUrl", camera.RtspUrl.Trim());
        command.Parameters.AddWithValue(
            "rfidReaderId",
            camera.RfidReaderId.HasValue && camera.RfidReaderId.Value > 0
                ? camera.RfidReaderId.Value
                : DBNull.Value);
        command.Parameters.AddWithValue("personDetectionEnabled", camera.PersonDetectionEnabled);
        command.Parameters.AddWithValue("matchWindowSeconds", camera.MatchWindowSeconds);
        command.Parameters.AddWithValue("alertOnNoRfid", camera.AlertOnNoRfid);
        command.Parameters.AddWithValue("alertOnTailgate", camera.AlertOnTailgate);
        command.Parameters.AddWithValue("changedBy", changedBy);
    }

    private static MasterCameraConfig MapCamera(NpgsqlDataReader reader) =>
        new()
        {
            CameraId = reader.GetInt64(0),
            ChamberId = reader.GetInt64(1),
            ChamberName = reader.GetString(2),
            CameraName = reader.GetString(3),
            CameraPurpose = reader.GetString(4),
            IpAddress = reader.IsDBNull(5) ? null : reader.GetString(5),
            RtspUrl = reader.GetString(6),
            RfidReaderId = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            RfidReaderName = reader.GetString(8),
            PersonDetectionEnabled = reader.GetBoolean(9),
            MatchWindowSeconds = reader.GetInt32(10),
            AlertOnNoRfid = reader.GetBoolean(11),
            AlertOnTailgate = reader.GetBoolean(12),
            IsActive = reader.GetBoolean(13)
        };
}
