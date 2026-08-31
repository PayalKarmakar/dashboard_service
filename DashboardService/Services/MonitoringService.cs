using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

public class MonitoringService
{
    public const string AttentionType = "ATTENTION";
    public const string WarningType = "WARNING";
    public const string ViolationType = "VIOLATION";
    public const string ViolationRepeatType = "VIOLATION_REPEAT";
    public const string LegacyViolationType = "TIME_THRESHOLD";

    private readonly ConfigurationService _configurationService = new();
    private readonly AlertMessageService _alertMessageService = new();

    public async Task<List<Employee>> GetMembersInsideAsync()
    {
        var settings = _configurationService.GetAlertSettings();
        var members = new List<Employee>();

        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                t.id,
                t.employee_id,
                t.employee_name,
                COALESCE(c.chamber_name, ''),
                t.entry_time,
                t.alert_triggered,
                t.last_announcement_at
            FROM public.rfid_transactions t
            LEFT JOIN public.master_chambers c
                ON c.chamber_id = t.chamber_id
            WHERE t.status = 'OPEN'
              AND t.exit_time IS NULL
            ORDER BY t.entry_time;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            members.Add(new Employee
            {
                TransactionId = reader.GetInt64(0),
                EmployeeId = reader.GetInt64(1),
                EmployeeName = reader.GetString(2),
                ChamberName = reader.GetString(3),
                EntryTime = reader.GetDateTime(4),
                TimeThresholdMinutes = settings.AfterMinutes,
                AttentionMinutes = settings.AttentionMinutes,
                WarningRemainingMinutes = settings.WarningRemainingMinutes,
                AlertTriggered = reader.GetBoolean(5),
                LastAnnouncementAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6)
            });
        }

        await reader.CloseAsync();

        if (members.Count == 0)
        {
            return members;
        }

        var announcedTypes = await GetAnnouncementTypesAsync(
            connection,
            members.Select(x => x.TransactionId).ToList());

        foreach (var member in members)
        {
            if (announcedTypes.TryGetValue(member.TransactionId, out var types))
            {
                member.AnnouncedTypes = types;
            }
        }

        return members;
    }

    public async Task<List<ChamberDashboard>> GetChamberOccupancyAsync()
    {
        var chambers = new List<ChamberDashboard>();
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                c.chamber_id,
                c.chamber_code,
                c.chamber_name,
                COALESCE(c.member_threshold, 0),
                COUNT(t.id) FILTER (
                    WHERE t.status = 'OPEN' AND t.exit_time IS NULL
                ) AS member_count
            FROM public.master_chambers c
            LEFT JOIN public.rfid_transactions t
                ON t.chamber_id = c.chamber_id
            WHERE c.is_active = TRUE
            GROUP BY
                c.chamber_id,
                c.chamber_code,
                c.chamber_name,
                c.member_threshold
            ORDER BY c.chamber_id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            chambers.Add(new ChamberDashboard
            {
                ChamberId = reader.GetInt64(0),
                ChamberCode = reader.GetString(1),
                ChamberName = reader.GetString(2),
                MemberThreshold = reader.GetInt32(3),
                MemberCount = Convert.ToInt32(reader.GetInt64(4))
            });
        }

        return chambers;
    }

    public async Task<List<AnnouncementRequest>> GetUnplayedAnnouncementsAsync()
    {
        var announcements = new List<AnnouncementRequest>();

        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                a.alert_id,
                a.announcement_message,
                a.rfid_transaction_id,
                a.alert_type,
                t.employee_name,
                COALESCE(c.chamber_name, '')
            FROM public.rfid_transaction_alerts a
            INNER JOIN public.rfid_transactions t
                ON t.id = a.rfid_transaction_id
            LEFT JOIN public.master_chambers c
                ON c.chamber_id = t.chamber_id
            WHERE a.announcement_played = FALSE
              AND t.status = 'OPEN'
              AND t.exit_time IS NULL
            ORDER BY a.created_at;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            string alertType = reader.GetString(3);
            var employee = new Employee
            {
                TransactionId = reader.GetInt64(2),
                EmployeeName = reader.GetString(4),
                ChamberName = reader.GetString(5),
                TimeThresholdMinutes = _configurationService.GetAlertSettings().AfterMinutes,
                AttentionMinutes = _configurationService.GetAlertSettings().AttentionMinutes,
                WarningRemainingMinutes = _configurationService.GetAlertSettings().WarningRemainingMinutes
            };

            AnnouncementRequest? announcement = await BuildAnnouncementRequestAsync(
                alertType,
                employee,
                reader.GetInt64(0),
                reader.GetString(1));

            if (announcement != null)
            {
                announcements.Add(announcement);
            }
        }

        return announcements;
    }

    public async Task MarkAnnouncementPlayedAsync(long alertId)
    {
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            UPDATE public.rfid_transaction_alerts
            SET announcement_played = TRUE
            WHERE alert_id = @alertId
              AND announcement_played = FALSE;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("alertId", alertId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<AnnouncementRequest?> TryCreateDueAnnouncementAsync(Employee employee)
    {
        if (employee.TransactionId <= 0)
        {
            return null;
        }

        var settings = _configurationService.GetAlertSettings();
        double elapsedMinutes = (DateTime.Now - employee.EntryTime).TotalMinutes;

        if (elapsedMinutes >= settings.AfterMinutes)
        {
            bool hasViolationRecord =
                employee.HasAnnouncement(ViolationType) ||
                employee.HasAnnouncement(LegacyViolationType) ||
                employee.AlertTriggered;

            bool heardViolationAudio =
                employee.HasAnnouncement(ViolationType) ||
                employee.HasAnnouncement(ViolationRepeatType);

            if (!hasViolationRecord)
            {
                return await CreateAnnouncementAsync(
                    employee,
                    ViolationType,
                    markViolation: true);
            }

            if (!heardViolationAudio && !employee.LastAnnouncementAt.HasValue)
            {
                return await CreateAnnouncementAsync(
                    employee,
                    ViolationType,
                    markViolation: true);
            }

            bool repeatDue =
                !employee.LastAnnouncementAt.HasValue ||
                DateTime.Now - employee.LastAnnouncementAt.Value >=
                    TimeSpan.FromMinutes(settings.RepeatAfterViolationMinutes);

            if (repeatDue)
            {
                return await CreateAnnouncementAsync(
                    employee,
                    ViolationRepeatType,
                    markViolation: true);
            }

            return null;
        }

        if (elapsedMinutes >= settings.WarningAtMinutes)
        {
            if (employee.HasAnnouncement(WarningType))
            {
                return null;
            }

            return await CreateAnnouncementAsync(
                employee,
                WarningType,
                markViolation: false);
        }

        if (elapsedMinutes >= settings.AttentionMinutes)
        {
            if (employee.HasAnnouncement(AttentionType))
            {
                return null;
            }

            return await CreateAnnouncementAsync(
                employee,
                AttentionType,
                markViolation: false);
        }

        return null;
    }

    private async Task<Dictionary<long, HashSet<string>>> GetAnnouncementTypesAsync(NpgsqlConnection connection,List<long> transactionIds)
    {
        var result = new Dictionary<long, HashSet<string>>();

        const string sql = @"
            SELECT rfid_transaction_id, alert_type
            FROM public.rfid_transaction_alerts
            WHERE rfid_transaction_id = ANY(@ids);
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("ids", transactionIds.ToArray());

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            long id = reader.GetInt64(0);
            string type = reader.GetString(1);

            if (!result.TryGetValue(id, out var types))
            {
                types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[id] = types;
            }

            types.Add(type);
        }

        return result;
    }

    private async Task<long> SaveAnnouncementAsync(Employee employee, string alertType, string message,bool markViolation)
    {
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();
        await using var dbTransaction = await connection.BeginTransactionAsync();

        const string insertSql = @"
            INSERT INTO public.rfid_transaction_alerts
            (
                rfid_transaction_id,
                alert_type,
                announcement_message,
                announcement_played
            )
            VALUES
            (
                @id,
                @alertType,
                @message,
                FALSE
            )
            RETURNING alert_id;
        ";

        await using var insertCommand = new NpgsqlCommand(insertSql, connection, dbTransaction);
        insertCommand.Parameters.AddWithValue("id", employee.TransactionId);
        insertCommand.Parameters.AddWithValue("alertType", alertType);
        insertCommand.Parameters.AddWithValue("message", message);
        long alertId = Convert.ToInt64(await insertCommand.ExecuteScalarAsync());

        const string updateSql = @"
            UPDATE public.rfid_transactions
            SET
                last_announcement_at = NOW(),
                updated_at = NOW(),
                alert_triggered = CASE WHEN @markViolation THEN TRUE ELSE alert_triggered END,
                alert_triggered_at = CASE
                    WHEN @markViolation AND alert_triggered = FALSE THEN NOW()
                    ELSE alert_triggered_at
                END
            WHERE id = @id;
        ";

        await using var updateCommand = new NpgsqlCommand(updateSql, connection, dbTransaction);
        updateCommand.Parameters.AddWithValue("id", employee.TransactionId);
        updateCommand.Parameters.AddWithValue("markViolation", markViolation);
        await updateCommand.ExecuteNonQueryAsync();

        await dbTransaction.CommitAsync();

        employee.AnnouncedTypes.Add(alertType);
        employee.LastAnnouncementAt = DateTime.Now;
        if (markViolation)
        {
            employee.AlertTriggered = true;
        }

        return alertId;
    }

    public static string FormatMessage(string template, Employee employee, AlertSettings settings)
    {
        return template
            .Replace("{EmployeeName}", employee.EmployeeName, StringComparison.OrdinalIgnoreCase)
            .Replace("$employeename", employee.EmployeeName, StringComparison.OrdinalIgnoreCase)
            .Replace("{ChamberName}", employee.ChamberName, StringComparison.OrdinalIgnoreCase)
            .Replace("{AttentionMinutes}", settings.AttentionMinutes.ToString())
            .Replace("{WarningRemainingMinutes}", settings.WarningRemainingMinutes.ToString())
            .Replace("{AfterMinutes}", settings.AfterMinutes.ToString());
    }

    public static string FormatSensorMessage(string template, string parameter, string chamberName)
    {
        return template
            .Replace("{Parameter}", parameter, StringComparison.OrdinalIgnoreCase)
            .Replace("{ChamberName}", chamberName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<AnnouncementRequest?> CreateAnnouncementAsync(
        Employee employee,
        string alertType,
        bool markViolation)
    {
        var settings = _configurationService.GetAlertSettings();
        var templates = await _alertMessageService.GetTemplatesAsync(
            AlertMessageService.CategoryEmployee,
            alertType);

        if (!templates.TryGetValue(AlertMessageService.CultureEnglishIndia, out string? primaryTemplate) ||
            string.IsNullOrWhiteSpace(primaryTemplate))
        {
            primaryTemplate = alertType.ToUpperInvariant() switch
            {
                AttentionType => settings.AttentionMessage,
                WarningType => settings.WarningMessage,
                ViolationType => settings.ViolationMessage,
                ViolationRepeatType => settings.ViolationRepeatMessage,
                _ => string.Empty
            };
        }

        if (string.IsNullOrWhiteSpace(primaryTemplate))
        {
            return null;
        }

        string primaryMessage = FormatMessage(primaryTemplate, employee, settings);
        long alertId = await SaveAnnouncementAsync(
            employee,
            alertType,
            primaryMessage,
            markViolation);

        string? secondaryMessage = null;
        if (templates.TryGetValue(AlertMessageService.CultureBengaliIndia, out string? secondaryTemplate) &&
            !string.IsNullOrWhiteSpace(secondaryTemplate))
        {
            secondaryMessage = FormatMessage(secondaryTemplate, employee, settings);
        }

        return new AnnouncementRequest
        {
            AlertId = alertId,
            Message = primaryMessage,
            SecondaryMessage = secondaryMessage,
            MessageCulture = AlertMessageService.CultureEnglishIndia,
            SecondaryCulture = AlertMessageService.CultureBengaliIndia,
            TransactionId = employee.TransactionId
        };
    }

    private async Task<AnnouncementRequest?> BuildAnnouncementRequestAsync(
        string alertType,
        Employee employee,
        long alertId,
        string primaryMessage)
    {
        var settings = _configurationService.GetAlertSettings();
        var templates = await _alertMessageService.GetTemplatesAsync(
            AlertMessageService.CategoryEmployee,
            alertType);

        string? secondaryMessage = null;
        if (templates.TryGetValue(AlertMessageService.CultureBengaliIndia, out string? secondaryTemplate) &&
            !string.IsNullOrWhiteSpace(secondaryTemplate))
        {
            secondaryMessage = FormatMessage(secondaryTemplate, employee, settings);
        }

        return new AnnouncementRequest
        {
            AlertId = alertId,
            Message = primaryMessage,
            SecondaryMessage = secondaryMessage,
            MessageCulture = AlertMessageService.CultureEnglishIndia,
            SecondaryCulture = AlertMessageService.CultureBengaliIndia,
            TransactionId = employee.TransactionId
        };
    }

    //Payal
    public async Task<SensorReading?> GetLatestSensorReadingAsync(long chamberId)
    {
        await using var connection =
            new NpgsqlConnection(
                _configurationService.GetConnectionString());

        await connection.OpenAsync();

        const string sql = @"
        SELECT
            temperature,
            humidity,
            co,
            co2,
            o2,
            recorded_at
        FROM public.sensor_readings
        WHERE chamber_id = @chamber_id
        ORDER BY recorded_at DESC
        LIMIT 1;
    ";

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "chamber_id",
            chamberId);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new SensorReading
        {
            Temperature = reader.IsDBNull(0)
                ? null
                : reader.GetDecimal(0),

            Humidity = reader.IsDBNull(1)
                ? null
                : reader.GetDecimal(1),

            CO = reader.IsDBNull(2)
                ? null
                : reader.GetDecimal(2),

            CO2 = reader.IsDBNull(3)
                ? null
                : reader.GetDecimal(3),

            O2 = reader.IsDBNull(4)
                ? null
                : reader.GetDecimal(4),

            RecordedAt = reader.GetDateTime(5)
        };
    }
   
    public async Task<List<SensorViolation>> GetActiveSensorViolationsAsync(long chamberId)
    {
        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());

        await connection.OpenAsync();

        const string sql = @"
        SELECT
            sensor_violations_id,
            chamber_id,
            sensor_model,
            sensor_type,
            parameter,
            unit,
            creation_severity,
            final_severity,
            threshold_type,
            threshold_value,
            actual_value_at_start,
            started_at,
            ended_at,
            duration_seconds,
            status,
            last_announced_at,
            last_announced_severity
        FROM public.sensor_violations
        WHERE chamber_id = @chamber_id
          AND status IN ('WARNING', 'CRITICAL')
          AND ended_at IS NULL
        ORDER BY started_at DESC;
    ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("chamber_id", chamberId);

        await using var reader = await command.ExecuteReaderAsync();

        var violations = new List<SensorViolation>();

        while (await reader.ReadAsync())
        {
            violations.Add(new SensorViolation
            {
                SensorViolationsId = reader.GetInt64(0),
                ChamberId = reader.GetInt64(1),
                SensorModel = reader.GetString(2),
                SensorType = reader.GetString(3),
                Parameter = reader.GetString(4),
                Unit = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreationSeverity = reader.IsDBNull(6) ? null : reader.GetString(6),
                FinalSeverity = reader.IsDBNull(7) ? null : reader.GetString(7),
                ThresholdType = reader.GetString(8),
                ThresholdValue = reader.GetDecimal(9),
                ActualValueAtStart = reader.GetDecimal(10),
                StartedAt = reader.GetDateTime(11),
                EndedAt = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                DurationSeconds = reader.IsDBNull(13) ? null : reader.GetInt64(13),
                Status = reader.GetString(14),
                LastAnnouncedAt = reader.IsDBNull(15) ? null : reader.GetDateTime(15),
                LastAnnouncedSeverity = reader.IsDBNull(16) ? null : reader.GetString(16)
            });
        }

        await reader.CloseAsync();

        if (violations.Count == 0)
        {
            return violations;
        }

        await RefreshOpenViolationSeveritiesAsync(connection, chamberId, violations);
        return violations;
    }

    private async Task RefreshOpenViolationSeveritiesAsync(
        NpgsqlConnection connection,
        long chamberId,
        List<SensorViolation> violations)
    {
        var thresholds = await LoadThresholdLookupAsync(connection);
        SensorReading? latest = await LoadLatestReadingAsync(connection, chamberId);

        foreach (var violation in violations)
        {
            try
            {
                decimal actual = ResolveCurrentValue(violation, latest) ?? violation.ActualValueAtStart;

                string? evaluated = EvaluateSeverity(
                    violation.Parameter,
                    violation.ThresholdType,
                    actual,
                    thresholds,
                    out decimal? breachedLimit);

                // Prefer freshly evaluated severity; otherwise keep the strongest known severity.
                string effective = PickStrongestSeverity(
                    evaluated,
                    violation.FinalSeverity,
                    violation.CreationSeverity,
                    violation.Status) ?? violation.Status;

                effective = effective.ToUpperInvariant();

                bool statusChanged =
                    !effective.Equals(violation.Status, StringComparison.OrdinalIgnoreCase);
                bool thresholdChanged =
                    breachedLimit.HasValue &&
                    breachedLimit.Value != violation.ThresholdValue;

                if (statusChanged || thresholdChanged)
                {
                    await using var update = new NpgsqlCommand(@"
                        UPDATE public.sensor_violations
                        SET status = @status,
                            final_severity = @status,
                            threshold_value = COALESCE(@threshold_value, threshold_value),
                            updated_at = NOW()
                        WHERE sensor_violations_id = @id
                          AND ended_at IS NULL;", connection);

                    update.Parameters.AddWithValue("status", effective);
                    var thresholdParam = update.Parameters.Add("threshold_value", NpgsqlTypes.NpgsqlDbType.Numeric);
                    thresholdParam.Value = breachedLimit.HasValue
                        ? breachedLimit.Value
                        : DBNull.Value;
                    update.Parameters.AddWithValue("id", violation.SensorViolationsId);
                    await update.ExecuteNonQueryAsync();
                }

                violation.Status = effective;
                violation.FinalSeverity = effective;
                if (breachedLimit.HasValue)
                {
                    violation.ThresholdValue = breachedLimit.Value;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Failed to refresh severity for {violation.Parameter}: {ex.Message}");
            }
        }
    }

    private static async Task<Dictionary<string, ThresholdLimits>> LoadThresholdLookupAsync(
        NpgsqlConnection connection)
    {
        var map = new Dictionary<string, ThresholdLimits>(StringComparer.OrdinalIgnoreCase);

        const string sql = @"
            SELECT parameter, warning_low, warning_high, critical_low, critical_high
            FROM public.sensor_thresholds
            WHERE is_active = TRUE AND enabled = TRUE;";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            map[reader.GetString(0)] = new ThresholdLimits(
                WarningLow: reader.IsDBNull(1) ? null : reader.GetDecimal(1),
                WarningHigh: reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                CriticalLow: reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                CriticalHigh: reader.IsDBNull(4) ? null : reader.GetDecimal(4));
        }

        return map;
    }

    private static async Task<SensorReading?> LoadLatestReadingAsync(
        NpgsqlConnection connection,
        long chamberId)
    {
        const string sql = @"
            SELECT temperature, humidity, co, co2, o2, recorded_at
            FROM public.sensor_readings
            WHERE chamber_id = @chamber_id
            ORDER BY recorded_at DESC
            LIMIT 1;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("chamber_id", chamberId);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new SensorReading
        {
            Temperature = reader.IsDBNull(0) ? null : reader.GetDecimal(0),
            Humidity = reader.IsDBNull(1) ? null : reader.GetDecimal(1),
            CO = reader.IsDBNull(2) ? null : reader.GetDecimal(2),
            CO2 = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
            O2 = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
            RecordedAt = reader.GetDateTime(5)
        };
    }

    private static decimal? ResolveCurrentValue(SensorViolation violation, SensorReading? reading)
    {
        if (reading == null)
        {
            return null;
        }

        return violation.Parameter.ToUpperInvariant() switch
        {
            "TEMPERATURE" => reading.Temperature,
            "HUMIDITY" => reading.Humidity,
            "CO" => reading.CO,
            "CO2" => reading.CO2,
            "O2" or "OXYGEN" => reading.O2,
            _ => null
        };
    }

    private static string? EvaluateSeverity(
        string parameter,
        string thresholdType,
        decimal actual,
        IReadOnlyDictionary<string, ThresholdLimits> thresholds,
        out decimal? breachedLimit)
    {
        breachedLimit = null;

        if (!thresholds.TryGetValue(parameter, out ThresholdLimits? limits))
        {
            return null;
        }

        bool isLow =
            thresholdType.Equals("LOW", StringComparison.OrdinalIgnoreCase) ||
            parameter.Equals("O2", StringComparison.OrdinalIgnoreCase) ||
            parameter.Equals("Oxygen", StringComparison.OrdinalIgnoreCase);

        if (isLow)
        {
            if (limits.CriticalLow.HasValue && actual <= limits.CriticalLow.Value)
            {
                breachedLimit = limits.CriticalLow;
                return "CRITICAL";
            }

            if (limits.WarningLow.HasValue && actual <= limits.WarningLow.Value)
            {
                breachedLimit = limits.WarningLow;
                return "WARNING";
            }

            return null;
        }

        if (limits.CriticalHigh.HasValue && actual >= limits.CriticalHigh.Value)
        {
            breachedLimit = limits.CriticalHigh;
            return "CRITICAL";
        }

        if (limits.WarningHigh.HasValue && actual >= limits.WarningHigh.Value)
        {
            breachedLimit = limits.WarningHigh;
            return "WARNING";
        }

        return null;
    }

    private static string? PickStrongestSeverity(params string?[] values)
    {
        string? best = null;
        int bestRank = -1;

        foreach (string? value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            int rank = value.ToUpperInvariant() switch
            {
                "CRITICAL" => 2,
                "WARNING" => 1,
                _ => 0
            };

            if (rank > bestRank)
            {
                bestRank = rank;
                best = value.ToUpperInvariant();
            }
        }

        return best;
    }

    private sealed record ThresholdLimits(
        decimal? WarningLow,
        decimal? WarningHigh,
        decimal? CriticalLow,
        decimal? CriticalHigh);

    public async Task ClearSensorAnnouncementMarksAsync(long chamberId)
    {
        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            UPDATE public.sensor_violations
            SET last_announced_at = NULL,
                last_announced_severity = NULL,
                updated_at = NOW()
            WHERE chamber_id = @chamber_id
              AND ended_at IS NULL
              AND status IN ('WARNING', 'CRITICAL');
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("chamber_id", chamberId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task MarkSensorViolationAnnouncedAsync(long sensorViolationId,string severity)
    {
        await using var connection =
            new NpgsqlConnection(
                _configurationService.GetConnectionString());

        await connection.OpenAsync();

        const string sql = @"
        UPDATE public.sensor_violations
        SET
            last_announced_at = now(),
            last_announced_severity = @severity
        WHERE sensor_violations_id = @sensor_violation_id;
    ";

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "sensor_violation_id",
            sensorViolationId);

        command.Parameters.AddWithValue(
            "severity",
            severity);

        await command.ExecuteNonQueryAsync();
    }

}
