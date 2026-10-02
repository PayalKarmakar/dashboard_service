using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DashboardService.Controls;

public partial class ToastHostControl : UserControl
{
    private const int MaxToasts = 5;
    private const int AutoDismissSeconds = 7;

    public ToastHostControl()
    {
        InitializeComponent();
    }

    public void ShowToast(string title, string message, bool isCritical)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowToast(title, message, isCritical));
            return;
        }

        var card = BuildToastCard(title, message, isCritical);
        ToastPanel.Children.Insert(0, card);

        while (ToastPanel.Children.Count > MaxToasts)
        {
            ToastPanel.Children.RemoveAt(ToastPanel.Children.Count - 1);
        }

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(AutoDismissSeconds)
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ToastPanel.Children.Remove(card);
        };

        timer.Start();
    }

    private static Border BuildToastCard(string title, string message, bool isCritical)
    {
        string bg = isCritical ? "#FEF2F2" : "#FFFBEB";
        string border = isCritical ? "#FECACA" : "#FDE68A";
        string titleColor = isCritical ? "#7F1D1D" : "#92400E";
        string messageColor = isCritical ? "#991B1B" : "#A16207";

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(titleColor)),
            TextWrapping = TextWrapping.Wrap
        };

        var messageBlock = new TextBlock
        {
            Text = message,
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(messageColor)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var closeButton = new Button
        {
            Content = "✕",
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(titleColor)),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top
        };

        var content = new Grid
        {
            Margin = new Thickness(14, 12, 10, 12)
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textPanel = new StackPanel();
        textPanel.Children.Add(titleBlock);
        textPanel.Children.Add(messageBlock);

        Grid.SetColumn(textPanel, 0);
        Grid.SetColumn(closeButton, 1);

        content.Children.Add(textPanel);
        content.Children.Add(closeButton);

        var card = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 0, 0, 10),
            MinWidth = 320,
            MaxWidth = 420,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 2,
                Opacity = 0.18,
                Color = Colors.Black
            },
            Child = content
        };

        closeButton.Click += (_, _) =>
        {
            if (card.Parent is Panel panel)
            {
                panel.Children.Remove(card);
            }
        };

        return card;
    }
}
