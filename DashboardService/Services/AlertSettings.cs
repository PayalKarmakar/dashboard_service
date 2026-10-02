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
        "Attention {EmployeeName} ji. You have completed {AttentionMinutes} minutes inside {ChamberName}.";

    public string WarningMessage { get; set; } =
        "Warning {EmployeeName} ji. Only {WarningRemainingMinutes} minutes remain before your permitted duration expires in {ChamberName}.";

    public string ViolationMessage { get; set; } =
        "Alert {EmployeeName} ji. Your permitted duration inside {ChamberName} has expired. Kindly exit immediately.";

    public string ViolationRepeatMessage { get; set; } =
        "Warning {EmployeeName} ji. You have exceeded the permitted duration inside {ChamberName}. Kindly exit immediately.";

    public int WarningAtMinutes => AfterMinutes - WarningRemainingMinutes;
}
