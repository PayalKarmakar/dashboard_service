using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class AddEmployeeWindow : Window
{
    private sealed class ReaderPickerItem
    {
        public RfidReader? Reader { get; init; }
        public string Label { get; init; } = "Please select";
        public bool IsPlaceholder => Reader == null;
    }

    private sealed class ChamberPickerItem
    {
        public Chamber? Chamber { get; init; }
        public string Label { get; init; } = "Please select";
        public bool IsPlaceholder => Chamber == null;
    }

    private readonly EmployeeService _employeeService = new();
    private readonly ChamberService _chamberService = new();
    private readonly RfidReaderService _readerService = new();
    private readonly EmployeeRegistrationApiService _registrationApi = new();
    private readonly long _createdBy;
    private CancellationTokenSource? _scanCts;
    private bool _suppressReaderChange;
    private bool _hasValidUnassignedCard;
    private string _scannedCardUid = string.Empty;
    private long? _selectedReaderId;

    public AddEmployeeWindow(long createdBy)
    {
        InitializeComponent();
        _createdBy = createdBy;
        Loaded += AddEmployeeWindow_Loaded;
    }

    private async void AddEmployeeWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SetDetailsUnlocked(false);
            SetWaitingUi(false);

            var activeChambers = (await _chamberService.GetAllAsync())
                .Where(x => x.IsActive)
                .OrderBy(x => x.ChamberName)
                .ToList();

            var chamberItems = new List<ChamberPickerItem>
            {
                new() { Label = "Please select" }
            };
            chamberItems.AddRange(activeChambers.Select(c => new ChamberPickerItem
            {
                Chamber = c,
                Label = c.ToString()
            }));

            ChamberComboBox.ItemsSource = chamberItems;
            ChamberComboBox.SelectedIndex = 0;

            var readers = await _readerService.GetAllAsync();
            var registrationReaders = readers
                .Where(r => r.IsActive
                    && IsEmployeeRegistrationPurpose(r.ReaderPurpose))
                .OrderBy(r => r.ReaderName)
                .ToList();

            var pickerItems = new List<ReaderPickerItem>
            {
                new() { Label = "Please select" }
            };
            pickerItems.AddRange(registrationReaders.Select(r => new ReaderPickerItem
            {
                Reader = r,
                Label = r.DisplayLabel
            }));

            _suppressReaderChange = true;
            RegistrationReaderComboBox.ItemsSource = pickerItems;
            RegistrationReaderComboBox.SelectedIndex = 0;
            RegistrationReaderComboBox.IsEnabled = registrationReaders.Count > 0;
            _suppressReaderChange = false;

            if (registrationReaders.Count == 0)
            {
                ScanStatusText.Text =
                    "No EMPLOYEE_REGISTRATION reader. Add one under RFID Readers.";
                ScanStatusText.Foreground = (Brush)FindResource("DangerBrush");
                StartScanButton.IsEnabled = false;
            }
            else
            {
                ScanStatusText.Text = "Select reader from dropdown, then Wait for Punch.";
                ScanStatusText.Foreground = (Brush)FindResource("TextMutedBrush");
            }
            UpdateStartScanButtonState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Add Employee",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static bool IsEmployeeRegistrationPurpose(string? purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            return false;
        }

        string normalized = purpose.Trim().Replace(" ", "_").Replace("/", "_");
        return string.Equals(normalized, "EMPLOYEE_REGISTRATION", StringComparison.OrdinalIgnoreCase)
            || string.Equals(purpose.Trim(), "Employee Registration", StringComparison.OrdinalIgnoreCase);
    }

    private void LostCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool isLost = LostCheckBox.IsChecked == true;
        if (isLost)
        {
            ActiveCheckBox.IsChecked = false;
            ActiveCheckBox.IsEnabled = false;
        }
        else
        {
            ActiveCheckBox.IsEnabled = true;
            if (ActiveCheckBox.IsChecked != true)
            {
                ActiveCheckBox.IsChecked = true;
            }
        }
    }

    private Chamber? GetSelectedChamber()
    {
        if (ChamberComboBox.SelectedItem is ChamberPickerItem item && !item.IsPlaceholder)
        {
            return item.Chamber;
        }

        return null;
    }

    private RfidReader? GetSelectedReader()
    {
        if (RegistrationReaderComboBox.SelectedItem is ReaderPickerItem item && !item.IsPlaceholder)
        {
            return item.Reader;
        }

        return null;
    }

    private void RegistrationReaderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressReaderChange)
        {
            return;
        }

        if (GetSelectedReader() is not RfidReader reader)
        {
            _selectedReaderId = null;
            CancelActiveScan();
            SetWaitingUi(false);
            ScanStatusText.Text = "Select reader, then Wait for Punch.";
            ScanStatusText.Foreground = (Brush)FindResource("TextMutedBrush");
            UpdateStartScanButtonState();
            return;
        }

        _selectedReaderId = reader.ReaderId;
        CancelActiveScan();
        SetWaitingUi(false);
        ScanStatusText.Text = $"{reader.ReaderName} selected — click Wait for Punch.";
        ScanStatusText.Foreground = (Brush)FindResource("TextMutedBrush");
        UpdateStartScanButtonState();
    }

    private void UpdateStartScanButtonState()
    {
        bool hasReader = GetSelectedReader() != null;
        bool waiting = WaitingPanel.Visibility == Visibility.Visible;
        StartScanButton.IsEnabled = hasReader && !waiting;
    }

    private async void StartScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedReader() is not RfidReader reader)
        {
            MessageBox.Show(
                "Please select a card reader first.",
                "Add Employee",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        await StartCardScanAsync(reader);
    }

    private void SetDetailsUnlocked(bool unlocked)
    {
        EmployeeDetailsPanel.IsEnabled = unlocked;
        EmployeeDetailsPanel.Opacity = unlocked ? 1.0 : 0.55;
        UpdateSaveButtonState();
    }

    private void SetWaitingUi(bool waiting)
    {
        WaitingPanel.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;

        // Only lock dropdown while waiting for punch — otherwise fully selectable.
        RegistrationReaderComboBox.IsEnabled = !waiting
            && RegistrationReaderComboBox.Items.Count > 0;

        var spin = (Storyboard)FindResource("LoaderSpinStoryboard");
        if (waiting)
        {
            spin.Begin(this, true);
        }
        else
        {
            spin.Stop(this);
            UpdateStartScanButtonState();
        }
    }

    private void UpdateSaveButtonState()
    {
        bool canSave = _hasValidUnassignedCard
            && !string.IsNullOrWhiteSpace(_scannedCardUid)
            && string.Equals(
                CardUidTextBox.Text.Trim(),
                _scannedCardUid,
                StringComparison.OrdinalIgnoreCase);

        SaveButton.IsEnabled = canSave;
    }

    private void CancelActiveScan()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
    }

    private async Task StartCardScanAsync(RfidReader reader)
    {
        CancelActiveScan();

        _scanCts = new CancellationTokenSource();
        _hasValidUnassignedCard = false;
        _scannedCardUid = string.Empty;
        CardUidTextBox.Text = string.Empty;
        AssignedCardBanner.Visibility = Visibility.Collapsed;
        AssignedEmployeeText.Text = string.Empty;
        SetDetailsUnlocked(false);

        WaitingHintText.Text = $"Waiting for punch on {reader.ReaderName}...";
        SetWaitingUi(true);
        ScanStatusText.Text = "Loader is active — punch the RFID card on the selected reader.";
        ScanStatusText.Foreground = (Brush)FindResource("TextMutedBrush");

        try
        {
            string cardUid = await _registrationApi.WaitForCardScanAsync(
                reader.ReaderId,
                _scanCts.Token);

            await ApplyScannedCardAsync(cardUid);
        }
        catch (OperationCanceledException) when (_scanCts?.IsCancellationRequested == true)
        {
            ScanStatusText.Text = "Card scan cancelled. Select reader and click Wait for Punch again.";
            SetDetailsUnlocked(false);
        }
        catch (OperationCanceledException)
        {
            ScanStatusText.Text = "Card scan timed out. Click Wait for Punch and punch again.";
            SetDetailsUnlocked(false);
            MessageBox.Show(
                "No RFID card was detected on the selected reader.",
                "Add Employee",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (TimeoutException)
        {
            ScanStatusText.Text = "Card scan timed out. Click Wait for Punch and punch again.";
            SetDetailsUnlocked(false);
            MessageBox.Show(
                "No RFID card was detected on the selected reader.",
                "Add Employee",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = "Scan failed. Click Wait for Punch to retry.";
            ScanStatusText.Foreground = (Brush)FindResource("DangerBrush");
            SetDetailsUnlocked(false);
            MessageBox.Show(
                ex.Message,
                "Add Employee",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetWaitingUi(false);
            UpdateSaveButtonState();
        }
    }

    private async Task ApplyScannedCardAsync(string cardUid)
    {
        cardUid = (cardUid ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cardUid))
        {
            _hasValidUnassignedCard = false;
            _scannedCardUid = string.Empty;
            SetDetailsUnlocked(false);
            ScanStatusText.Text = "Invalid card scan. Click Wait for Punch and try again.";
            ScanStatusText.Foreground = (Brush)FindResource("DangerBrush");
            return;
        }

        var existing = await _employeeService.FindByCardUidAsync(cardUid);
        if (existing != null)
        {
            _hasValidUnassignedCard = false;
            _scannedCardUid = string.Empty;
            CardUidTextBox.Text = string.Empty;
            SetDetailsUnlocked(false);
            AssignedCardBanner.Visibility = Visibility.Visible;

            string chamberPart = string.IsNullOrWhiteSpace(existing.ChamberName)
                ? string.Empty
                : $" | Chamber: {existing.ChamberName}";
            string deptPart = string.IsNullOrWhiteSpace(existing.Department)
                ? string.Empty
                : $" | Dept: {existing.Department}";

            AssignedEmployeeText.Text =
                $"Assigned to: {existing.EmployeeName} ({existing.EmployeeCode}){deptPart}{chamberPart}\n" +
                $"Card UID: {cardUid}";

            ScanStatusText.Text = "This card is already assigned. Punch another card.";
            ScanStatusText.Foreground = (Brush)FindResource("DangerBrush");

            MessageBox.Show(
                $"This RFID card is already assigned to:\n\n" +
                $"{existing.EmployeeName} ({existing.EmployeeCode})\n\n" +
                "Cannot register with this card. Punch a different card.",
                "Card Already Assigned",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _hasValidUnassignedCard = true;
        _scannedCardUid = cardUid;
        AssignedCardBanner.Visibility = Visibility.Collapsed;
        AssignedEmployeeText.Text = string.Empty;
        CardUidTextBox.Text = cardUid;
        ScanStatusText.Text = $"Card punched successfully. UID: {cardUid} — fill employee details below.";
        ScanStatusText.Foreground = (Brush)FindResource("AccentBrush");
        SetDetailsUnlocked(true);
        CodeTextBox.Focus();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasValidUnassignedCard || string.IsNullOrWhiteSpace(_scannedCardUid))
        {
            MessageBox.Show(
                "Card punch is required first. Select a registration reader and punch a card.",
                "Add Employee",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string cardUid = _scannedCardUid;
        if (!string.Equals(CardUidTextBox.Text.Trim(), cardUid, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Card UID was changed manually. Please punch the card again.",
                "Add Employee",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _hasValidUnassignedCard = false;
            _scannedCardUid = string.Empty;
            CardUidTextBox.Text = string.Empty;
            SetDetailsUnlocked(false);
            return;
        }

        string code = CodeTextBox.Text.Trim();
        string name = NameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(
                "Employee code and name are required.",
                "Add Employee",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var existing = await _employeeService.FindByCardUidAsync(cardUid);
        if (existing != null)
        {
            _hasValidUnassignedCard = false;
            _scannedCardUid = string.Empty;
            CardUidTextBox.Text = string.Empty;
            SetDetailsUnlocked(false);
            AssignedCardBanner.Visibility = Visibility.Visible;
            AssignedEmployeeText.Text =
                $"Assigned to: {existing.EmployeeName} ({existing.EmployeeCode})";
            MessageBox.Show(
                $"This RFID card is already assigned to {existing.EmployeeName} ({existing.EmployeeCode}).",
                "Card Already Assigned",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        bool isLost = LostCheckBox.IsChecked == true;
        var employee = new EmployeeRecord
        {
            EmployeeCode = code,
            EmployeeName = name,
            CardUid = cardUid,
            Department = DepartmentTextBox.Text.Trim(),
            Designation = DesignationTextBox.Text.Trim(),
            Mobile = MobileTextBox.Text.Trim(),
            ChamberId = GetSelectedChamber()?.ChamberId,
            IsLost = isLost,
            IsActive = !isLost && ActiveCheckBox.IsChecked == true
        };

        try
        {
            SaveButton.IsEnabled = false;
            await _employeeService.AddAsync(employee, _createdBy);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Add Employee",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            UpdateSaveButtonState();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CancelActiveScan();
    }
}
