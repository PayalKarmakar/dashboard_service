using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

public sealed class SensorConfigurationService
{
    private readonly ConfigurationService _configurationService = new();

    public async Task<List<MasterSensorConfig>> GetMasterSensorsAsync()
    {
        var sensors = new List<MasterSensorConfig>();

        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                sensor_id,
                chamber_id,
                sensor_type,
                sensor_model,
                port,
                device_id,
                baud_rate,
                data_bits,
                parity,
                stop_bits,
                response_timeout_milliseconds,
                enabled
            FROM public.master_sensors
            ORDER BY chamber_id, sensor_id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            sensors.Add(new MasterSensorConfig
            {
                SensorId = reader.GetInt64(0),
                ChamberId = reader.GetInt64(1),
                SensorType = reader.GetString(2),
                SensorModel = reader.GetString(3),
                Port = reader.GetString(4),
                DeviceId = reader.GetInt32(5),
                BaudRate = reader.GetInt32(6),
                DataBits = reader.GetInt32(7),
                Parity = reader.GetString(8),
                StopBits = reader.GetString(9),
                ResponseTimeoutMilliseconds = reader.GetInt32(10),
                Enabled = reader.GetBoolean(11)
            });
        }

        return sensors;
    }

    public async Task UpdateSensorPortAsync(long sensorId, string port)
    {
        if (string.IsNullOrWhiteSpace(port))
        {
            throw new InvalidOperationException("COM port is required.");
        }

        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            UPDATE public.master_sensors
            SET port = @port,
                updated_at = NOW()
            WHERE sensor_id = @sensor_id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("port", port.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("sensor_id", sensorId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateViolationThresholdAsync(long sensorViolationsId, string thresholdValue)
    {
        if (string.IsNullOrWhiteSpace(thresholdValue))
        {
            throw new InvalidOperationException("Threshold value is required.");
        }

        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            UPDATE public.sensor_violations
            SET threshold_value = @threshold_value,
                updated_at = NOW()
            WHERE sensor_violations_id = @sensor_violations_id
              AND ended_at IS NULL;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("threshold_value", decimal.Parse(thresholdValue.Trim()));
        command.Parameters.AddWithValue("sensor_violations_id", sensorViolationsId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<SensorViolationThresholdEdit>> GetActiveViolationThresholdsAsync()
    {
        var rows = new List<SensorViolationThresholdEdit>();

        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                sensor_violations_id,
                parameter,
                threshold_type,
                threshold_value,
                status,
                actual_value_at_start,
                unit
            FROM public.sensor_violations
            WHERE status IN ('WARNING', 'CRITICAL')
              AND ended_at IS NULL
            ORDER BY parameter;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new SensorViolationThresholdEdit
            {
                SensorViolationsId = reader.GetInt64(0),
                Parameter = reader.GetString(1),
                ThresholdType = reader.GetString(2),
                ThresholdValue = reader.GetDecimal(3).ToString("0.####"),
                Status = reader.GetString(4),
                ActualValue = reader.GetDecimal(5).ToString("0.####"),
                Unit = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
            });
        }

        return rows;
    }
}
