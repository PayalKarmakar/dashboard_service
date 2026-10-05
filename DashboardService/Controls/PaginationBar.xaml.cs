using System.Windows;
using System.Windows.Controls;
using DashboardService.Helpers;

namespace DashboardService.Controls;

public partial class PaginationBar : UserControl
{
    public static readonly DependencyProperty ShowItemSummaryProperty =
        DependencyProperty.Register(
            nameof(ShowItemSummary),
            typeof(bool),
            typeof(PaginationBar),
            new PropertyMetadata(true, OnChromePropertyChanged));

    public static readonly DependencyProperty HideWhenSinglePageProperty =
        DependencyProperty.Register(
            nameof(HideWhenSinglePage),
            typeof(bool),
            typeof(PaginationBar),
            new PropertyMetadata(false, OnChromePropertyChanged));

    private IListPager? _pager;
    private Action? _pageChanged;

    public PaginationBar()
    {
        InitializeComponent();
    }

    public bool ShowItemSummary
    {
        get => (bool)GetValue(ShowItemSummaryProperty);
        set => SetValue(ShowItemSummaryProperty, value);
    }

    public bool HideWhenSinglePage
    {
        get => (bool)GetValue(HideWhenSinglePageProperty);
        set => SetValue(HideWhenSinglePageProperty, value);
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
            Visibility = Visibility.Collapsed;
            return;
        }

        SummaryText.Visibility = ShowItemSummary ? Visibility.Visible : Visibility.Collapsed;
        SummaryText.Text = _pager.SummaryText;
        PageText.Text = _pager.PageText;
        PrevButton.IsEnabled = _pager.CanGoPrevious;
        NextButton.IsEnabled = _pager.CanGoNext;

        bool needsPager = _pager.TotalPages > 1;
        Visibility = HideWhenSinglePage && !needsPager
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static void OnChromePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PaginationBar bar)
        {
            bar.RefreshUi();
        }
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
