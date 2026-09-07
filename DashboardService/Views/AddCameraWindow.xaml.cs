using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DashboardService.Models;
using DashboardService.Services;
using OpenCvSharp;
using Window = System.Windows.Window;

namespace DashboardService.Views;

public partial class AddCameraWindow : Window
{
    private readonly CameraConfigurationService _cameraService = new();
    private readonly ChamberService _chamberService = new();
    private readonly RfidReaderService _readerService = new();
    private readonly long _changedBy;
    private readonly MasterCameraConfig? _editingCamera;
    private readonly bool _isEditMode;

    public AddCameraWindow(long changedBy, MasterCameraConfig? existingCamera = null)
    {
        InitializeComponent();
        _changedBy = changedBy;
        _editingCamera = existingCamera;
        _isEditMode = existingCamera != null;

        Loaded += AddCameraWindow_Loaded;
    }

    private async void AddCameraWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var chambers = await _chamberService.GetAllAsync();
            ChamberComboBox.ItemsSource = chambers
                .Where(c => c.IsActive)
                .Select(c => new ChamberOption
                {
                    ChamberId = c.ChamberId,
                    Display = c.ToString()
                })
                .ToList();

            var readers = await _readerService.GetAllAsync();
            var readerItems = new List<RfidReaderOption>
            {
                new() { ReaderId = 0, DisplayLabel = "None" }
            };
            readerItems.AddRange(
                readers
                    .Where(r => r.IsActive)
                    .Select(r => new RfidReaderOption
                    {
                        ReaderId = r.ReaderId,
                        DisplayLabel = r.DisplayLabel
                    }));

            ReaderComboBox.ItemsSource = readerItems;

