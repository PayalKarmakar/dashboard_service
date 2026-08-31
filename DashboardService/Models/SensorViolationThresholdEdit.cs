namespace DashboardService.Models;

public sealed class SensorViolationThresholdEdit
{
    public long SensorViolationsId { get; set; }

    public string Parameter { get; set; } = string.Empty;

    public string ThresholdType { get; set; } = string.Empty;

    public string ThresholdValue { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string ActualValue { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public bool IsGasParameter =>
        Parameter.Equals("CO", StringComparison.OrdinalIgnoreCase) ||
        Parameter.Equals("CO2", StringComparison.OrdinalIgnoreCase) ||
        Parameter.Equals("O2", StringComparison.OrdinalIgnoreCase);
}
