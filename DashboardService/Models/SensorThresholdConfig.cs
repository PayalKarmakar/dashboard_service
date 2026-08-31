namespace DashboardService.Models;

public sealed class SensorThresholdConfig
{
    public long ThresholdsId { get; set; }

    public string SensorType { get; set; } = string.Empty;

    public string Parameter { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string? WarningLow { get; set; }

    public string? WarningHigh { get; set; }

    public string? CriticalLow { get; set; }

    public string? CriticalHigh { get; set; }

    public bool Enabled { get; set; }

    public bool IsGasParameter =>
        Parameter.Equals("CO", StringComparison.OrdinalIgnoreCase) ||
        Parameter.Equals("CO2", StringComparison.OrdinalIgnoreCase) ||
        Parameter.Equals("O2", StringComparison.OrdinalIgnoreCase);
}
