using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

public class RfidReaderService
{
    private readonly ConfigurationService _configurationService = new();

    public async Task<List<RfidReader>> GetAllAsync()
    {
        var readers = new List<RfidReader>();
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                reader_id,
                reader_name,
                reader_serialno,
                ip_address,
                port,
                reader_purpose,
                is_active
            FROM public.master_rfid_readers
            ORDER BY reader_id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            readers.Add(new RfidReader
            {
                ReaderId = reader.GetInt64(0),
                ReaderName = reader.GetString(1),
                ReaderSerialNo = reader.GetString(2),
                IpAddress = reader.GetString(3),
                Port = reader.GetInt32(4),
                ReaderPurpose = reader.GetString(5),
                IsActive = reader.GetBoolean(6)
            });
        }

        return readers;
    }

    public async Task AddAsync(RfidReader reader, long changedBy)
    {
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string insertSql = @"
            INSERT INTO public.master_rfid_readers
            (
                reader_name,
                reader_serialno,
                ip_address,
                port,
                reader_purpose,
                is_active,
                last_updated_by
            )
            VALUES
            (
                @name,
                @serial,
                @ip,
                @port,
                @purpose,
                TRUE,
                @changedBy
            )
            RETURNING reader_id;
        ";

        try
        {
            await using var insertCommand = new NpgsqlCommand(insertSql, connection, transaction);
            insertCommand.Parameters.AddWithValue("name", reader.ReaderName.Trim());
            insertCommand.Parameters.AddWithValue("serial", reader.ReaderSerialNo.Trim());
            insertCommand.Parameters.AddWithValue("ip", reader.IpAddress.Trim());
            insertCommand.Parameters.AddWithValue("port", reader.Port);
            insertCommand.Parameters.AddWithValue("purpose", reader.ReaderPurpose.Trim());
            insertCommand.Parameters.AddWithValue("changedBy", changedBy);

            long readerId = Convert.ToInt64(await insertCommand.ExecuteScalarAsync());

            const string logSql = @"
                INSERT INTO public.rfid_reader_configuration_log
                (
                    reader_id,
                    reader_name,
                    reader_serialno,
                    old_ip_address,
                    new_ip_address,
                    old_port,
                    new_port,
                    old_reader_purpose,
                    new_reader_purpose,
                    action_type,
                    changed_by
                )
                VALUES
                (
                    @readerId,
                    @name,
                    @serial,
                    NULL,
                    @ip,
                    NULL,
                    @port,
                    NULL,
                    @purpose,
                    'CREATED',
                    @changedBy
                );
            ";

            await using var logCommand = new NpgsqlCommand(logSql, connection, transaction);
            logCommand.Parameters.AddWithValue("readerId", readerId);
            logCommand.Parameters.AddWithValue("name", reader.ReaderName.Trim());
            logCommand.Parameters.AddWithValue("serial", reader.ReaderSerialNo.Trim());
            logCommand.Parameters.AddWithValue("ip", reader.IpAddress.Trim());
            logCommand.Parameters.AddWithValue("port", reader.Port);
            logCommand.Parameters.AddWithValue("purpose", reader.ReaderPurpose.Trim());
            logCommand.Parameters.AddWithValue("changedBy", changedBy);
            await logCommand.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await transaction.RollbackAsync();
            throw new Exception("IP address or port already exists.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task UpdateAsync(RfidReader reader, long changedBy)
    {
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string selectSql = @"
            SELECT
                reader_name,
                reader_serialno,
                ip_address,
                port,
                reader_purpose
            FROM public.master_rfid_readers
            WHERE reader_id = @id
            FOR UPDATE;
        ";

        try
        {
            await using var selectCommand = new NpgsqlCommand(selectSql, connection, transaction);
            selectCommand.Parameters.AddWithValue("id", reader.ReaderId);

            string oldIp;
            int oldPort;
            string oldPurpose;

            await using (var dbReader = await selectCommand.ExecuteReaderAsync())
            {
                if (!await dbReader.ReadAsync())
                {
                    throw new Exception("RFID reader not found.");
                }

                oldIp = dbReader.GetString(2);
                oldPort = dbReader.GetInt32(3);
                oldPurpose = dbReader.GetString(4);
            }

            const string updateSql = @"
                UPDATE public.master_rfid_readers
                SET
                    reader_name = @name,
                    reader_serialno = @serial,
                    ip_address = @ip,
                    port = @port,
                    reader_purpose = @purpose,
                    updated_at = NOW(),
                    last_updated_by = @changedBy
                WHERE reader_id = @id;
            ";

            await using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);
            updateCommand.Parameters.AddWithValue("name", reader.ReaderName.Trim());
            updateCommand.Parameters.AddWithValue("serial", reader.ReaderSerialNo.Trim());
            updateCommand.Parameters.AddWithValue("ip", reader.IpAddress.Trim());
            updateCommand.Parameters.AddWithValue("port", reader.Port);
            updateCommand.Parameters.AddWithValue("purpose", reader.ReaderPurpose.Trim());
            updateCommand.Parameters.AddWithValue("changedBy", changedBy);
            updateCommand.Parameters.AddWithValue("id", reader.ReaderId);
            await updateCommand.ExecuteNonQueryAsync();

            const string logSql = @"
                INSERT INTO public.rfid_reader_configuration_log
                (
                    reader_id,
                    reader_name,
                    reader_serialno,
                    old_ip_address,
                    new_ip_address,
                    old_port,
                    new_port,
                    old_reader_purpose,
                    new_reader_purpose,
                    action_type,
                    changed_by
                )
                VALUES
                (
                    @readerId,
                    @name,
                    @serial,
                    @oldIp,
                    @ip,
                    @oldPort,
                    @port,
                    @oldPurpose,
                    @purpose,
                    'UPDATED',
                    @changedBy
                );
            ";

            await using var logCommand = new NpgsqlCommand(logSql, connection, transaction);
            logCommand.Parameters.AddWithValue("readerId", reader.ReaderId);
            logCommand.Parameters.AddWithValue("name", reader.ReaderName.Trim());
            logCommand.Parameters.AddWithValue("serial", reader.ReaderSerialNo.Trim());
            logCommand.Parameters.AddWithValue("oldIp", oldIp);
            logCommand.Parameters.AddWithValue("ip", reader.IpAddress.Trim());
            logCommand.Parameters.AddWithValue("oldPort", oldPort);
            logCommand.Parameters.AddWithValue("port", reader.Port);
            logCommand.Parameters.AddWithValue("oldPurpose", oldPurpose);
            logCommand.Parameters.AddWithValue("purpose", reader.ReaderPurpose.Trim());
            logCommand.Parameters.AddWithValue("changedBy", changedBy);
            await logCommand.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await transaction.RollbackAsync();
            throw new Exception("IP address or port already exists.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task SetActiveAsync(long readerId, bool isActive, long changedBy)
    {
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string selectSql = @"
            SELECT
                reader_name,
                reader_serialno,
                ip_address,
                port,
                reader_purpose,
                is_active
            FROM public.master_rfid_readers
            WHERE reader_id = @id
            FOR UPDATE;
        ";

        await using var selectCommand = new NpgsqlCommand(selectSql, connection, transaction);
        selectCommand.Parameters.AddWithValue("id", readerId);

        await using var reader = await selectCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new Exception("RFID reader not found.");
        }

        string name = reader.GetString(0);
        string serial = reader.GetString(1);
        string ip = reader.GetString(2);
        int port = reader.GetInt32(3);
        string purpose = reader.GetString(4);
        bool currentActive = reader.GetBoolean(5);
        await reader.CloseAsync();

        if (currentActive == isActive)
        {
            await transaction.CommitAsync();
            return;
        }

        const string updateSql = @"
            UPDATE public.master_rfid_readers
            SET
                is_active = @isActive,
                updated_at = NOW(),
                last_updated_by = @changedBy
            WHERE reader_id = @id;
        ";

        await using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);
        updateCommand.Parameters.AddWithValue("isActive", isActive);
        updateCommand.Parameters.AddWithValue("changedBy", changedBy);
        updateCommand.Parameters.AddWithValue("id", readerId);
        await updateCommand.ExecuteNonQueryAsync();

        const string logSql = @"
            INSERT INTO public.rfid_reader_configuration_log
            (
                reader_id,
                reader_name,
                reader_serialno,
                old_ip_address,
                new_ip_address,
                old_port,
                new_port,
                old_reader_purpose,
                new_reader_purpose,
                action_type,
                changed_by
            )
            VALUES
            (
                @readerId,
                @name,
                @serial,
                @ip,
                @ip,
                @port,
                @port,
                @purpose,
                @purpose,
                @actionType,
                @changedBy
            );
        ";

        await using var logCommand = new NpgsqlCommand(logSql, connection, transaction);
        logCommand.Parameters.AddWithValue("readerId", readerId);
        logCommand.Parameters.AddWithValue("name", name);
        logCommand.Parameters.AddWithValue("serial", serial);
        logCommand.Parameters.AddWithValue("ip", ip);
        logCommand.Parameters.AddWithValue("port", port);
        logCommand.Parameters.AddWithValue("purpose", purpose);
        logCommand.Parameters.AddWithValue("actionType", isActive ? "ACTIVATED" : "DEACTIVATED");
        logCommand.Parameters.AddWithValue("changedBy", changedBy);
        await logCommand.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
    }
}
