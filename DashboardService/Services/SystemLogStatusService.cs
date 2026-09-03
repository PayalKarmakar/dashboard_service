using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

public sealed class SystemLogStatusService
{
    private readonly ConfigurationService _configurationService = new();

    public async Task<List<SystemLogConnectionStatus>> GetLatestSensorStatusesAsync()
    {
        const string sql = @"
            WITH ranked AS (
                SELECT
                    event_type,
                    message,
                    source_port,
                    created_at,
                    CASE
                        WHEN message ILIKE '%temperature%' OR message ILIKE '%humidity%' THEN 'TRH'
                        WHEN message ILIKE '%gas%' THEN 'GAS'
                        ELSE COALESCE(source_port, message)
                    END AS device_key,
                    ROW_NUMBER() OVER (
                        PARTITION BY
                            COALESCE(source_port, ''),
                            CASE
                                WHEN message ILIKE '%temperature%' OR message ILIKE '%humidity%' THEN 'TRH'
                                WHEN message ILIKE '%gas%' THEN 'GAS'
                                ELSE COALESCE(source_port, message)
                            END
                        ORDER BY created_at DESC, id DESC
                    ) AS rn
                FROM public.system_logs
                WHERE service_name = 'SENSOR_SERVICE'
                  AND event_type IN ('SENSOR_CONNECTED', 'SENSOR_DISCONNECTED', 'SENSOR_RECONNECTED')
            )
            SELECT event_type, message, source_port, created_at, device_key
            FROM ranked
            WHERE rn = 1
            ORDER BY created_at DESC;
        ";

        return await QueryAsync(sql, MapSensor);
    }

    public async Task<List<SystemLogConnectionStatus>> GetLatestRfidStatusesAsync()
    {
        const string sql = @"
            WITH ranked AS (
                SELECT
                    event_type,
                    message,
                    source_ip,
                    source_port,
                    created_at,
                    ROW_NUMBER() OVER (
                        PARTITION BY COALESCE(source_ip, ''), COALESCE(source_port, '')
                        ORDER BY created_at DESC, id DESC
                    ) AS rn
                FROM public.system_logs
                WHERE service_name = 'RFID_SERVICE'
                  AND event_type IN (
                        'RFID_READER_CONNECTED',
                        'RFID_READER_DISCONNECTED',
                        'RFID_READER_NOT_CONNECTED'
                  )
            )
            SELECT event_type, message, source_ip, source_port, created_at
            FROM ranked
            WHERE rn = 1
            ORDER BY created_at DESC;
        ";

        return await QueryAsync(sql, MapRfid);
    }

    private async Task<List<SystemLogConnectionStatus>> QueryAsync(
        string sql,
        Func<NpgsqlDataReader, SystemLogConnectionStatus> map)
    {
        var rows = new List<SystemLogConnectionStatus>();

        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    private static SystemLogConnectionStatus MapSensor(NpgsqlDataReader reader)
    {
        string eventType = reader.GetString(0);
        string message = reader.GetString(1);
        string port = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        DateTime createdAt = reader.GetDateTime(3);
        bool connected = eventType is "SENSOR_CONNECTED" or "SENSOR_RECONNECTED";

        string name = message.Contains("gas", StringComparison.OrdinalIgnoreCase)
            ? "Gas sensor"
            : message.Contains("temperature", StringComparison.OrdinalIgnoreCase)
                ? "Temperature / Humidity"
                : "Sensor";

        return new SystemLogConnectionStatus
        {
            DeviceName = ExtractLeadName(message) ?? name,
            LocationDisplay = string.IsNullOrWhiteSpace(port) ? "Port unknown" : port,
            DetailDisplay = $"{FormatEvent(eventType)} · {createdAt:dd-MM-yyyy HH:mm:ss}",
            EventType = eventType,
            Message = message,
            CreatedAt = createdAt,
            IsConnected = connected
        };
    }

    private static SystemLogConnectionStatus MapRfid(NpgsqlDataReader reader)
    {
        string eventType = reader.GetString(0);
        string message = reader.GetString(1);
        string ip = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        string port = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
        DateTime createdAt = reader.GetDateTime(4);
        bool connected = eventType == "RFID_READER_CONNECTED";

        string location = string.IsNullOrWhiteSpace(ip)
            ? port
            : string.IsNullOrWhiteSpace(port) ? ip : $"{ip}:{port}";

        return new SystemLogConnectionStatus
        {
            DeviceName = ExtractQuotedName(message) ?? "RFID Reader",
            LocationDisplay = string.IsNullOrWhiteSpace(location) ? "Location unknown" : location,
            DetailDisplay = $"{FormatEvent(eventType)} · {createdAt:dd-MM-yyyy HH:mm:ss}",
            EventType = eventType,
            Message = message,
            CreatedAt = createdAt,
            IsConnected = connected
        };
    }

    private static string FormatEvent(string eventType) =>
        eventType switch
        {
            "SENSOR_CONNECTED" or "RFID_READER_CONNECTED" => "Connected",
            "SENSOR_RECONNECTED" => "Reconnected",
            "SENSOR_DISCONNECTED" or "RFID_READER_DISCONNECTED" => "Disconnected",
            "RFID_READER_NOT_CONNECTED" => "Not connected",
            _ => eventType
        };

    private static string? ExtractQuotedName(string message)
    {
        int start = message.IndexOf('\'');
        int end = message.IndexOf('\'', start + 1);
        if (start >= 0 && end > start)
        {
            return message.Substring(start + 1, end - start - 1);
        }

        return null;
    }

    private static string? ExtractLeadName(string message)
    {
        int space = message.IndexOf(' ');
        if (space > 0)
        {
            return message[..space];
        }

        return null;
    }
}
