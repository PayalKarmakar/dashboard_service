using System.Windows;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class AddChamberWindow : Window
{
    private readonly ChamberService _chamberService = new();
    private readonly long _changedBy;
    private readonly Chamber? _editingChamber;
    private readonly bool _isEditMode;

    public AddChamberWindow(long changedBy, Chamber? existingChamber = null)
    {
        InitializeComponent();
        _changedBy = changedBy;
        _editingChamber = existingChamber;
        _isEditMode = existingChamber != null;

        if (_isEditMode && _editingChamber != null)
        {
            Title = "Edit Chamber";
            TitleText.Text = "Edit Chamber";
            SubtitleText.Text = "Update chamber details and time threshold";
            SaveButton.Content = "Update";

            CodeTextBox.Text = _editingChamber.ChamberCode;
            NameTextBox.Text = _editingChamber.ChamberName;
            LocationTextBox.Text = _editingChamber.ChamberLocation;
            MemberThresholdTextBox.Text = _editingChamber.MemberThreshold?.ToString() ?? string.Empty;
            TimeThresholdTextBox.Text = _editingChamber.TimeThreshold?.ToString() ?? string.Empty;
        }

        CodeTextBox.Focus();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        string code = CodeTextBox.Text.Trim();
        string name = NameTextBox.Text.Trim();
        string dialogTitle = _isEditMode ? "Edit Chamber" : "Add Chamber";

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(
                "Chamber code and name are required.",
                dialogTitle,
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
            ChamberId = _editingChamber?.ChamberId ?? 0,
            ChamberCode = code,
            ChamberName = name,
            ChamberLocation = LocationTextBox.Text.Trim(),
            MemberThreshold = memberThreshold,
            TimeThreshold = timeThreshold
        };

        try
        {
            if (_isEditMode)
            {
                await _chamberService.UpdateAsync(chamber, _changedBy);
            }
            else
            {
                await _chamberService.AddAsync(chamber, _changedBy);
            }

            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                dialogTitle,
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
                "Chamber",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        value = parsed;
        return true;
    }
}
