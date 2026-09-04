using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

public sealed class RfidTransactionService
{
    private readonly ConfigurationService _configurationService = new();

    public async Task<List<RfidTransactionRow>> GetOpenAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync(openOnly: true, cancellationToken);
    }

    public async Task<List<RfidTransactionRow>> GetRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return await QueryAsync(openOnly: false, cancellationToken, limit);
    }

    public async Task OpenManualAsync(
        EmployeeRecord employee,
        Chamber chamber,
        long correctedByUserId,
        string? remarks = null,
        DateTime? entryTime = null,
        CancellationToken cancellationToken = default)
    {
        if (employee.EmployeeId <= 0)
        {
            throw new InvalidOperationException("Select an employee.");
        }

        if (chamber.ChamberId <= 0)
        {
            throw new InvalidOperationException("Select a chamber.");
        }

        if (correctedByUserId <= 0)
        {
            throw new InvalidOperationException("Invalid user for manual correction.");
        }

        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        const string existsSql = @"
            SELECT EXISTS(
                SELECT 1
                FROM public.rfid_transactions t
                WHERE t.employee_id = @employeeId
                  AND t.status = 'OPEN'
                  AND t.exit_time IS NULL
            );
        ";

        await using (var existsCommand = new NpgsqlCommand(existsSql, connection))
        {
            existsCommand.Parameters.AddWithValue("employeeId", employee.EmployeeId);
            bool alreadyOpen = Convert.ToBoolean(await existsCommand.ExecuteScalarAsync(cancellationToken));
            if (alreadyOpen)
            {
                throw new InvalidOperationException(
                    $"{employee.EmployeeName} already has an OPEN RFID transaction. Close it first.");
            }
        }

        const string insertSql = @"
            INSERT INTO public.rfid_transactions (
                employee_id,
                chamber_id,
                employee_name,
                card_uid,
                entry_time,
                entry_reader_ip,
                entry_reader_port,
                status,
                alert_triggered,
                remarks,
                is_manually_corrected,
                corrected_by,
                corrected_at
            ) VALUES (
                @employeeId,
                @chamberId,
                @employeeName,
                @cardUid,
                @entryTime,
                'MANUAL',
                0,
                'OPEN',
                FALSE,
                @remarks,
                TRUE,
                @correctedBy,
                NOW()
            );
        ";

        await using var insertCommand = new NpgsqlCommand(insertSql, connection);
        insertCommand.Parameters.AddWithValue("employeeId", employee.EmployeeId);
        insertCommand.Parameters.AddWithValue("chamberId", chamber.ChamberId);
        insertCommand.Parameters.AddWithValue("employeeName", employee.EmployeeName ?? string.Empty);
        insertCommand.Parameters.AddWithValue("cardUid", employee.CardUid ?? string.Empty);
        insertCommand.Parameters.AddWithValue("entryTime", entryTime ?? DateTime.Now);
        insertCommand.Parameters.AddWithValue(
            "remarks",
            string.IsNullOrWhiteSpace(remarks) ? "Manual open" : remarks.Trim());
        insertCommand.Parameters.AddWithValue("correctedBy", correctedByUserId);
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CloseManualAsync(
        long transactionId,
        long correctedByUserId,
        string? remarks = null,
        DateTime? exitTime = null,
        CancellationToken cancellationToken = default)
    {
        if (transactionId <= 0)
        {
            throw new InvalidOperationException("Invalid transaction.");
        }

        if (correctedByUserId <= 0)
        {
            throw new InvalidOperationException("Invalid user for manual correction.");
        }

        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        // DB check chk_rfid_transaction_status allows: OPEN, COMPLETED, INCOMPLETE (not CLOSED).
        const string sql = @"
            UPDATE public.rfid_transactions
            SET exit_time = @exitTime,
                status = 'COMPLETED',
                updated_at = NOW(),
                remarks = @remarks,
                is_manually_corrected = TRUE,
                corrected_by = @correctedBy,
                corrected_at = NOW()
            WHERE id = @id
              AND status = 'OPEN'
              AND exit_time IS NULL;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", transactionId);
        command.Parameters.AddWithValue("exitTime", exitTime ?? DateTime.Now);
        command.Parameters.AddWithValue(
            "remarks",
            string.IsNullOrWhiteSpace(remarks) ? "Manual close" : remarks.Trim());
        command.Parameters.AddWithValue("correctedBy", correctedByUserId);

        int updated = await command.ExecuteNonQueryAsync(cancellationToken);
        if (updated == 0)
        {
            throw new InvalidOperationException("Transaction is not OPEN or already closed.");
        }
    }

    private async Task<List<RfidTransactionRow>> QueryAsync(
        bool openOnly,
        CancellationToken cancellationToken,
        int limit = 200)
    {
        var rows = new List<RfidTransactionRow>();

        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        string sql = openOnly
            ? @"
                SELECT
                    t.id,
                    t.employee_id,
                    COALESCE(e.employee_code, ''),
                    COALESCE(t.employee_name, ''),
                    COALESCE(t.card_uid, ''),
                    t.chamber_id,
                    COALESCE(c.chamber_name, ''),
                    t.entry_time,
                    t.exit_time,
                    COALESCE(t.status, ''),
                    COALESCE(t.remarks, ''),
                    COALESCE(t.is_manually_corrected, FALSE),
                    t.corrected_by,
                    t.corrected_at
                FROM public.rfid_transactions t
                LEFT JOIN public.master_employees e ON e.emp_id = t.employee_id
                LEFT JOIN public.master_chambers c ON c.chamber_id = t.chamber_id
                WHERE t.status = 'OPEN'
                  AND t.exit_time IS NULL
                ORDER BY t.entry_time DESC;
              "
            : @"
                SELECT
                    t.id,
                    t.employee_id,
                    COALESCE(e.employee_code, ''),
                    COALESCE(t.employee_name, ''),
                    COALESCE(t.card_uid, ''),
                    t.chamber_id,
                    COALESCE(c.chamber_name, ''),
                    t.entry_time,
                    t.exit_time,
                    COALESCE(t.status, ''),
                    COALESCE(t.remarks, ''),
                    COALESCE(t.is_manually_corrected, FALSE),
                    t.corrected_by,
                    t.corrected_at
                FROM public.rfid_transactions t
                LEFT JOIN public.master_employees e ON e.emp_id = t.employee_id
                LEFT JOIN public.master_chambers c ON c.chamber_id = t.chamber_id
                ORDER BY COALESCE(t.exit_time, t.entry_time) DESC, t.id DESC
                LIMIT @limit;
              ";

        await using var command = new NpgsqlCommand(sql, connection);
        if (!openOnly)
        {
            command.Parameters.AddWithValue("limit", Math.Clamp(limit, 20, 500));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RfidTransactionRow
            {
                TransactionId = reader.GetInt64(0),
                EmployeeId = reader.GetInt64(1),
                EmployeeCode = reader.GetString(2),
                EmployeeName = reader.GetString(3),
                CardUid = reader.GetString(4),
                ChamberId = reader.GetInt64(5),
                ChamberName = reader.GetString(6),
                EntryTime = reader.GetDateTime(7),
                ExitTime = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                Status = reader.GetString(9),
                Remarks = reader.GetString(10),
                IsManuallyCorrected = reader.GetBoolean(11),
                CorrectedBy = reader.IsDBNull(12) ? null : reader.GetInt64(12),
                CorrectedAt = reader.IsDBNull(13) ? null : reader.GetDateTime(13)
            });
        }

        return rows;
    }
}
