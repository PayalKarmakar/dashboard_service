using System.Windows;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class AddChamberWindow : Window
{
    private readonly ChamberService _chamberService = new();
    private readonly long _createdBy;

    public AddChamberWindow(long createdBy)
    {
        InitializeComponent();
        _createdBy = createdBy;
        CodeTextBox.Focus();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        string code = CodeTextBox.Text.Trim();
        string name = NameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(
                "Chamber code and name are required.",
                "Add Chamber",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!TryParseOptionalInt(MemberThresholdTextBox.Text, "Member threshold", out int? memberThreshold) ||
            !TryParseOptionalInt(TimeThresholdTextBox.Text, "Time threshold", out int? timeThreshold))
        {
            return;
        }

        var chamber = new Chamber
        {
            ChamberCode = code,
            ChamberName = name,
            ChamberLocation = LocationTextBox.Text.Trim(),
            MemberThreshold = memberThreshold,
            TimeThreshold = timeThreshold
        };

        try
        {
            await _chamberService.AddAsync(chamber, _createdBy);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Add Chamber",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static bool TryParseOptionalInt(string text, string fieldName, out int? value)
    {
        value = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text.Trim(), out int parsed) || parsed < 0)
        {
            MessageBox.Show(
                $"{fieldName} must be a valid number.",
                "Add Chamber",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        value = parsed;
        return true;
    }
}
