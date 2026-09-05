using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DashboardService.Helpers;

public interface IListPager : INotifyPropertyChanged
{
    int PageSize { get; }

    int CurrentPage { get; }

    int TotalPages { get; }

    int TotalCount { get; }

    bool CanGoPrevious { get; }

    bool CanGoNext { get; }

    string SummaryText { get; }

    string PageText { get; }

    bool GoPrevious();

    bool GoNext();

    void GoToFirst();
}

public sealed class ListPager<T> : IListPager
{
    private readonly List<T> _all = new();
    private int _pageSize;
    private int _currentPage = 1;

    public ListPager()
        : this(ResolveConfiguredPageSize())
    {
    }

    public ListPager(int pageSize)
    {
        _pageSize = Math.Clamp(pageSize, 5, 200);
        PageItems = new ObservableCollection<T>();
    }

    public static int ResolveConfiguredPageSize()
    {
        try
        {
            return new Services.ConfigurationService().GetListPageSize();
        }
        catch
        {
            return 20;
        }
    }

    public ObservableCollection<T> PageItems { get; }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            int next = Math.Clamp(value, 5, 200);
            if (_pageSize == next)
            {
                return;
            }

            _pageSize = next;
            _currentPage = 1;
            RefreshPage();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(PageText));
        }
    }

    public int CurrentPage => _currentPage;

    public int TotalCount => _all.Count;

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool CanGoPrevious => _currentPage > 1;

    public bool CanGoNext => _currentPage < TotalPages;

    public string SummaryText
    {
        get
        {
            if (TotalCount == 0)
            {
                return "0 items";
            }

            int start = ((_currentPage - 1) * PageSize) + 1;
            int end = Math.Min(_currentPage * PageSize, TotalCount);
            return $"{start}–{end} of {TotalCount}";
        }
    }

    public string PageText => $"Page {_currentPage} / {TotalPages}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetItems(IEnumerable<T>? items)
    {
        _all.Clear();
        if (items != null)
        {
            _all.AddRange(items);
        }

        _currentPage = 1;
        RefreshPage();
        RaiseAll();
    }

    public bool GoPrevious()
    {
        if (!CanGoPrevious)
        {
            return false;
        }

        _currentPage--;
        RefreshPage();
        RaiseAll();
        return true;
    }

    public bool GoNext()
    {
        if (!CanGoNext)
        {
            return false;
        }

        _currentPage++;
        RefreshPage();
        RaiseAll();
        return true;
    }

    public void GoToFirst()
    {
        _currentPage = 1;
        RefreshPage();
        RaiseAll();
    }

    private void RefreshPage()
    {
        if (_currentPage > TotalPages)
        {
            _currentPage = TotalPages;
        }

        if (_currentPage < 1)
        {
            _currentPage = 1;
        }

        PageItems.Clear();
        if (_all.Count == 0)
        {
            return;
        }

        int skip = (_currentPage - 1) * PageSize;
        foreach (var item in _all.Skip(skip).Take(PageSize))
        {
            PageItems.Add(item);
        }
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(PageText));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Non-generic helper for binding DataGrid to a pager without re-assigning ItemsSource each time.</summary>
public static class ListPagerBindings
{
    public static void Attach(System.Windows.Controls.ItemsControl grid, IList pagerPageItems)
    {
        grid.ItemsSource = pagerPageItems;
    }
}
