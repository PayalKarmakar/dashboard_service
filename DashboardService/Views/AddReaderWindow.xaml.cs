using System.Windows;
using System.Windows.Controls;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class AddReaderWindow : Window
{
    private readonly RfidReaderService _readerService = new();
    private readonly long _changedBy;
    private readonly RfidReader? _editingReader;
    private readonly bool _isEditMode;

    public AddReaderWindow(long changedBy, RfidReader? existingReader = null)
    {
        InitializeComponent();
        _changedBy = changedBy;
        _editingReader = existingReader;
        _isEditMode = existingReader != null;

        if (_isEditMode && _editingReader != null)
        {
            Title = "Edit RFID Reader";
            TitleText.Text = "Edit RFID Reader";
            SubtitleText.Text = "Update RFID reader device details";
            SaveButton.Content = "Update";

            NameTextBox.Text = _editingReader.ReaderName;
            SerialTextBox.Text = _editingReader.ReaderSerialNo;
            IpTextBox.Text = _editingReader.IpAddress;
            PortTextBox.Text = _editingReader.Port.ToString();

            foreach (ComboBoxItem item in PurposeComboBox.Items)
            {
                if (item.Tag is string tag &&
                    tag.Equals(_editingReader.ReaderPurpose, StringComparison.OrdinalIgnoreCase))
                {
                    PurposeComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        NameTextBox.Focus();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        string name = NameTextBox.Text.Trim();
        string serial = SerialTextBox.Text.Trim();
        string ip = IpTextBox.Text.Trim();
        string portText = PortTextBox.Text.Trim();
        string dialogTitle = _isEditMode ? "Edit RFID Reader" : "Add RFID Reader";

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(serial) ||
            string.IsNullOrWhiteSpace(ip) ||
            string.IsNullOrWhiteSpace(portText))
        {
            MessageBox.Show(
                "All fields are required.",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(portText, out int port) || port < 1 || port > 65535)
        {
            MessageBox.Show(
                "Port must be a number between 1 and 65535.",
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

        var reader = new RfidReader
        {
            ReaderId = _editingReader?.ReaderId ?? 0,
            ReaderName = name,
            ReaderSerialNo = serial,
            IpAddress = ip,
            Port = port,
            ReaderPurpose = purpose
        };

        try
        {
            if (_isEditMode)
            {
                await _readerService.UpdateAsync(reader, _changedBy);
            }
            else
            {
                await _readerService.AddAsync(reader, _changedBy);
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
}
