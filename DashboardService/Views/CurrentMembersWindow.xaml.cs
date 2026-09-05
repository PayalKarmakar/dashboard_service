using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DashboardService.Helpers;
using DashboardService.Models;

namespace DashboardService.Views;

public partial class CurrentMembersWindow : Window
{
    private readonly List<Employee> _allMembers;
    private readonly ListPager<Employee> _membersPager = new();
    private readonly DispatcherTimer _countdownTimer;
    private readonly Action<long>? _stopMemberVoice;
    private readonly Action? _stopAllVoice;
    private readonly Func<long, bool>? _isVoicePlaying;
    private readonly Func<bool>? _hasAnyVoicePlaying;
    private readonly Action<long, bool> _voicePlayingChangedHandler;

    public CurrentMembersWindow(
        IEnumerable<Employee> members,
        Action<long>? stopMemberVoice = null,
        Action? stopAllVoice = null,
        Func<long, bool>? isVoicePlaying = null,
        Func<bool>? hasAnyVoicePlaying = null,
        Action<Action<long, bool>>? subscribeVoicePlayingChanged = null,
        Action<Action<long, bool>>? unsubscribeVoicePlayingChanged = null)
    {
        InitializeComponent();

        _allMembers = members.ToList();
        _stopMemberVoice = stopMemberVoice;
        _stopAllVoice = stopAllVoice;
        _isVoicePlaying = isVoicePlaying;
        _hasAnyVoicePlaying = hasAnyVoicePlaying;
        _voicePlayingChangedHandler = OnVoicePlayingChanged;

        MembersPagerBar.Bind(_membersPager);
        MembersGrid.ItemsSource = _membersPager.PageItems;
        _membersPager.SetItems(_allMembers);

        subscribeVoicePlayingChanged?.Invoke(_voicePlayingChangedHandler);

        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += (_, _) =>
        {
            UpdateCountdowns();
            SyncVoicePlayingFlags();
            UpdateStopAllVisibility();
        };
        _countdownTimer.Start();

        Closed += (_, _) =>
        {
            _countdownTimer.Stop();
            unsubscribeVoicePlayingChanged?.Invoke(_voicePlayingChangedHandler);
        };

        SyncVoicePlayingFlags();
        SubtitleText.Text = $"{_allMembers.Count} employee(s) currently inside monitored chambers";
        UpdateStopAllVisibility();
    }

    private void UpdateCountdowns()
    {
        foreach (var employee in _allMembers)
        {
            DateTime allowedExitTime =
                employee.EntryTime.AddMinutes(employee.TimeThresholdMinutes);

            employee.RemainingTime = allowedExitTime - DateTime.Now;
        }
    }

    private void SyncVoicePlayingFlags()
    {
        if (_isVoicePlaying == null)
        {
            return;
        }

        foreach (var employee in _allMembers)
        {
            employee.IsVoicePlaying = _isVoicePlaying(employee.TransactionId);
        }
    }

    private void OnVoicePlayingChanged(long transactionId, bool isPlaying)
    {
        Dispatcher.Invoke(() =>
        {
            foreach (var employee in _allMembers.Where(x => x.TransactionId == transactionId))
            {
                employee.IsVoicePlaying = isPlaying;
            }

            UpdateStopAllVisibility();
        });
    }

    private void UpdateStopAllVisibility()
    {
        bool anyPlaying = _allMembers.Any(x => x.IsVoicePlaying);

        if (!anyPlaying && _hasAnyVoicePlaying != null)
        {
            anyPlaying = _hasAnyVoicePlaying();
        }

        StopAllVoiceButton.Visibility = anyPlaying
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void StopMemberVoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Employee employee })
        {
            return;
        }

        _stopMemberVoice?.Invoke(employee.TransactionId);
        employee.IsVoicePlaying = false;
        UpdateStopAllVisibility();
    }

    private void StopAllVoice_Click(object sender, RoutedEventArgs e)
    {
        _stopAllVoice?.Invoke();

        foreach (var employee in _allMembers)
        {
            employee.IsVoicePlaying = false;
        }

        UpdateStopAllVisibility();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
