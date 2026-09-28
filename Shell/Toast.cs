using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace SirWordALot.Shell;

/// <summary>A transient message in the bottom-right corner, in the institutional voice.</summary>
public static class Toast
{
    public static void Show(Panel host, string message)
    {
        var border = new Border
        {
            Background = Ui.Dark,
            CornerRadius = new CornerRadius(AppTheme.Current.Radius * 0.75),
            BoxShadow = AppTheme.IsBraun
                ? BoxShadows.Parse("0 6 18 0 #40000000, inset 0 1 0 0 #26FFFFFF")
                : default,
            Padding = new Thickness(18, 12),
            Margin = new Thickness(0, 0, 26, 58),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = Ui.Text(message, 12, Ui.Phosphor, mono: true),
            Opacity = 0,
        };

        host.Children.Add(border);
        Motion.For(border, 180, t => border.Opacity = t);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Motion.For(border, 220, t => border.Opacity = 1 - t, () => host.Children.Remove(border));
        };
        timer.Start();
    }
}
