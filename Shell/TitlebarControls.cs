using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace SirWordALot.Shell;

/// <summary>Where a platform expects its window controls to sit.</summary>
public enum TitlebarStyle
{
    /// <summary>Minimize, maximize and close as glyphs on the right.</summary>
    WindowsRight,

    /// <summary>Close, minimize and zoom as round lights on the left.</summary>
    TrafficLightsLeft,
}

public static class Platform
{
    /// <summary>What this machine expects. macOS and most Linux desktops put the
    /// controls on the left; Windows puts them on the right.</summary>
    public static TitlebarStyle DefaultTitlebarStyle =>
        OperatingSystem.IsWindows() ? TitlebarStyle.WindowsRight : TitlebarStyle.TrafficLightsLeft;
}

/// <summary>A macOS-style round window control, in this app's palette rather than
/// the system red, amber and green.
///
/// Colour follows window focus, the way macOS does it: coloured while the window
/// is active, grey while it is not.</summary>
public sealed class TrafficLight : Button
{
    private readonly Ellipse _dot;
    private readonly IBrush _colour;

    public TrafficLight(string kind, IBrush colour, string tip, Action action)
    {
        _colour = colour;

        _dot = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = colour,
            Stroke = new SolidColorBrush(Color.Parse("#20000000")),
            StrokeThickness = 1,
        };

        Kind = kind;
        Content = _dot;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(4, 0);
        MinHeight = 0;
        Cursor = new Cursor(StandardCursorType.Arrow);
        VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(this, tip);
        Click += (_, _) => action();
    }

    public string Kind { get; }

    /// <summary>Lit means the window has focus. Exposed so a check can assert the
    /// behaviour without synthesising activation.</summary>
    public bool IsLit { get; private set; } = true;

    public void SetLit(bool lit)
    {
        IsLit = lit;
        _dot.Fill = lit ? _colour : Ui.SignalIdle;
    }
}

public static class TitlebarControls
{
    /// <summary>The three lights, in macOS order: close, minimize, zoom.</summary>
    public static (StackPanel Panel, IReadOnlyList<TrafficLight> Lights) TrafficLights(
        Action close, Action minimize, Action maximize)
    {
        var lights = new List<TrafficLight>
        {
            new("close", Ui.SignalClose, "Close", close),
            new("minimize", Ui.SignalMinimize, "Minimize", minimize),
            new("maximize", Ui.SignalMaximize, "Zoom", maximize),
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
        };
        foreach (var light in lights) panel.Children.Add(light);

        return (panel, lights);
    }

    /// <summary>Minimize, maximize and close as line glyphs, the Windows way.</summary>
    public static StackPanel WindowsButtons(Action minimize, Action maximize, Action close)
    {
        var row = Ui.Row(0,
            Ui.IconButton("minimize", "Minimize", minimize),
            Ui.IconButton("maximize", "Maximize / restore", maximize),
            Ui.IconButton("close", "Close", close));
        row.VerticalAlignment = VerticalAlignment.Center;
        return row;
    }
}
