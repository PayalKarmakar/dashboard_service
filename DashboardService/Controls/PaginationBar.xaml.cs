using System.Windows;
using System.Windows.Controls;
using DashboardService.Helpers;

namespace DashboardService.Controls;

public partial class PaginationBar : UserControl
{
    private IListPager? _pager;
    private Action? _pageChanged;

    public PaginationBar()
    {
        InitializeComponent();
    }

    public void Bind(IListPager pager, Action? pageChanged = null)
    {
        if (_pager != null)
        {
            _pager.PropertyChanged -= Pager_PropertyChanged;
        }

        _pager = pager;
        _pageChanged = pageChanged;
        _pager.PropertyChanged += Pager_PropertyChanged;
        RefreshUi();
    }

    public void RefreshUi()
    {
        if (_pager == null)
        {
            SummaryText.Text = string.Empty;
            PageText.Text = string.Empty;
            PrevButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            return;
        }

        SummaryText.Text = _pager.SummaryText;
        PageText.Text = _pager.PageText;
        PrevButton.IsEnabled = _pager.CanGoPrevious;
        NextButton.IsEnabled = _pager.CanGoNext;
    }

    private void Pager_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        Dispatcher.Invoke(RefreshUi);

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_pager?.GoPrevious() == true)
        {
            _pageChanged?.Invoke();
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_pager?.GoNext() == true)
        {
            _pageChanged?.Invoke();
        }
    }
}
