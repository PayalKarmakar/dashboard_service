using Npgsql;
using DashboardService.Models;

namespace DashboardService.Services;

public sealed class AlertMessageService
{
    public const string CategorySensor = "SENSOR";
    public const string CategoryEmployee = "EMPLOYEE";

    public const string CultureEnglishIndia = "en-IN";
    public const string CultureBengaliIndia = "bn-IN";

    private readonly ConfigurationService _configurationService = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _cacheLoadedAt = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<string?> GetTemplateAsync(
        string category,
        string alertType,
        string culture)
    {
        await EnsureLoadedAsync();

        string key = BuildKey(category, alertType, culture);
        if (_cache.TryGetValue(key, out string? template) &&
            !string.IsNullOrWhiteSpace(template))
        {
            return template;
        }

        return GetAppsettingsFallback(category, alertType, culture);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetTemplatesAsync(string category,string alertType)
    {
        await EnsureLoadedAsync();

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string culture in new[] { CultureEnglishIndia, CultureBengaliIndia })
        {
            string key = BuildKey(category, alertType, culture);
            if (_cache.TryGetValue(key, out string? template) &&
                !string.IsNullOrWhiteSpace(template))
            {
                result[culture] = template;
                continue;
            }

            string? fallback = GetAppsettingsFallback(category, alertType, culture);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                result[culture] = fallback;
            }
        }

