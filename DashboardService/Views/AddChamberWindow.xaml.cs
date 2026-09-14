using System.Windows;
using DashboardService.Models;
using DashboardService.Services;

namespace DashboardService.Views;

public partial class AddChamberWindow : Window
{
    private readonly ChamberService _chamberService = new();
    private readonly ConfigurationService _configurationService = new();
    private readonly AlertMessageService _alertMessageService = new();
    private readonly long _changedBy;
    private Chamber? _editingChamber;
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
            SubtitleText.Text = "Values load from the database. Update chamber details and time-alert rules.";
            SaveButton.Content = "Update";
        }

        Loaded += AddChamberWindow_Loaded;
        CodeTextBox.Focus();
    }

    private async void AddChamberWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = _configurationService.GetAlertSettings();
            string warningExample = await GetDefaultTemplateAsync(
                ChamberService.WarningAlertType,
                settings.WarningMessage);
            string violationExample = await GetDefaultTemplateAsync(
                ChamberService.ViolationAlertType,
                settings.ViolationMessage);
            string halfTimeExample = await GetDefaultTemplateAsync(
                ChamberService.HalfTimeAlertType,
                settings.AttentionMessage);

            WarningExampleText.Text = "Example: " + warningExample;
            ViolationExampleText.Text = "Example: " + violationExample;
            HalfTimeExampleText.Text = "Example: " + halfTimeExample;
            SystemDefinitionText.Text = BuildSystemDefinition(settings.RepeatAfterViolationMinutes);

            int defaultWarningBefore = settings.WarningRemainingMinutes > 0
                ? settings.WarningRemainingMinutes
                : 10;
            int defaultRepeatAfter = settings.RepeatAfterViolationMinutes > 0
                ? settings.RepeatAfterViolationMinutes
                : 5;
            ApplyRule(null, WarningBeforeTextBox, WarningPlayCountTextBox, WarningAudioCheckBox, WarningMessageTextBox,
                defaultWarningBefore, 1, allowContinue: false);
            ApplyRule(null, ViolationAfterTextBox, ViolationPlayCountTextBox, ViolationAudioCheckBox, ViolationMessageTextBox,
                0, ChamberAlertRule.ContinuePlayCount, allowContinue: true);
            RestartAfterStopTextBox.Text = defaultRepeatAfter.ToString();

            if (!_isEditMode || _editingChamber == null)
            {
                TimeThresholdTextBox.Text = settings.AfterMinutes.ToString();
                int addHalfDefault = Math.Max(1, settings.AfterMinutes / 2);
                ApplyRule(null, HalfTimeMinutesTextBox, HalfTimePlayCountTextBox, HalfTimeAudioCheckBox, HalfTimeMessageTextBox,
                    addHalfDefault, 1, allowContinue: false);
                return;
            }

            var chamber = await _chamberService.GetByIdAsync(_editingChamber.ChamberId) ?? _editingChamber;
            _editingChamber = chamber;

            CodeTextBox.Text = chamber.ChamberCode;
            NameTextBox.Text = chamber.ChamberName;
            LocationTextBox.Text = chamber.ChamberLocation;
            MemberThresholdTextBox.Text = chamber.MemberThreshold?.ToString() ?? string.Empty;
            TimeThresholdTextBox.Text = chamber.TimeThreshold?.ToString() ?? string.Empty;

            var rules = await _chamberService.GetRulesAsync(chamber.ChamberId);
            ApplyRule(
                rules.FirstOrDefault(r => r.AlertType.Equals(ChamberService.WarningAlertType, StringComparison.OrdinalIgnoreCase)),
                WarningBeforeTextBox,
                WarningPlayCountTextBox,
                WarningAudioCheckBox,
                WarningMessageTextBox,
                defaultWarningBefore,
                1,
                allowContinue: false);

            ApplyRule(
                rules.FirstOrDefault(r => r.AlertType.Equals(ChamberService.ViolationAlertType, StringComparison.OrdinalIgnoreCase)),
                ViolationAfterTextBox,
                ViolationPlayCountTextBox,
                ViolationAudioCheckBox,
                ViolationMessageTextBox,
                0,
                ChamberAlertRule.ContinuePlayCount,
                allowContinue: true);

            var violationRule = rules.FirstOrDefault(r =>
                r.AlertType.Equals(ChamberService.ViolationAlertType, StringComparison.OrdinalIgnoreCase));
            RestartAfterStopTextBox.Text = (violationRule?.RepeatAfterMinutes > 0
                ? violationRule.RepeatAfterMinutes
                : defaultRepeatAfter).ToString();

            int halfDefault = Math.Max(1, (chamber.TimeThreshold ?? settings.AfterMinutes) / 2);
            ApplyRule(
                rules.FirstOrDefault(r => r.AlertType.Equals(ChamberService.HalfTimeAlertType, StringComparison.OrdinalIgnoreCase)),
                HalfTimeMinutesTextBox,
                HalfTimePlayCountTextBox,
                HalfTimeAudioCheckBox,
                HalfTimeMessageTextBox,
                halfDefault,
                1,
                allowContinue: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                _isEditMode ? "Edit Chamber" : "Add Chamber",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
            !TryParseRequiredInt(TimeThresholdTextBox.Text, "Time threshold", out int timeThreshold) ||
            !TryParseRequiredInt(WarningBeforeTextBox.Text, "Warning minutes before", out int warningBefore) ||
            !TryParseRequiredInt(WarningPlayCountTextBox.Text, "Warning play count", out int warningCount) ||
            !TryParseRequiredInt(HalfTimeMinutesTextBox.Text, "Half-time minutes", out int halfTimeMinutes) ||
            !TryParseRequiredInt(HalfTimePlayCountTextBox.Text, "Half-time play count", out int halfTimeCount) ||
            !TryParseRequiredInt(ViolationAfterTextBox.Text, "Violation minutes after", out int violationAfter) ||
            !TryParseViolationPlayCount(ViolationPlayCountTextBox.Text, dialogTitle, out int violationCount) ||
            !TryParseRequiredInt(RestartAfterStopTextBox.Text, "Restart after Stop", out int restartAfterStop))
        {
            return;
        }

        if (warningCount < 1)
        {
            MessageBox.Show(
                "Warning play count must be at least 1.",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (halfTimeCount < 1)
        {
            MessageBox.Show(
                "Half-time play count must be at least 1.",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (restartAfterStop < 1)
        {
            MessageBox.Show(
                "Restart after Stop must be at least 1 minute.",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (warningBefore >= timeThreshold)
        {
            MessageBox.Show(
                "Warning minutes before must be less than the time threshold.",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (halfTimeMinutes < 1 || halfTimeMinutes >= timeThreshold)
        {
            MessageBox.Show(
                "Half-time minutes must be between 1 and less than the time threshold.",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        int warningAt = timeThreshold - warningBefore;
        if (halfTimeMinutes >= warningAt)
        {
            MessageBox.Show(
                "Half-time must be earlier than the warning (before time threshold minus warning minutes).",
                dialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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
            long chamberId = chamber.ChamberId;
            if (_isEditMode)
            {
                await _chamberService.UpdateAsync(chamber, _changedBy);
            }
            else
            {
                chamberId = await _chamberService.AddAsync(chamber, _changedBy);
            }

            await _chamberService.SaveRulesAsync(chamberId,
            [
                new ChamberAlertRule
                {
                    AlertType = ChamberService.HalfTimeAlertType,
                    AlertTimeMinutes = halfTimeMinutes,
                    MaxPlayCount = halfTimeCount,
                    RepeatAfterMinutes = restartAfterStop,
                    IsAnnouncementEnabled = HalfTimeAudioCheckBox.IsChecked == true,
                    IsActive = true,
                    AnnouncementMessage = HalfTimeMessageTextBox.Text.Trim()
                },
                new ChamberAlertRule
                {
                    AlertType = ChamberService.WarningAlertType,
                    AlertTimeMinutes = warningBefore,
                    MaxPlayCount = warningCount,
                    RepeatAfterMinutes = restartAfterStop,
                    IsAnnouncementEnabled = WarningAudioCheckBox.IsChecked == true,
                    IsActive = true,
                    AnnouncementMessage = WarningMessageTextBox.Text.Trim()
                },
                new ChamberAlertRule
                {
                    AlertType = ChamberService.ViolationAlertType,
                    AlertTimeMinutes = violationAfter,
                    MaxPlayCount = violationCount,
                    RepeatAfterMinutes = restartAfterStop,
                    IsAnnouncementEnabled = ViolationAudioCheckBox.IsChecked == true,
                    IsActive = true,
                    AnnouncementMessage = ViolationMessageTextBox.Text.Trim()
                }
            ]);

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

    private async Task<string> GetDefaultTemplateAsync(string alertType, string fallback)
    {
        var templates = await _alertMessageService.GetTemplatesAsync(
            AlertMessageService.CategoryEmployee,
            alertType);

        if (templates.TryGetValue(AlertMessageService.CultureEnglishIndia, out string? template) &&
            !string.IsNullOrWhiteSpace(template))
        {
            return template.Trim();
        }

        return fallback.Trim();
    }

    private static string BuildSystemDefinition(int repeatAfterMinutes)
    {
        return
            "Time Threshold is the permitted stay inside the chamber (from entry time).\n\n" +
            "Half-time fires that many minutes AFTER entry (default is half of Time Threshold). Status becomes Attention. " +
            "Voice speaks 2 times per alert then stops.\n\n" +
            "Warning fires that many minutes BEFORE the threshold. Dashboard status becomes Warning. " +
            "If Play warning audio is on, voice starts. Play count is how many warning alerts are created " +
            $"(first immediately, then every {repeatAfterMinutes} minutes). Warning voice speaks 2 times per alert then stops.\n\n" +
            "Violation fires that many minutes AFTER the threshold (0 = exactly at expiry). Status becomes Violation. " +
            "If Play violation audio is on, voice loops until the member exits when play count is continue. " +
            "Stop pauses it. Restart after Stop is how many minutes later the next session starts " +
            "(only if play count still allows it). A number caps how many sessions are created.\n\n" +
            "Leave message empty to use the system default template shown in Example. Saved text is stored in " +
            "chamber_alert_rules.announcement_message. Placeholders: {EmployeeName}, {ChamberName}, " +
            "{HalfTimeMinutes}, {WarningRemainingMinutes}, {AfterMinutes}.";
    }

    private static void ApplyRule(
        ChamberAlertRule? rule,
        System.Windows.Controls.TextBox minutesBox,
        System.Windows.Controls.TextBox countBox,
        System.Windows.Controls.CheckBox audioBox,
        System.Windows.Controls.TextBox messageBox,
        int defaultMinutes,
        int defaultCount,
        bool allowContinue)
    {
        if (rule == null)
        {
            minutesBox.Text = defaultMinutes.ToString();
            countBox.Text = ChamberAlertRule.FormatPlayCount(defaultCount, allowContinue);
            audioBox.IsChecked = true;
            messageBox.Text = string.Empty;
            return;
        }

        minutesBox.Text = rule.AlertTimeMinutes.ToString();
        countBox.Text = ChamberAlertRule.FormatPlayCount(rule.MaxPlayCount, allowContinue);
        audioBox.IsChecked = rule.IsAnnouncementEnabled;
        messageBox.Text = rule.AnnouncementMessage;
    }

    private static bool TryParseViolationPlayCount(string text, string dialogTitle, out int playCount)
    {
        if (ChamberAlertRule.TryParsePlayCount(text, allowContinue: true, out playCount))
        {
            return true;
        }

        MessageBox.Show(
            "Violation play count must be continue or a number of 1 or more.",
            dialogTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
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

    private static bool TryParseRequiredInt(string text, string fieldName, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text.Trim(), out value) || value < 0)
        {
            MessageBox.Show(
                $"{fieldName} is required and must be 0 or more.",
                "Chamber",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        return true;
    }
}
