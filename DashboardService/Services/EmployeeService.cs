using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

public class EmployeeService
{
    private readonly ConfigurationService _configurationService = new();

    public async Task<List<EmployeeRecord>> GetAllAsync()
    {
        var employees = new List<EmployeeRecord>();
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                e.emp_id,
                e.employee_code,
                e.employee_name,
                COALESCE(e.card_uid, ''),
                COALESCE(e.department, ''),
                COALESCE(e.designation, ''),
                COALESCE(e.mobile, ''),
                e.chamber_id,
                COALESCE(c.chamber_name, ''),
                e.is_active
            FROM public.master_employees e
            LEFT JOIN public.master_chambers c
                ON c.chamber_id = e.chamber_id
            ORDER BY e.emp_id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            employees.Add(new EmployeeRecord
            {
                EmployeeId = reader.GetInt64(0),
                EmployeeCode = reader.GetString(1),
                EmployeeName = reader.GetString(2),
                CardUid = reader.GetString(3),
                Department = reader.GetString(4),
                Designation = reader.GetString(5),
                Mobile = reader.GetString(6),
                ChamberId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                ChamberName = reader.GetString(8),
                IsActive = reader.GetBoolean(9)
            });
        }

        return employees;
    }

    public async Task<EmployeeRecord?> FindByCardUidAsync(string cardUid)
    {
        if (string.IsNullOrWhiteSpace(cardUid))
        {
            return null;
        }

        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                e.emp_id,
                e.employee_code,
                e.employee_name,
                COALESCE(e.card_uid, ''),
                COALESCE(e.department, ''),
                COALESCE(e.designation, ''),
                COALESCE(e.mobile, ''),
                e.chamber_id,
                COALESCE(c.chamber_name, ''),
                e.is_active
            FROM public.master_employees e
            LEFT JOIN public.master_chambers c
                ON c.chamber_id = e.chamber_id
            WHERE LOWER(TRIM(e.card_uid)) = LOWER(TRIM(@cardUid))
            LIMIT 1;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("cardUid", cardUid.Trim());
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new EmployeeRecord
        {
            EmployeeId = reader.GetInt64(0),
            EmployeeCode = reader.GetString(1),
            EmployeeName = reader.GetString(2),
            CardUid = reader.GetString(3),
            Department = reader.GetString(4),
            Designation = reader.GetString(5),
            Mobile = reader.GetString(6),
            ChamberId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            ChamberName = reader.GetString(8),
            IsActive = reader.GetBoolean(9)
        };
    }

    public async Task AddAsync(EmployeeRecord employee, long createdBy)
    {
        if (string.IsNullOrWhiteSpace(employee.CardUid))
        {
            throw new Exception("RFID card UID is required. Please scan a card first.");
        }

        var existing = await FindByCardUidAsync(employee.CardUid);
        if (existing != null)
        {
            throw new Exception(
                $"RFID card is already assigned to {existing.EmployeeName} ({existing.EmployeeCode}). Entry not allowed.");
        }

        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO public.master_employees
            (
                employee_code,
                employee_name,
                card_uid,
                department,
                designation,
                mobile,
                chamber_id,
                created_by,
                is_active
            )
            VALUES
            (
                @code,
                @name,
                @cardUid,
                @department,
                @designation,
                @mobile,
                @chamberId,
                @createdBy,
                TRUE
            );
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("code", employee.EmployeeCode.Trim());
        command.Parameters.AddWithValue("name", employee.EmployeeName.Trim());
        command.Parameters.AddWithValue("cardUid", employee.CardUid.Trim());
        command.Parameters.AddWithValue(
            "department",
            string.IsNullOrWhiteSpace(employee.Department)
                ? (object)DBNull.Value
                : employee.Department.Trim());
        command.Parameters.AddWithValue(
            "designation",
            string.IsNullOrWhiteSpace(employee.Designation)
                ? (object)DBNull.Value
                : employee.Designation.Trim());
        command.Parameters.AddWithValue(
            "mobile",
            string.IsNullOrWhiteSpace(employee.Mobile)
                ? (object)DBNull.Value
                : employee.Mobile.Trim());
        command.Parameters.AddWithValue(
            "chamberId",
            employee.ChamberId.HasValue
                ? Convert.ToInt32(employee.ChamberId.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("createdBy", createdBy);

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new Exception("Employee code or RFID card UID already exists.");
        }
    }

    public async Task SetActiveAsync(long employeeId, bool isActive, long updatedBy)
    {
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            UPDATE public.master_employees
            SET
                is_active = @isActive,
                updated_at = NOW()
            WHERE emp_id = @id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("isActive", isActive);
        command.Parameters.AddWithValue("id", employeeId);
        await command.ExecuteNonQueryAsync();
    }
}
