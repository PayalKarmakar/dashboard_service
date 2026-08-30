using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DashboardService.Models;

namespace DashboardService.Views;

public partial class CurrentMembersWindow : Window
{
    private const int PageSize = 5;

    private readonly List<Employee> _allMembers;
    private readonly ObservableCollection<Employee> _pageMembers = new();
    private readonly DispatcherTimer _countdownTimer;
    private readonly Action<long>? _stopMemberVoice;
    private readonly Action? _stopAllVoice;
    private int _currentPage = 1;

    public CurrentMembersWindow(
        IEnumerable<Employee> members,
        Action<long>? stopMemberVoice = null,
        Action? stopAllVoice = null)
    {
        InitializeComponent();

        _allMembers = members.ToList();
        _stopMemberVoice = stopMemberVoice;
        _stopAllVoice = stopAllVoice;
        MembersGrid.ItemsSource = _pageMembers;

        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += (_, _) =>
        {
            UpdateCountdowns();
            UpdateStopAllVisibility();
        };
        _countdownTimer.Start();

        Closed += (_, _) => _countdownTimer.Stop();

        RenderPage();
        UpdateStopAllVisibility();
    }

    private int TotalPages =>
        Math.Max(1, (int)Math.Ceiling(_allMembers.Count / (double)PageSize));

    private void RenderPage()
    {
        if (_currentPage > TotalPages)
        {
            _currentPage = TotalPages;
        }

        if (_currentPage < 1)
        {
            _currentPage = 1;
        }

        _pageMembers.Clear();

        foreach (var member in _allMembers
                     .Skip((_currentPage - 1) * PageSize)
                     .Take(PageSize))
        {
            _pageMembers.Add(member);
        }

        UpdateCountdowns();

        SubtitleText.Text = $"{_allMembers.Count} employee(s) currently inside monitored chambers";
        PageInfoText.Text = $"Page {_currentPage} of {TotalPages}";
        PrevButton.IsEnabled = _currentPage > 1;
        NextButton.IsEnabled = _currentPage < TotalPages;
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

    private void UpdateStopAllVisibility()
    {
        StopAllVoiceButton.Visibility = _allMembers.Any(x => x.IsVoicePlaying)
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

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            RenderPage();
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage < TotalPages)
        {
            _currentPage++;
            RenderPage();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
