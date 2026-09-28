using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;

namespace SirWordALot.Shell;

/// <summary>Everything that differs between two styles of the app: colour, type and
/// the few shapes that code draws rather than the stylesheet. <see cref="Ui"/> reads
/// from whichever of these is current, so views never ask which theme is on.</summary>
public sealed class ThemeSpec
{
    public required string Name { get; init; }

    public required IBrush Ink, Cream, Muted, Line, Phosphor, Dark, Sidebar, RowRule, WindowBorder,
        TitlebarActive, TitlebarInactive, Danger, Accent;

    public required IBrush SignalClose, SignalMinimize, SignalMaximize, SignalIdle;

    /// <summary>A chip's fill, the bulk-selection bar, and the two text colours used
    /// on top of a primary button.</summary>
    public required IBrush ChipFill, Highlight, OnPrimary, OnPrimaryMuted;

    /// <summary>Heatmap tints from an empty day to the busiest.</summary>
    public required IBrush[] HeatLevels;

    public required FontFamily Mono, Serif, LabelFont;
    public required FontWeight DisplayWeight;

    /// <summary>Rounding for cards, chips and overlays drawn in code. Zero keeps the
    /// institutional square corners.</summary>
    public required double Radius;

    /// <summary>The extra stylesheet layered over Shell/Controls.axaml, or null.</summary>
    public string? Stylesheet { get; init; }
}

public static class AppTheme
{
    // The first name that exists wins, so Windows keeps Cascadia and Georgia
    // while macOS falls back to Menlo and Linux to the DejaVu faces.
    private static readonly FontFamily MonoFaces =
        new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, Liberation Mono, monospace");

    /// <summary>The petrol institute lifted from hammeros. Its values are asserted by
    /// tools/Checks and measured in the rendered pixels by tools/Measure.</summary>
    public static readonly ThemeSpec Classic = new()
    {
        Name = "Classic",
        Ink = Brush.Parse("#173B40"),
        Cream = Brush.Parse("#ECEDE0"),
        Muted = Brush.Parse("#688178"),
        Line = Brush.Parse("#BCC8BB"),
        Phosphor = Brush.Parse("#B3E5DA"),
        Dark = Brush.Parse("#102C32"),
        Sidebar = Brush.Parse("#DFE4D6"),
        RowRule = Brush.Parse("#D8DECF"),
        WindowBorder = Brush.Parse("#9CAEA0"),
        TitlebarActive = Brush.Parse("#E9EBDD"),
        TitlebarInactive = Brush.Parse("#CCD6C9"),
        Danger = Brush.Parse("#99513D"),
        Accent = Brush.Parse("#4E8A7B"),

        // Terracotta, ochre and sage keep the same earthy register as the rest,
        // while staying distinguishable from one another at 12px.
        SignalClose = Brush.Parse("#99513D"),
        SignalMinimize = Brush.Parse("#A8894E"),
        SignalMaximize = Brush.Parse("#4E8A7B"),
        SignalIdle = Brush.Parse("#B7C1B6"),

        ChipFill = Brush.Parse("#F3F4E9"),
        Highlight = Brush.Parse("#E4E8D8"),
        OnPrimary = Brush.Parse("#F0F0DF"),
        OnPrimaryMuted = Brush.Parse("#BFD8C9"),
        HeatLevels =
        [
            Brush.Parse("#E3E6D8"), Brush.Parse("#BFD7CB"), Brush.Parse("#93BFAE"),
            Brush.Parse("#63A28D"), Brush.Parse("#3C7D69"),
        ],

        Mono = MonoFaces,
        Serif = new("Georgia, Times New Roman, DejaVu Serif, Liberation Serif, serif"),
        LabelFont = MonoFaces,
        DisplayWeight = FontWeight.Normal,
        Radius = 0,
    };

