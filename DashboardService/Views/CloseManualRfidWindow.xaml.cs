using System.Windows;
using DashboardService.Models;

namespace DashboardService.Views;

public partial class CloseManualRfidWindow : Window
{
    public string? Remarks { get; private set; }

    public CloseManualRfidWindow(RfidTransactionRow row)
    {
        InitializeComponent();
        EmployeeText.Text = string.IsNullOrWhiteSpace(row.EmployeeCode)
            ? row.EmployeeName
            : $"{row.EmployeeCode} · {row.EmployeeName}";
        ChamberText.Text = row.ChamberName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Remarks = RemarksTextBox.Text?.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
