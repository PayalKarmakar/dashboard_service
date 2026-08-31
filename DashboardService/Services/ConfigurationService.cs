using DashboardService.Models;
using Microsoft.Extensions.Configuration;

namespace DashboardService.Services;

public class ConfigurationService
{
    private readonly IConfiguration _configuration;

    public ConfigurationService()
    {
        _configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
    }

    public string GetConnectionString()
    {
        return _configuration.GetConnectionString("PostgreSqlConnection")
            ?? throw new Exception("PostgreSQL connection string is missing.");
    }

    public string GetRfidServiceBaseUrl()
    {
        string? value = _configuration["RfidService:BaseUrl"];
        return string.IsNullOrWhiteSpace(value)
            ? "http://localhost:64312"
            : value.Trim().TrimEnd('/');
    }

    public string GetSensorServiceBaseUrl()
    {
        string? value = _configuration["SensorService:BaseUrl"];
        return string.IsNullOrWhiteSpace(value)
            ? "http://localhost:64314"
            : value.Trim().TrimEnd('/');
    }

    public AlertSettings GetAlertSettings()
    {
        return new AlertSettings
        {
            AfterMinutes = ReadPositiveInt("AlertSettings:AfterMinutes", 60),
            AttentionMinutes = ReadPositiveInt("AlertSettings:AttentionMinutes", 30),
            WarningRemainingMinutes = ReadPositiveInt("AlertSettings:WarningRemainingMinutes", 10),
            RepeatAfterViolationMinutes = ReadPositiveInt("AlertSettings:RepeatAfterViolationMinutes", 5),
            VoiceEnabled = ReadBool("AlertSettings:VoiceEnabled", true),
            VoiceCulture = ReadMessage("AlertSettings:VoiceCulture", "en-IN"),
            VoiceName = ReadMessage("AlertSettings:VoiceName", string.Empty),
            VoiceRate = ReadInt("AlertSettings:VoiceRate", -1),
            AttentionMessage = ReadMessage(
                "AlertSettings:Messages:Attention",
                "Attention {EmployeeName}. You have completed {AttentionMinutes} minutes inside the {ChamberName}."),
            WarningMessage = ReadMessage(
                "AlertSettings:Messages:Warning",
                "Warning {EmployeeName}. Only {WarningRemainingMinutes} minutes remain before your permitted duration expires."),
            ViolationMessage = ReadMessage(
                "AlertSettings:Messages:Violation",
                "Alert {EmployeeName}. Your permitted duration inside the {ChamberName} has expired. Please exit {ChamberName} immediately."),
            ViolationRepeatMessage = ReadMessage(
                "AlertSettings:Messages:ViolationRepeat",
                "Warning {EmployeeName}. You have exceeded the permitted duration inside the {ChamberName}. Please exit immediately.")
        };
    }

    public int GetAlertAfterMinutes()
    {
        return GetAlertSettings().AfterMinutes;
    }

    // Payal
    public SensorAlertSettings GetSensorAlertSettings()
    {
        return new SensorAlertSettings
        {
            VoiceEnabled = ReadBool("SensorAlertSettings:VoiceEnabled", true),
            RepeatAfterMinutes = ReadPositiveInt("SensorAlertSettings:RepeatAfterMinutes", 5),
            SensorViolationDbCheckIntervalSeconds = ReadPositiveInt("SensorAlertSettings:SensorViolationDbCheckIntervalSeconds", 2),
            EnglishVoiceCulture = ReadMessage("SensorAlertSettings:EnglishVoiceCulture", "en-IN"),
            BengaliVoiceCulture = ReadMessage("SensorAlertSettings:BengaliVoiceCulture", "bn-IN")
        };
    }

    private int ReadPositiveInt(string key, int fallback)
    {
        if (!int.TryParse(_configuration[key], out int value) || value <= 0)
        {
            return fallback;
        }

        return value;
    }

    private int ReadInt(string key, int fallback)
    {
        if (!int.TryParse(_configuration[key], out int value))
        {
            return fallback;
        }

        return value;
    }

    private bool ReadBool(string key, bool fallback)
    {
        string? value = _configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (bool.TryParse(value, out bool parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private string ReadMessage(string key, string fallback)
    {
        string? value = _configuration[key];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    public string? ReadRawMessage(string key)
    {
        string? value = _configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
