using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

public class ReportService
{
    private readonly ConfigurationService _configurationService = new();

    public async Task<List<EntryExitReportRow>> GetEntryExitReportAsync(
        DateTime fromDate,
        DateTime toDate,
        string? employeeSearch = null,
        string? statusFilter = null)
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

    public async Task<List<ChamberEmployeeReportRow>> GetChamberWiseEmployeesAsync(
        long? chamberId = null,
        string? employeeSearch = null,
        string? activeFilter = null)
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
                    OR (@active = '1' AND e.is_active = TRUE)
                    OR (@active = '0' AND e.is_active = FALSE)
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
}