    /// <summary>A Braun appliance from the Rams years: warm off-white plastic, brushed
    /// aluminium, a charcoal readout and one orange key. The traffic lights take the
    /// orange, yellow and green of the ET 66 calculator's function keys.</summary>
    public static readonly ThemeSpec Braun = new()
    {
        Name = "Braun",
        Ink = Brush.Parse("#262624"),
        Cream = Brush.Parse("#ECE9E1"),
        Muted = Brush.Parse("#7A776F"),
        Line = Brush.Parse("#CBC7BD"),
        Phosphor = Brush.Parse("#F5B04A"),
        Dark = Brush.Parse("#1F1F1D"),
        Sidebar = Vertical("#DCDAD4", "#CDCBC4"),
        RowRule = Brush.Parse("#DEDAD1"),
        WindowBorder = Brush.Parse("#9E9A91"),
        TitlebarActive = Vertical("#F7F5F0", "#E2DFD8"),
        TitlebarInactive = Vertical("#EAE7E1", "#DDDAD3"),
        Danger = Brush.Parse("#C4452A"),
        Accent = Brush.Parse("#E8661F"),

        SignalClose = Brush.Parse("#E8661F"),
        SignalMinimize = Brush.Parse("#EDB92E"),
        SignalMaximize = Brush.Parse("#6C9A4B"),
        SignalIdle = Brush.Parse("#CFCBC2"),

        ChipFill = Vertical("#FBFAF7", "#E9E6DF"),
        Highlight = Brush.Parse("#F4E3D3"),
        OnPrimary = Brush.Parse("#FFFFFF"),
        OnPrimaryMuted = Brush.Parse("#FCDCC6"),
        HeatLevels =
        [
            Brush.Parse("#DCD8CE"), Brush.Parse("#F4CFAF"), Brush.Parse("#F1A774"),
            Brush.Parse("#EB8340"), Brush.Parse("#D95D17"),
        ],

        Mono = MonoFaces,
        // Akzidenz is not on anyone's machine; Inter ships with the app and sits
        // closest to it. Light at display sizes, the way Braun set its model names.
        Serif = new("Inter, Helvetica Neue, Arial, sans-serif"),
        LabelFont = new("Inter, Helvetica Neue, Arial, sans-serif"),
        DisplayWeight = FontWeight.Light,
        Radius = 12,
        Stylesheet = "Shell/Braun.axaml",
    };

    public static readonly IReadOnlyList<ThemeSpec> All = [Classic, Braun];

    public static IReadOnlyList<string> Names { get; } = All.Select(t => t.Name).ToList();

    public static ThemeSpec Current { get; private set; } = Classic;

    public static bool IsBraun => Current == Braun;

    /// <summary>Raised by <see cref="Switch"/>. Controls capture brushes when they are
    /// built, so whoever owns the window rebuilds it; nothing subscribes headlessly.</summary>
    public static event Action? Switched;

    private static StyleInclude? _layer;

    public static ThemeSpec Find(string? name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Classic;

    /// <summary>Makes <paramref name="name"/> current and swaps its stylesheet in over
    /// the shared one. Unknown names fall back to Classic rather than failing.</summary>
    public static void Apply(string? name)
    {
        Current = Find(name);

        if (Application.Current is not { } app) return;
        if (_layer is not null) app.Styles.Remove(_layer);
        _layer = null;

        if (Current.Stylesheet is not { } sheet) return;

        // Shell/ is compiled into the app and into each tool, and every one of them
        // carries the stylesheet under its own assembly name.
        var assembly = typeof(AppTheme).Assembly.GetName().Name;
        var baseUri = new Uri($"avares://{assembly}/");
        _layer = new StyleInclude(baseUri) { Source = new Uri(baseUri, sheet) };
        app.Styles.Add(_layer);
    }

    /// <summary>Applies a theme chosen at runtime and asks for the window to be
    /// rebuilt. Returns false when nobody is listening, i.e. it applies on restart.</summary>
    public static bool Switch(string name)
    {
        Apply(name);
        if (Switched is null) return false;
        Switched();
        return true;
    }

    private static LinearGradientBrush Vertical(string top, string bottom) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse(top), 0), new GradientStop(Color.Parse(bottom), 1) },
    };
}