            if (_isEditMode && _editingCamera != null)
            {
                Title = "Edit Camera";
                TitleText.Text = "Edit Camera";
                SubtitleText.Text = "Update camera purpose and RFID verification settings";
                SaveButton.Content = "Update";

                NameTextBox.Text = _editingCamera.CameraName;
                ChamberComboBox.SelectedValue = _editingCamera.ChamberId;
                IpTextBox.Text = _editingCamera.IpAddress ?? string.Empty;
                RtspTextBox.Text = _editingCamera.RtspUrl;
                MatchWindowTextBox.Text = _editingCamera.MatchWindowSeconds.ToString();
                PersonDetectionCheckBox.IsChecked = _editingCamera.PersonDetectionEnabled;
                AlertNoRfidCheckBox.IsChecked = _editingCamera.AlertOnNoRfid;
                AlertTailgateCheckBox.IsChecked = _editingCamera.AlertOnTailgate;
                ReaderComboBox.SelectedValue = _editingCamera.RfidReaderId ?? 0L;

                string editPurpose = _editingCamera.CameraPurpose?.Trim() ?? "ENTRY";
                // Legacy "DOOR" cameras map to Entry in the dropdown.
                if (editPurpose.Equals("DOOR", StringComparison.OrdinalIgnoreCase))
                {
                    editPurpose = "ENTRY";
                }

                foreach (ComboBoxItem item in PurposeComboBox.Items)
                {
                    if (item.Tag is string tag &&
                        tag.Equals(editPurpose, StringComparison.OrdinalIgnoreCase))
                    {
                        PurposeComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            else if (ChamberComboBox.Items.Count > 0)
            {
                ChamberComboBox.SelectedIndex = 0;
                ReaderComboBox.SelectedIndex = 0;
            }

            NameTextBox.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Camera Configuration", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        string rtsp = RtspTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rtsp))
        {
            MessageBox.Show(
                "Enter an RTSP URL first.",
                "Camera Test",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        TestButton.IsEnabled = false;
        TestStatusText.Foreground = (Brush)FindResource("TextMutedBrush");
        TestStatusText.Text = "Testing camera connection...";

        try
        {
            var testTask = Task.Run(() => TryReachRtsp(rtsp));
            var finished = await Task.WhenAny(testTask, Task.Delay(TimeSpan.FromSeconds(12)));
            if (finished != testTask)
            {
                throw new OperationCanceledException();
            }

            var result = await testTask;

            if (result.Ok)
            {
                TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                TestStatusText.Text = result.Detail;
                MessageBox.Show(
                    result.Detail,
                    "Camera Test",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                TestStatusText.Text = result.Detail;
                MessageBox.Show(
                    result.Detail,
                    "Camera Test",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            TestStatusText.Text = "Test timed out. Check RTSP URL and network.";
            MessageBox.Show(
                "Test timed out. Check RTSP URL and network.",
                "Camera Test",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            TestStatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Camera Test", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private static (bool Ok, string Detail) TryReachRtsp(string streamUrl)
    {
        VideoCapture? capture = null;
        try
        {
            capture = new VideoCapture(streamUrl, VideoCaptureAPIs.FFMPEG);
            capture.Set(VideoCaptureProperties.BufferSize, 1);

            if (!capture.IsOpened())
            {
                return (false, "Camera not reachable. Check RTSP URL, user/password, and network.");
            }

            using var frame = new Mat();
            for (int i = 0; i < 10; i++)
            {
                if (capture.Read(frame) && !frame.Empty())
                {
                    return (true, $"Camera is reachable. Frame {frame.Width}×{frame.Height} received.");
                }

                Thread.Sleep(250);
            }

            return (false, "Stream opened but no video frame was received.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            capture?.Release();
            capture?.Dispose();
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        string dialogTitle = _isEditMode ? "Edit Camera" : "Add Camera";
        string name = NameTextBox.Text.Trim();
        string rtsp = RtspTextBox.Text.Trim();
        string matchWindowText = MatchWindowTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rtsp))
        {
            MessageBox.Show(
                "Camera name and RTSP URL are required.",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (ChamberComboBox.SelectedValue is not long chamberId || chamberId <= 0)
        {
            MessageBox.Show(
                "Please select a chamber.",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(matchWindowText, out int matchWindow) ||
            matchWindow <= 0 ||
            matchWindow > 120)
        {
            MessageBox.Show(
                "Match window must be a number between 1 and 120 seconds.",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string purpose = "ENTRY";
        if (PurposeComboBox.SelectedItem is ComboBoxItem selectedItem &&
            selectedItem.Tag is string tag &&
            !string.IsNullOrWhiteSpace(tag))
        {
            purpose = tag;
        }

        long? readerId = null;
        if (ReaderComboBox.SelectedValue is long selectedReader && selectedReader > 0)
        {
            readerId = selectedReader;
        }

        var camera = new MasterCameraConfig
        {
            CameraId = _editingCamera?.CameraId ?? 0,
            CameraName = name,
            ChamberId = chamberId,
            CameraPurpose = purpose,
            IpAddress = string.IsNullOrWhiteSpace(IpTextBox.Text) ? null : IpTextBox.Text.Trim(),
            RtspUrl = rtsp,
            RfidReaderId = readerId,
            PersonDetectionEnabled = PersonDetectionCheckBox.IsChecked == true,
            MatchWindowSeconds = matchWindow,
            AlertOnNoRfid = AlertNoRfidCheckBox.IsChecked == true,
            AlertOnTailgate = AlertTailgateCheckBox.IsChecked == true,
            IsActive = _editingCamera?.IsActive ?? true
        };

        try
        {
            if (_isEditMode)
            {
                await _cameraService.UpdateAsync(camera, _changedBy);
            }
            else
            {
                await _cameraService.AddAsync(camera, _changedBy);
            }

            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, dialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private sealed class ChamberOption
    {
        public long ChamberId { get; init; }

        public string Display { get; init; } = string.Empty;
    }

    private sealed class RfidReaderOption
    {
        public long ReaderId { get; init; }

        public string DisplayLabel { get; init; } = string.Empty;
    }
}
