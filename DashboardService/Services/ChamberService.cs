using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

public class ChamberService
{
    private readonly ConfigurationService _configurationService = new();

    public async Task<List<Chamber>> GetAllAsync()
    {
        var chambers = new List<Chamber>();
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                chamber_id,
                chamber_code,
                chamber_name,
                COALESCE(chamber_location, ''),
                member_threshold,
                time_threshold,
                is_active
            FROM public.master_chambers
            ORDER BY chamber_id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            chambers.Add(new Chamber
            {
                ChamberId = reader.GetInt64(0),
                ChamberCode = reader.GetString(1),
                ChamberName = reader.GetString(2),
                ChamberLocation = reader.GetString(3),
                MemberThreshold = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                TimeThreshold = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                IsActive = reader.GetBoolean(6)
            });
        }

        return chambers;
    }

    public async Task AddAsync(Chamber chamber, long createdBy)
    {
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO public.master_chambers
            (
                chamber_code,
                chamber_name,
                chamber_location,
                member_threshold,
                time_threshold,
                created_by,
                is_active
            )
            VALUES
            (
                @code,
                @name,
                @location,
                @memberThreshold,
                @timeThreshold,
                @createdBy,
                TRUE
            );
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("code", chamber.ChamberCode.Trim());
        command.Parameters.AddWithValue("name", chamber.ChamberName.Trim());
        command.Parameters.AddWithValue(
            "location",
            string.IsNullOrWhiteSpace(chamber.ChamberLocation)
                ? (object)DBNull.Value
                : chamber.ChamberLocation.Trim());
        command.Parameters.AddWithValue(
            "memberThreshold",
            (object?)chamber.MemberThreshold ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "timeThreshold",
            (object?)chamber.TimeThreshold ?? DBNull.Value);
        command.Parameters.AddWithValue("createdBy", createdBy);

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new Exception("Chamber code already exists.");
        }
    }

    public async Task SetActiveAsync(long chamberId, bool isActive, long updatedBy)
    {
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            UPDATE public.master_chambers
            SET
                is_active = @isActive,
                updated_at = NOW()
            WHERE chamber_id = @id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("isActive", isActive);
        command.Parameters.AddWithValue("id", chamberId);
        await command.ExecuteNonQueryAsync();
    }
}
