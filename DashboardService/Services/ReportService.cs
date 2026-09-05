using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

public class ReportService
{
    private readonly ConfigurationService _configurationService = new();

    public async Task<List<EntryExitReportRow>> GetEntryExitReportAsync(DateTime fromDate, DateTime toDate,string? employeeSearch = null, string? statusFilter = null)
    {
        var rows = new List<EntryExitReportRow>();

        DateTime from = fromDate.Date;
        DateTime to = toDate.Date.AddDays(1).AddTicks(-1);

        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                t.id,
                COALESCE(e.employee_code, ''),
                COALESCE(t.employee_name, ''),
                COALESCE(t.card_uid, ''),
                COALESCE(c.chamber_name, ''),
                t.entry_time,
                t.exit_time,
                COALESCE(t.status, '')
            FROM public.rfid_transactions t
            LEFT JOIN public.master_employees e
                ON e.emp_id = t.employee_id
            LEFT JOIN public.master_chambers c
                ON c.chamber_id = t.chamber_id
            WHERE t.entry_time >= @fromDate
              AND t.entry_time <= @toDate
              AND (
                    @search = ''
                    OR LOWER(COALESCE(t.employee_name, '')) LIKE @searchLike
                    OR LOWER(COALESCE(e.employee_code, '')) LIKE @searchLike
                    OR LOWER(COALESCE(t.card_uid, '')) LIKE @searchLike
                  )
              AND (
                    @status = ''
                    OR LOWER(COALESCE(t.status, '')) = LOWER(@status)
                  )
            ORDER BY t.entry_time DESC;
        ";

        string search = (employeeSearch ?? string.Empty).Trim();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("fromDate", from);
        command.Parameters.AddWithValue("toDate", to);
        command.Parameters.AddWithValue("search", search);
        command.Parameters.AddWithValue("searchLike", $"%{search.ToLowerInvariant()}%");
        command.Parameters.AddWithValue("status", (statusFilter ?? string.Empty).Trim());

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new EntryExitReportRow
            {
                TransactionId = reader.GetInt64(0),
                EmployeeCode = reader.GetString(1),
                EmployeeName = reader.GetString(2),
                CardUid = reader.GetString(3),
                ChamberName = reader.GetString(4),
                EntryTime = reader.GetDateTime(5),
                ExitTime = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                Status = reader.GetString(7)
            });
        }

        return rows;
    }

    public async Task<List<ChamberEmployeeReportRow>> GetChamberWiseEmployeesAsync(long? chamberId = null,string? employeeSearch = null, string? activeFilter = null)
    {
        var rows = new List<ChamberEmployeeReportRow>();

        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                COALESCE(c.chamber_code, ''),
                COALESCE(c.chamber_name, ''),
                e.employee_code,
                e.employee_name,
                COALESCE(e.card_uid, ''),
                COALESCE(e.department, ''),
                COALESCE(e.designation, ''),
                COALESCE(e.mobile, ''),
                e.is_active
            FROM public.master_employees e
            LEFT JOIN public.master_chambers c
                ON c.chamber_id = e.chamber_id
            WHERE (
                    @chamberId = 0
                    OR e.chamber_id = @chamberId
                  )
              AND (
                    @search = ''
                    OR LOWER(COALESCE(e.employee_name, '')) LIKE @searchLike
                    OR LOWER(COALESCE(e.employee_code, '')) LIKE @searchLike
                    OR LOWER(COALESCE(e.card_uid, '')) LIKE @searchLike
                    OR LOWER(COALESCE(c.chamber_name, '')) LIKE @searchLike
                  )
              AND (
                    @active = ''
                    OR (@active = '1' AND e.is_active = TRUE AND e.is_lost = FALSE)
                    OR (@active = '0' AND (e.is_active = FALSE OR e.is_lost = TRUE))
                  )
            ORDER BY
                COALESCE(c.chamber_name, 'zzz'),
                e.employee_name;
        ";

        string search = (employeeSearch ?? string.Empty).Trim();
        string active = (activeFilter ?? string.Empty).Trim();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("chamberId", chamberId ?? 0L);
        command.Parameters.AddWithValue("search", search);
        command.Parameters.AddWithValue("searchLike", $"%{search.ToLowerInvariant()}%");
        command.Parameters.AddWithValue("active", active);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new ChamberEmployeeReportRow
            {
                ChamberCode = reader.GetString(0),
                ChamberName = reader.GetString(1),
                EmployeeCode = reader.GetString(2),
                EmployeeName = reader.GetString(3),
                CardUid = reader.GetString(4),
                Department = reader.GetString(5),
                Designation = reader.GetString(6),
                Mobile = reader.GetString(7),
                IsActive = reader.GetBoolean(8)
            });
        }

        return rows;
    }

    public async Task<List<ChamberCriticalReportRow>> GetChamberCriticalReportAsync(DateTime fromDate, DateTime toDate,long? chamberId = null,string? parameterSearch = null,string? severityFilter = null)
    {
        var violations = await GetSensorViolationRecordsAsync(fromDate, toDate,chamberId, parameterSearch, severityFilter);
        DateTime rangeStart = fromDate.Date;
        DateTime rangeEnd = toDate.Date.AddDays(1).AddTicks(-1);

        return violations
            .Select(v => MapCriticalRow(v, rangeStart, rangeEnd))
            .OrderByDescending(r => r.StartedAt)
            .ToList();
    }

    public async Task<List<ProductionLossReportRow>> GetProductionLossReportAsync(DateTime fromDate, DateTime toDate, long? chamberId = null)
    {
        var violations = await GetSensorViolationRecordsAsync(
            fromDate,
            toDate,
            chamberId,
            parameterSearch: null,
            severityFilter: null);
        DateTime rangeStart = fromDate.Date;
        DateTime rangeEnd = toDate.Date.AddDays(1).AddTicks(-1);

        var intervalsByChamber = violations
            .GroupBy(v => v.ChamberId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(v => ClipInterval(v.StartedAt, v.EndedAt, rangeStart, rangeEnd)).ToList());

        var rows = new List<ProductionLossReportRow>();

        foreach (var group in intervalsByChamber)
        {
            var chamberViolations = violations.Where(v => v.ChamberId == group.Key).ToList();
            var sample = chamberViolations[0];

            foreach (var merged in MergeIntervals(group.Value))
            {
                bool ongoing = chamberViolations.Any(v =>
                    v.EndedAt == null
                    && v.StartedAt <= merged.End
                    && merged.Start <= DateTime.Now);

                rows.Add(new ProductionLossReportRow
                {
                    ChamberId = group.Key,
                    ChamberCode = sample.ChamberCode,
                    ChamberName = sample.ChamberName,
                    LossStartedAt = merged.Start,
                    LossEndedAt = ongoing ? null : merged.End,
                    IsOngoing = ongoing,
                    Duration = merged.End - merged.Start
                });
            }
        }

        return rows
            .OrderByDescending(r => r.LossStartedAt)
            .ThenBy(r => r.ChamberName)
            .ToList();
    }

    public static string FormatTotalDuration(IEnumerable<ProductionLossReportRow> rows)
    {
        var total = TimeSpan.FromSeconds(rows.Sum(r => Math.Max(0, r.Duration.TotalSeconds)));
        return ReportDurationFormatter.Format(total);
    }

    public async Task<List<SensorReadingReportRow>> GetSensorReadingsReportAsync(
        DateTime fromDate,
        DateTime toDate,
        long? chamberId = null)
    {
        var rows = new List<SensorReadingReportRow>();

        DateTime from = fromDate.Date;
        DateTime to = toDate.Date.AddDays(1).AddTicks(-1);

        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                sr.reading_id,
                sr.chamber_id,
                COALESCE(c.chamber_code, ''),
                COALESCE(c.chamber_name, ''),
                sr.temperature,
                sr.humidity,
                sr.co,
                sr.co2,
                sr.o2,
                sr.recorded_at
            FROM public.sensor_readings sr
            LEFT JOIN public.master_chambers c
                ON c.chamber_id = sr.chamber_id
            WHERE sr.recorded_at >= @fromDate
              AND sr.recorded_at <= @toDate
              AND (@chamberId = 0 OR sr.chamber_id = @chamberId)
            ORDER BY sr.reading_id DESC;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("fromDate", from);
        command.Parameters.AddWithValue("toDate", to);
        command.Parameters.AddWithValue("chamberId", chamberId ?? 0L);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new SensorReadingReportRow
            {
                ReadingId = reader.GetInt64(0),
                ChamberId = reader.GetInt64(1),
                ChamberCode = reader.GetString(2),
                ChamberName = reader.GetString(3),
                Temperature = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                Humidity = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                CO = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                CO2 = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                O2 = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                RecordedAt = reader.GetDateTime(9)
            });
        }

        return rows;
    }

    public async Task<List<SystemLogReportRow>> GetSystemLogsAsync(
        DateTime fromDate,
        DateTime toDate,
        string? statusFilter = null)
    {
        var rows = new List<SystemLogReportRow>();

        DateTime from = fromDate.Date;
        DateTime to = toDate.Date.AddDays(1).AddTicks(-1);
        string filter = (statusFilter ?? string.Empty).Trim().ToUpperInvariant();

        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                sl.log_id,
                sl.created_at,
                COALESCE(sl.service_name, ''),
                COALESCE(sl.log_level, ''),
                COALESCE(sl.event_type, ''),
                COALESCE(sl.message, ''),
                COALESCE(sl.source_port, '')
            FROM public.system_logs sl
            WHERE sl.created_at >= @fromDate
              AND sl.created_at <= @toDate
              AND (
                    @filter = ''
                    OR (@filter = 'CONNECTED'
                        AND UPPER(sl.event_type) LIKE '%CONNECTED%'
                        AND UPPER(sl.event_type) NOT LIKE '%DISCONNECTED%')
                    OR (@filter = 'DISCONNECTED'
                        AND UPPER(sl.event_type) LIKE '%DISCONNECTED%')
                    OR (@filter = 'RECONNECTED'
                        AND UPPER(sl.event_type) LIKE '%RECONNECTED%')
                  )
            ORDER BY sl.created_at DESC, sl.log_id DESC;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("fromDate", from);
        command.Parameters.AddWithValue("toDate", to);
        command.Parameters.AddWithValue("filter", filter);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new SystemLogReportRow
            {
                LogId = reader.GetInt64(0),
                CreatedAt = reader.GetDateTime(1),
                ServiceName = reader.GetString(2),
                LogLevel = reader.GetString(3),
                EventType = reader.GetString(4),
                Message = reader.GetString(5),
                SourcePort = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
            });
        }

        return rows;
    }

    private async Task<List<SensorViolationRecord>> GetSensorViolationRecordsAsync(
        DateTime fromDate,
        DateTime toDate,
        long? chamberId,
        string? parameterSearch,
        string? severityFilter)
    {
        var records = new List<SensorViolationRecord>();

        DateTime from = fromDate.Date;
        DateTime to = toDate.Date.AddDays(1).AddTicks(-1);

        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                sv.sensor_violations_id,
                sv.chamber_id,
                COALESCE(c.chamber_code, ''),
                COALESCE(c.chamber_name, ''),
                sv.parameter,
                sv.unit,
                sv.actual_value_at_start,
                sv.threshold_value,
                sv.started_at,
                sv.ended_at,
                UPPER(COALESCE(
                    NULLIF(TRIM(sv.final_severity), ''),
                    NULLIF(TRIM(sv.creation_severity), ''),
                    NULLIF(TRIM(sv.status), ''),
                    'WARNING'
                )) AS severity
            FROM public.sensor_violations sv
            LEFT JOIN public.master_chambers c
                ON c.chamber_id = sv.chamber_id
            WHERE UPPER(COALESCE(
                    NULLIF(TRIM(sv.final_severity), ''),
                    NULLIF(TRIM(sv.creation_severity), ''),
                    NULLIF(TRIM(sv.status), ''),
                    ''
                  )) IN ('WARNING', 'CRITICAL')
              AND sv.started_at <= @toDate
              AND (sv.ended_at IS NULL OR sv.ended_at >= @fromDate)
              AND (@chamberId = 0 OR sv.chamber_id = @chamberId)
              AND (
                    @parameterSearch = ''
                    OR LOWER(sv.parameter) LIKE @parameterLike
                  )
              AND (
                    @severity = ''
                    OR UPPER(COALESCE(
                        NULLIF(TRIM(sv.final_severity), ''),
                        NULLIF(TRIM(sv.creation_severity), ''),
                        NULLIF(TRIM(sv.status), ''),
                        ''
                    )) = UPPER(@severity)
                  )
            ORDER BY sv.started_at DESC;
        ";

        string parameter = (parameterSearch ?? string.Empty).Trim();
        string severity = (severityFilter ?? string.Empty).Trim();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("fromDate", from);
        command.Parameters.AddWithValue("toDate", to);
        command.Parameters.AddWithValue("chamberId", chamberId ?? 0L);
        command.Parameters.AddWithValue("parameterSearch", parameter);
        command.Parameters.AddWithValue("parameterLike", $"%{parameter.ToLowerInvariant()}%");
        command.Parameters.AddWithValue("severity", severity);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            records.Add(new SensorViolationRecord
            {
                SensorViolationId = reader.GetInt64(0),
                ChamberId = reader.GetInt64(1),
                ChamberCode = reader.GetString(2),
                ChamberName = reader.GetString(3),
                Parameter = reader.GetString(4),
                Unit = reader.IsDBNull(5) ? null : reader.GetString(5),
                ActualValueAtStart = reader.GetDecimal(6),
                ThresholdValue = reader.GetDecimal(7),
                StartedAt = reader.GetDateTime(8),
                EndedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                Severity = reader.GetString(10)
            });
        }

        return records;
    }

    private static ChamberCriticalReportRow MapCriticalRow(
        SensorViolationRecord violation,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        var interval = ClipInterval(violation.StartedAt, violation.EndedAt, rangeStart, rangeEnd);
        bool ongoing = violation.EndedAt == null;

        return new ChamberCriticalReportRow
        {
            SensorViolationId = violation.SensorViolationId,
            ChamberId = violation.ChamberId,
            ChamberCode = violation.ChamberCode,
            ChamberName = violation.ChamberName,
            Parameter = violation.Parameter,
            Unit = violation.Unit,
            ActualValueAtStart = violation.ActualValueAtStart,
            ThresholdValue = violation.ThresholdValue,
            StartedAt = interval.Start,
            EndedAt = ongoing ? null : interval.End,
            IsOngoing = ongoing,
            Duration = interval.End - interval.Start,
            Severity = violation.Severity
        };
    }

    private static TimeInterval ClipInterval(
        DateTime startedAt,
        DateTime? endedAt,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        DateTime effectiveEnd = endedAt ?? DateTime.Now;
        if (effectiveEnd > rangeEnd)
        {
            effectiveEnd = rangeEnd;
        }

        DateTime effectiveStart = startedAt < rangeStart ? rangeStart : startedAt;
        if (effectiveEnd < effectiveStart)
        {
            effectiveEnd = effectiveStart;
        }

        return new TimeInterval
        {
            Start = effectiveStart,
            End = effectiveEnd
        };
    }

    private static List<TimeInterval> MergeIntervals(List<TimeInterval> intervals)
    {
        if (intervals.Count == 0)
        {
            return intervals;
        }

        var sorted = intervals
            .OrderBy(i => i.Start)
            .ToList();

        var merged = new List<TimeInterval> { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            var current = sorted[i];
            var last = merged[^1];

            if (current.Start <= last.End)
            {
                if (current.End > last.End)
                {
                    last.End = current.End;
                }
            }
            else
            {
                merged.Add(new TimeInterval
                {
                    Start = current.Start,
                    End = current.End
                });
            }
        }

        return merged;
    }

    private sealed class SensorViolationRecord
    {
        public long SensorViolationId { get; set; }

        public long ChamberId { get; set; }

        public string ChamberCode { get; set; } = string.Empty;

        public string ChamberName { get; set; } = string.Empty;

        public string Parameter { get; set; } = string.Empty;

        public string? Unit { get; set; }

        public decimal ActualValueAtStart { get; set; }

        public decimal ThresholdValue { get; set; }

        public DateTime StartedAt { get; set; }

        public DateTime? EndedAt { get; set; }

        public string Severity { get; set; } = string.Empty;
    }

    private sealed class TimeInterval
    {
        public DateTime Start { get; set; }

        public DateTime End { get; set; }
    }
}