        return result;
    }

    public void InvalidateCache()
    {
        _cacheLoadedAt = DateTime.MinValue;
    }

    public async Task<List<VoiceAlertMessage>> GetAllMessagesAsync(string? category = null)
    {
        await EnsureLoadedAsync();

        var messages = new List<VoiceAlertMessage>();

        try
        {
            await using var connection =
                new NpgsqlConnection(_configurationService.GetConnectionString());
            await connection.OpenAsync();
            await EnsureSchemaAsync(connection);

            string sql = @"
                SELECT message_id, category, alert_type, culture, message_template, is_active
                FROM public.voice_alert_messages
                WHERE is_active = TRUE";

            if (!string.IsNullOrWhiteSpace(category))
            {
                sql += " AND category = @category";
            }

            sql += " ORDER BY category, alert_type, culture;";

            await using var command = new NpgsqlCommand(sql, connection);
            if (!string.IsNullOrWhiteSpace(category))
            {
                command.Parameters.AddWithValue("category", category);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                messages.Add(new VoiceAlertMessage
                {
                    MessageId = reader.GetInt64(0),
                    Category = reader.GetString(1),
                    AlertType = reader.GetString(2),
                    Culture = reader.GetString(3),
                    MessageTemplate = reader.GetString(4),
                    IsActive = reader.GetBoolean(5)
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Failed to list voice alert messages: {ex.Message}");
        }

        return messages;
    }

    public async Task UpdateMessageTemplateAsync(long messageId, string template)
    {
        await using var connection =
            new NpgsqlConnection(_configurationService.GetConnectionString());
        await connection.OpenAsync();

        const string sql = @"
            UPDATE public.voice_alert_messages
            SET message_template = @template,
                updated_at = NOW()
            WHERE message_id = @message_id;
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("template", template.Trim());
        command.Parameters.AddWithValue("message_id", messageId);
        await command.ExecuteNonQueryAsync();
        InvalidateCache();
    }

    private async Task EnsureLoadedAsync()
    {
        if (DateTime.UtcNow - _cacheLoadedAt < CacheDuration && _cache.Count > 0)
        {
            return;
        }

        await _loadLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _cacheLoadedAt < CacheDuration && _cache.Count > 0)
            {
                return;
            }

            var loaded = await LoadFromDatabaseAsync();
            _cache = loaded;
            _cacheLoadedAt = DateTime.UtcNow;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<Dictionary<string, string>> LoadFromDatabaseAsync()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await using var connection =
                new NpgsqlConnection(_configurationService.GetConnectionString());
            await connection.OpenAsync();

            await EnsureSchemaAsync(connection);

            const string sql = @"
                SELECT category, alert_type, culture, message_template
                FROM public.voice_alert_messages
                WHERE is_active = TRUE;
            ";

            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string category = reader.GetString(0);
                string alertType = reader.GetString(1);
                string culture = reader.GetString(2);
                string template = reader.GetString(3);

                if (string.IsNullOrWhiteSpace(template))
                {
                    continue;
                }

                result[BuildKey(category, alertType, culture)] = template.Trim();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Failed to load voice alert messages from database: {ex.Message}");
        }

        return result;
    }

    private static async Task EnsureSchemaAsync(NpgsqlConnection connection)
    {
        const string createTableSql = @"
            CREATE TABLE IF NOT EXISTS public.voice_alert_messages (
                message_id       BIGSERIAL PRIMARY KEY,
                category         VARCHAR(30)  NOT NULL,
                alert_type       VARCHAR(30)  NOT NULL,
                culture          VARCHAR(10)  NOT NULL,
                message_template TEXT         NOT NULL,
                is_active        BOOLEAN      NOT NULL DEFAULT TRUE,
                updated_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_voice_alert_messages UNIQUE (category, alert_type, culture)
            );

            CREATE INDEX IF NOT EXISTS ix_voice_alert_messages_lookup
                ON public.voice_alert_messages (category, alert_type, culture)
                WHERE is_active = TRUE;
        ";

        await using (var createCommand = new NpgsqlCommand(createTableSql, connection))
        {
            await createCommand.ExecuteNonQueryAsync();
        }

        const string countSql = "SELECT COUNT(*) FROM public.voice_alert_messages;";
        await using var countCommand = new NpgsqlCommand(countSql, connection);
        long count = Convert.ToInt64(await countCommand.ExecuteScalarAsync() ?? 0L);

        if (count > 0)
        {
            return;
        }

        const string seedSql = @"
            INSERT INTO public.voice_alert_messages (category, alert_type, culture, message_template)
            VALUES
                ('SENSOR', 'WARNING', 'en-IN',
                 'Warning. {Parameter} level in {ChamberName} has exceeded the permitted limit.'),
                ('SENSOR', 'WARNING', 'bn-IN',
                 'সতর্কতা। {ChamberName}-এ {Parameter}-এর মাত্রা অনুমোদিত সীমা অতিক্রম করেছে।'),
                ('SENSOR', 'CRITICAL', 'en-IN',
                 'Critical alert. {Parameter} level in {ChamberName} has reached a critical level. Please take immediate action.'),
                ('SENSOR', 'CRITICAL', 'bn-IN',
                 'গুরুতর সতর্কতা। {ChamberName}-এ {Parameter}-এর মাত্রা গুরুতর পর্যায়ে পৌঁছেছে। অবিলম্বে ব্যবস্থা নিন।'),
                ('EMPLOYEE', 'ATTENTION', 'en-IN',
                 'Attention {EmployeeName} ji. You have completed {AttentionMinutes} minutes inside {ChamberName}.'),
                ('EMPLOYEE', 'ATTENTION', 'bn-IN',
                 'মনোযোগ {EmployeeName}। আপনি {ChamberName}-এ {AttentionMinutes} মিনিট সময় কাটিয়েছেন।'),
                ('EMPLOYEE', 'WARNING', 'en-IN',
                 'Warning {EmployeeName} ji. Only {WarningRemainingMinutes} minutes remain before your permitted duration expires in {ChamberName}.'),
                ('EMPLOYEE', 'WARNING', 'bn-IN',
                 'সতর্কতা {EmployeeName}। {ChamberName}-এ অনুমোদিত সময় শেষ হতে {WarningRemainingMinutes} মিনিট বাকি।'),
                ('EMPLOYEE', 'VIOLATION', 'en-IN',
                 'Alert {EmployeeName} ji. Your permitted duration inside {ChamberName} has expired. Please exit immediately.'),
                ('EMPLOYEE', 'VIOLATION', 'bn-IN',
                 'সতর্কতা {EmployeeName}। {ChamberName}-এ আপনার অনুমোদিত সময় শেষ। দয়া করে অবিলম্বে বেরিয়ে আসুন।'),
                ('EMPLOYEE', 'VIOLATION_REPEAT', 'en-IN',
                 'Warning {EmployeeName} ji. You have exceeded the permitted duration inside {ChamberName}. Please exit immediately.'),
                ('EMPLOYEE', 'VIOLATION_REPEAT', 'bn-IN',
                 'সতর্কতা {EmployeeName}। {ChamberName}-এ অনুমোদিত সময় অতিক্রম করেছেন। দয়া করে অবিলম্বে বেরিয়ে আসুন।');
        ";

        await using var seedCommand = new NpgsqlCommand(seedSql, connection);
        await seedCommand.ExecuteNonQueryAsync();
    }

    private static string BuildKey(string category, string alertType, string culture) =>
        $"{category}:{alertType}:{culture}";

    private string? GetAppsettingsFallback(string category, string alertType, string culture)
    {
        if (category.Equals(CategorySensor, StringComparison.OrdinalIgnoreCase))
        {
            if (alertType.Equals("WARNING", StringComparison.OrdinalIgnoreCase))
            {
                return _configurationService.ReadRawMessage(
                    $"SensorAlertSettings:Messages:WARNING:{culture}");
            }

            if (alertType.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase))
            {
                return _configurationService.ReadRawMessage(
                    $"SensorAlertSettings:Messages:CRITICAL:{culture}");
            }
        }

        if (category.Equals(CategoryEmployee, StringComparison.OrdinalIgnoreCase))
        {
            string appsettingsKey = alertType.ToUpperInvariant() switch
            {
                "ATTENTION" => "Attention",
                "WARNING" => "Warning",
                "VIOLATION" => "Violation",
                "VIOLATION_REPEAT" => "ViolationRepeat",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(appsettingsKey))
            {
                return null;
            }

            if (culture.Equals(CultureEnglishIndia, StringComparison.OrdinalIgnoreCase))
            {
                return _configurationService.ReadRawMessage(
                    $"AlertSettings:Messages:{appsettingsKey}");
            }
        }

        return null;
    }
}
