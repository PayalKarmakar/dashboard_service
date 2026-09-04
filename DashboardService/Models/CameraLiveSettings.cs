namespace DashboardService.Models;

public sealed class CameraLiveSettings
{
    public double MinConfidence { get; set; } = 0.40;

    public int ZoneDividerPercent { get; set; } = 50;

    public int RfidRefreshIntervalSeconds { get; set; } = 2;

    public int DetectEveryNFrames { get; set; } = 2;

    public int InputSize { get; set; } = 320;

    public string ModelPath { get; set; } = "Models/Vision/yolov5n.onnx";

    public bool UsePythonService { get; set; } = true;

    /// <summary>
    /// When true, Entry/Exit/Unauthorized stat cards show for ENTRY/EXIT cameras.
    /// </summary>
    public bool ShowEntryExitStats { get; set; } = true;
}
