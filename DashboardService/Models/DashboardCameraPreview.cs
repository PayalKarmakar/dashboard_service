using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace DashboardService.Models;

public sealed class DashboardCameraPreview : INotifyPropertyChanged
{
    private BitmapSource? _frame;
    private bool _isConnected;

    public long CameraId { get; init; }

    public string CameraName { get; init; } = string.Empty;

    public string ChamberName { get; init; } = string.Empty;

    public string Purpose { get; init; } = string.Empty;

    public string Title =>
        string.IsNullOrWhiteSpace(PurposeDisplay)
            ? CameraName
            : $"{PurposeDisplay} · {CameraName}";

    public string PurposeDisplay => Purpose.Trim().ToUpperInvariant() switch
    {
        "ENTRY" => "ENTRY",
        "EXIT" => "EXIT",
        "MONITORING" => "MONITOR",
        "DOOR" => "ENTRY",
        _ => Purpose
    };

    public BitmapSource? Frame
    {
        get => _frame;
        set
        {
            _frame = value;
            OnPropertyChanged();
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected == value)
            {
                return;
            }

            _isConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusLabel));
        }
    }

    public string StatusLabel => IsConnected ? "LIVE" : "OFF";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
