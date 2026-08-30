namespace DashboardService.Services;

public class AlertSettings
{
    public int AfterMinutes { get; set; } = 60;

    public int AttentionMinutes { get; set; } = 30;

    public int WarningRemainingMinutes { get; set; } = 10;

    public int RepeatAfterViolationMinutes { get; set; } = 5;

    public bool VoiceEnabled { get; set; } = true;

    public string VoiceCulture { get; set; } = "en-IN";

    public string VoiceName { get; set; } = string.Empty;

    public int VoiceRate { get; set; } = -1;

    public string AttentionMessage { get; set; } =
        "Attention {EmployeeName}. You have completed {AttentionMinutes} minutes inside the {ChamberName}.";

    public string WarningMessage { get; set; } =
        "Warning {EmployeeName}. Only {WarningRemainingMinutes} minutes remain before your permitted duration expires.";

    public string ViolationMessage { get; set; } =
        "Alert {EmployeeName}. Your permitted duration inside the {ChamberName} has expired. Please exit {ChamberName} immediately.";

    public string ViolationRepeatMessage { get; set; } =
        "Warning {EmployeeName}. You have exceeded the permitted duration inside the {ChamberName}. Please exit immediately.";

    public int WarningAtMinutes => AfterMinutes - WarningRemainingMinutes;
}
