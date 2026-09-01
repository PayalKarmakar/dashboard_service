namespace DashboardService.Models;

public sealed class CameraDetectionStats
{
    public bool IsConnected { get; set; }

    public string StatusMessage { get; set; } = "Idle";

    public int TotalDetected { get; set; }

    public int InsideCount { get; set; }

    public int OutsideCount { get; set; }

    public double AverageConfidence { get; set; }

    public double Fps { get; set; }

    public int RfidInsideCount { get; set; }

    public string AccuracyDisplay => $"{AverageConfidence:F0}%";

    public string FpsDisplay => $"{Fps:F1} fps";
}
