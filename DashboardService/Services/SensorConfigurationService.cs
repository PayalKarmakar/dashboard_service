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

    public async Task<List<SensorThresholdConfig>> GetSensorThresholdsAsync()
    {
        var rows = new List<SensorThresholdConfig>();

        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                thresholds_id,
                sensor_type,
                parameter,
                COALESCE(unit, ''),
                warning_low,
                warning_high,
                critical_low,
                critical_high,
                enabled
            FROM public.sensor_thresholds
            WHERE is_active = TRUE
            ORDER BY parameter;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new SensorThresholdConfig
            {
                ThresholdsId = reader.GetInt64(0),
                SensorType = reader.GetString(1),
                Parameter = reader.GetString(2),
                Unit = reader.GetString(3),
                WarningLow = reader.IsDBNull(4) ? null : reader.GetDecimal(4).ToString("0.####"),
                WarningHigh = reader.IsDBNull(5) ? null : reader.GetDecimal(5).ToString("0.####"),
                CriticalLow = reader.IsDBNull(6) ? null : reader.GetDecimal(6).ToString("0.####"),
                CriticalHigh = reader.IsDBNull(7) ? null : reader.GetDecimal(7).ToString("0.####"),
                Enabled = reader.GetBoolean(8)
            });
        }

        return rows;
    }

    public async Task UpdateSensorThresholdAsync(SensorThresholdConfig row)
    {
        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            UPDATE public.sensor_thresholds
            SET warning_low = @warning_low,
                warning_high = @warning_high,
                critical_low = @critical_low,
                critical_high = @critical_high,
                updated_at = NOW()
            WHERE thresholds_id = @thresholds_id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("warning_low", ParseOptionalDecimal(row.WarningLow));
        command.Parameters.AddWithValue("warning_high", ParseOptionalDecimal(row.WarningHigh));
        command.Parameters.AddWithValue("critical_low", ParseOptionalDecimal(row.CriticalLow));
        command.Parameters.AddWithValue("critical_high", ParseOptionalDecimal(row.CriticalHigh));
        command.Parameters.AddWithValue("thresholds_id", row.ThresholdsId);
        await command.ExecuteNonQueryAsync();
    }

    private static object ParseOptionalDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DBNull.Value;
        }

        if (!decimal.TryParse(value.Trim(), out decimal parsed))
        {
            throw new InvalidOperationException($"Invalid threshold number: '{value}'.");
        }

        return parsed;
    }

    public async Task<List<SensorViolationThresholdEdit>> GetActiveViolationThresholdsAsync()
    {
        // Reuse monitoring refresh so WARNING→CRITICAL stays in sync with sensor_thresholds.
        var monitoring = new MonitoringService();
        var open = await monitoring.GetActiveSensorViolationsAsync(1);

        return open
            .OrderBy(v => v.Parameter)
            .Select(v => new SensorViolationThresholdEdit
            {
                SensorViolationsId = v.SensorViolationsId,
                Parameter = v.Parameter,
                ThresholdType = v.ThresholdType,
                ThresholdValue = v.ThresholdValue.ToString("0.####"),
                Status = v.Status,
                ActualValue = v.ActualValueAtStart.ToString("0.####"),
                Unit = v.Unit ?? string.Empty
            })
            .ToList();
    }
}
