using System.Windows;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class OpenManualRfidWindow : Window
{
    private readonly long _correctedByUserId;
    private readonly EmployeeService _employeeService = new();
    private readonly ChamberService _chamberService = new();
    private readonly RfidTransactionService _transactionService = new();

    public OpenManualRfidWindow(long correctedByUserId)
    {
        InitializeComponent();
        _correctedByUserId = correctedByUserId;
        Loaded += OpenManualRfidWindow_Loaded;
    }

    private async void OpenManualRfidWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var employees = (await _employeeService.GetAllAsync())
                .Where(x => x.IsActive)
                .Select(x => new EmployeeOption
                {
                    EmployeeId = x.EmployeeId,
                    Display = $"{x.EmployeeCode} · {x.EmployeeName}",
                    Record = x
                })
                .ToList();

            var chambers = (await _chamberService.GetAllAsync())
                .Where(x => x.IsActive)
                .Select(x => new ChamberOption
                {
                    ChamberId = x.ChamberId,
                    Display = x.ToString(),
                    Record = x
                })
                .ToList();

            EmployeeComboBox.ItemsSource = employees;
            ChamberComboBox.ItemsSource = chambers;

            if (employees.Count > 0)
            {
                EmployeeComboBox.SelectedIndex = 0;
            }

            if (chambers.Count > 0)
            {
                ChamberComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Manual RFID Open", MessageBoxButton.OK, MessageBoxImage.Error);
            DialogResult = false;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (EmployeeComboBox.SelectedItem is not EmployeeOption employeeOption)
        {
            MessageBox.Show("Select an employee.", "Manual RFID Open", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ChamberComboBox.SelectedItem is not ChamberOption chamberOption)
        {
            MessageBox.Show("Select a chamber.", "Manual RFID Open", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            string? remarks = RemarksTextBox.Text?.Trim();
            await _transactionService.OpenManualAsync(
                employeeOption.Record,
                chamberOption.Record,
                _correctedByUserId,
                remarks);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Manual RFID Open", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class EmployeeOption
    {
        public long EmployeeId { get; set; }

        public string Display { get; set; } = string.Empty;

        public EmployeeRecord Record { get; set; } = new();
    }

    private sealed class ChamberOption
    {
        public long ChamberId { get; set; }

        public string Display { get; set; } = string.Empty;

        public Chamber Record { get; set; } = new();
    }
}
