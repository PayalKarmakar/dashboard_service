using DashboardService.Models;
using Npgsql;

namespace DashboardService.Services;

public class ChamberService
{
    public const string WarningAlertType = "WARNING";
    public const string ViolationAlertType = "VIOLATION";

    private readonly ConfigurationService _configurationService = new();
    private static bool _schemaEnsured;

    public async Task<List<Chamber>> GetAllAsync()
    {
        var chambers = new List<Chamber>();
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);

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

    public async Task<Chamber?> GetByIdAsync(long chamberId)
    {
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);

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
            WHERE chamber_id = @id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", chamberId);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new Chamber
        {
            ChamberId = reader.GetInt64(0),
            ChamberCode = reader.GetString(1),
            ChamberName = reader.GetString(2),
            ChamberLocation = reader.GetString(3),
            MemberThreshold = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            TimeThreshold = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            IsActive = reader.GetBoolean(6)
        };
    }

    public async Task<long> AddAsync(Chamber chamber, long createdBy)
    {
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);

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
            )
            RETURNING chamber_id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        BindChamberParameters(command, chamber, createdBy);

        try
        {
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new Exception("Chamber code already exists.");
        }
    }

    public async Task UpdateAsync(Chamber chamber, long updatedBy)
    {
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);

        const string sql = @"
            UPDATE public.master_chambers
            SET
                chamber_code = @code,
                chamber_name = @name,
                chamber_location = @location,
                member_threshold = @memberThreshold,
                time_threshold = @timeThreshold,
                updated_at = NOW()
            WHERE chamber_id = @id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", chamber.ChamberId);
        BindChamberParameters(command, chamber, updatedBy);

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

    public async Task<List<ChamberAlertRule>> GetRulesAsync(long chamberId)
    {
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        return await GetRulesAsync(connection, [chamberId]);
    }

    public async Task<Dictionary<long, List<ChamberAlertRule>>> GetRulesByChamberIdsAsync(IReadOnlyCollection<long> chamberIds)
    {
        var result = new Dictionary<long, List<ChamberAlertRule>>();
        if (chamberIds.Count == 0)
        {
            return result;
        }

        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);

        foreach (var rule in await GetRulesAsync(connection, chamberIds))
        {
            if (!result.TryGetValue(rule.ChamberId, out var list))
            {
                list = [];
                result[rule.ChamberId] = list;
            }

            list.Add(rule);
        }

        return result;
    }

    public async Task SaveRulesAsync(long chamberId, IEnumerable<ChamberAlertRule> rules)
    {
        await using var connection = new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);

        const string sql = @"
            INSERT INTO public.chamber_alert_rules
            (
                chamber_id,
                alert_time_minutes,
                alert_type,
                announcement_message,
                is_announcement_enabled,
                is_active,
                max_play_count
            )
            VALUES
            (
                @chamberId,
                @alertTimeMinutes,
                @alertType,
                @message,
                @audioEnabled,
                @isActive,
                @maxPlayCount
            )
            ON CONFLICT (chamber_id, alert_type)
            DO UPDATE SET
                alert_time_minutes = EXCLUDED.alert_time_minutes,
                announcement_message = EXCLUDED.announcement_message,
                is_announcement_enabled = EXCLUDED.is_announcement_enabled,
                is_active = EXCLUDED.is_active,
                max_play_count = EXCLUDED.max_play_count,
                updated_at = NOW();
        ";

        foreach (var rule in rules)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("chamberId", Convert.ToInt32(chamberId));
            command.Parameters.AddWithValue("alertTimeMinutes", Math.Max(0, rule.AlertTimeMinutes));
            command.Parameters.AddWithValue("alertType", rule.AlertType.Trim().ToUpperInvariant());
            command.Parameters.AddWithValue(
                "message",
                string.IsNullOrWhiteSpace(rule.AnnouncementMessage)
                    ? DBNull.Value
                    : rule.AnnouncementMessage.Trim());
            command.Parameters.AddWithValue("audioEnabled", rule.IsAnnouncementEnabled);
            command.Parameters.AddWithValue("isActive", rule.IsActive);
            bool unlimited = rule.AlertType.Equals(ViolationAlertType, StringComparison.OrdinalIgnoreCase) &&
                             ChamberAlertRule.IsContinue(rule.MaxPlayCount);
            command.Parameters.AddWithValue(
                "maxPlayCount",
                unlimited ? ChamberAlertRule.ContinuePlayCount : Math.Max(1, rule.MaxPlayCount));
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<List<ChamberAlertRule>> GetRulesAsync(
        NpgsqlConnection connection,
        IReadOnlyCollection<long> chamberIds)
    {
        var rules = new List<ChamberAlertRule>();
        int[] ids = chamberIds.Select(id => Convert.ToInt32(id)).ToArray();

        const string sql = @"
            SELECT
                rule_id,
                chamber_id,
                alert_time_minutes,
                alert_type,
                COALESCE(announcement_message, ''),
                is_announcement_enabled,
                is_active,
                COALESCE(max_play_count, 0)
            FROM public.chamber_alert_rules
            WHERE chamber_id = ANY(@ids);
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("ids", ids);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rules.Add(new ChamberAlertRule
            {
                RuleId = reader.GetInt64(0),
                ChamberId = reader.GetInt32(1),
                AlertTimeMinutes = reader.GetInt32(2),
                AlertType = reader.GetString(3),
                AnnouncementMessage = reader.GetString(4),
                IsAnnouncementEnabled = reader.GetBoolean(5),
                IsActive = reader.GetBoolean(6),
                MaxPlayCount = reader.GetInt32(7)
            });
        }

        return rules;
    }

    private static void BindChamberParameters(NpgsqlCommand command, Chamber chamber, long userId)
    {
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
        command.Parameters.AddWithValue("createdBy", userId);
    }

    private static async Task EnsureSchemaAsync(NpgsqlConnection connection)
    {
        if (_schemaEnsured)
        {
            return;
        }

        const string sql = @"
            ALTER TABLE public.chamber_alert_rules
              ADD COLUMN IF NOT EXISTS max_play_count integer NOT NULL DEFAULT 1;

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'uq_chamber_alert_rules_chamber_type'
                ) THEN
                    CREATE UNIQUE INDEX IF NOT EXISTS uq_chamber_alert_rules_chamber_type
                      ON public.chamber_alert_rules (chamber_id, alert_type);
                    ALTER TABLE public.chamber_alert_rules
                      ADD CONSTRAINT uq_chamber_alert_rules_chamber_type
                      UNIQUE USING INDEX uq_chamber_alert_rules_chamber_type;
                END IF;
            END $$;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
        _schemaEnsured = true;
    }
}
