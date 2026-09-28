using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;

namespace SirWordALot.Shell;

public static class Ui
{
    // Read through to the current theme, so a control built after a switch picks
    // up the new colours without any view knowing that themes exist.
    public static IBrush Ink => AppTheme.Current.Ink;
    public static IBrush Cream => AppTheme.Current.Cream;
    public static IBrush Muted => AppTheme.Current.Muted;
    public static IBrush Line => AppTheme.Current.Line;
    public static IBrush Phosphor => AppTheme.Current.Phosphor;
    public static IBrush Dark => AppTheme.Current.Dark;
    public static IBrush Sidebar => AppTheme.Current.Sidebar;
    public static IBrush RowRule => AppTheme.Current.RowRule;
    public static IBrush WindowBorder => AppTheme.Current.WindowBorder;
    public static IBrush TitlebarActive => AppTheme.Current.TitlebarActive;
    public static IBrush TitlebarInactive => AppTheme.Current.TitlebarInactive;
    public static IBrush Danger => AppTheme.Current.Danger;
    public static IBrush Accent => AppTheme.Current.Accent;
    public static IBrush ChipFill => AppTheme.Current.ChipFill;
    public static IBrush Highlight => AppTheme.Current.Highlight;
    public static IBrush OnPrimary => AppTheme.Current.OnPrimary;
    public static IBrush OnPrimaryMuted => AppTheme.Current.OnPrimaryMuted;

    // Traffic lights in the theme's palette rather than the system red/amber/green.
    public static IBrush SignalClose => AppTheme.Current.SignalClose;
    public static IBrush SignalMinimize => AppTheme.Current.SignalMinimize;
    public static IBrush SignalMaximize => AppTheme.Current.SignalMaximize;
    public static IBrush SignalIdle => AppTheme.Current.SignalIdle;

    public static FontFamily Mono => AppTheme.Current.Mono;
    public static FontFamily Serif => AppTheme.Current.Serif;

    public static TextBlock Text(string text, double size = 13, IBrush? color = null, bool mono = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = color ?? Ink,
        FontFamily = mono ? Mono : FontFamily.Default,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Label(string text, IBrush? color = null) => new()
    {
        Text = text,
        FontSize = 10.5,
        LetterSpacing = 1.2,
        Foreground = color ?? Muted,
        FontFamily = AppTheme.Current.LabelFont,
        FontWeight = AppTheme.IsBraun ? FontWeight.SemiBold : FontWeight.Normal,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Display(string text, double size = 32) => new()
    {
        Text = text,
        FontFamily = Serif,
        FontWeight = AppTheme.Current.DisplayWeight,
        FontSize = size,
        Foreground = Ink,
        TextWrapping = TextWrapping.Wrap,
    };

    public static StackPanel Stack(double spacing = 12, params Control[] children)
    {
        var panel = new StackPanel { Spacing = spacing };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    public static StackPanel Row(double spacing = 10, params Control[] children)
    {
        var panel = Stack(spacing, children);
        panel.Orientation = Orientation.Horizontal;
        return panel;
    }

    public static Border Rule(IBrush? brush = null) => new()
    {
        Height = 1,
        Background = brush ?? Line,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    public static Border Pad(Control child, double padding = 24) =>
        new() { Padding = new Thickness(padding), Child = child };

    public static Grid Columns(string definitions, params Control[] controls)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(definitions) };
        for (var i = 0; i < controls.Length; i++)
        {
            Grid.SetColumn(controls[i], i);
            grid.Children.Add(controls[i]);
        }
        return grid;
    }

    public static Grid Rows(string definitions, params Control[] controls)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions(definitions) };
        for (var i = 0; i < controls.Length; i++)
        {
            Grid.SetRow(controls[i], i);
            grid.Children.Add(controls[i]);
        }
        return grid;
    }

    public static ScrollViewer Scroll(Control content) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
    };

    public static Button Button(string text, Action action, string? cls = null)
    {
        var button = new Button { Content = text };
        if (cls is not null) button.Classes.Add(cls);
        button.Click += (_, _) => action();
        button.Transitions =
        [
            new BrushTransition
            {
                Property = Avalonia.Controls.Button.BackgroundProperty,
                Duration = TimeSpan.FromMilliseconds(140),
            },
        ];
        return button;
    }

    public static Button IconButton(string icon, string tip, Action action)
    {
        var button = Button("", action);
        button.Content = new Glyph(icon) { Width = 15, Height = 15 };
        ToolTip.SetTip(button, tip);
        return button;
    }

    /// <summary>A relation or tag chip. Clicking the body runs <paramref name="click"/>;
    /// the trailing cross appears only when <paramref name="remove"/> is supplied.</summary>
    public static Control Chip(string text, Action? click = null, Action? remove = null)
    {
        var body = Button("", click ?? (() => { }));
        body.Content = Text(text, 12);
        body.Padding = new Thickness(10, 5);
        body.IsHitTestVisible = click is not null;
        if (AppTheme.IsBraun) body.CornerRadius = new CornerRadius(15);

        var chip = new Border
        {
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            Background = ChipFill,
            Child = remove is null ? body : Row(0, body, IconButton("close", "Remove", remove)),
        };
        if (AppTheme.IsBraun) BraunParts.Key(chip, 15);
        return chip;
    }

    public static Control Heading(string eyebrow, string title, string subtitle) => Stack(10,
        Label(eyebrow),
        Display(title),
        new TextBlock { Text = subtitle, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap });

    /// <summary>The Macrodata Refinement stat tile: index, value, rule, caption.</summary>
    public static Control StatTile(string index, string value, string caption) =>
        AppTheme.IsBraun ? BraunParts.StatTile(index, value, caption) : new Border
    {
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(16, 14),
        Child = Stack(10,
            Columns("*,Auto",
                new TextBlock { Text = index, FontFamily = Mono, FontSize = 20, Foreground = Ink },
                new TextBlock
                {
                    Text = value,
                    FontFamily = Mono,
                    FontSize = 12,
                    Foreground = Muted,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right,
                }),
            Rule(),
            Label(caption)),
    };

    public static Control MasteryBar(int percent)
    {
        var grid = new Grid { Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
        grid.Children.Add(new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(AppTheme.IsBraun ? 1.5 : 0),
            Background = RowRule,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        });
        grid.Children.Add(new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(AppTheme.IsBraun ? 1.5 : 0),
            Background = Accent,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Clamp(percent, 0, 100),
        });
        return grid;
    }

    /// <summary>The institutional eyebrow identifier, e.g. <c>0X 4A 92</c>.</summary>
    public static string HexId(long id) => "0X " + $"{id & 0xFFFF:X4}".Insert(2, " ");

    // ---- context menus -------------------------------------------------

    /// <summary>A right-click menu. Pass <see cref="Separator"/> between groups.</summary>
    public static ContextMenu Menu(params Control[] items)
    {
        var menu = new ContextMenu();
        foreach (var item in items) menu.Items.Add(item);
        return menu;
    }

    public static MenuItem Item(string header, Action click, bool enabled = true, string? cls = null)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (cls is not null) item.Classes.Add(cls);
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>A submenu. Disabled automatically when it would be empty, because a
    /// submenu that opens onto nothing reads as a bug.</summary>
    public static MenuItem Submenu(string header, params Control[] children)
    {
        var item = new MenuItem { Header = header, IsEnabled = children.Length > 0 };
        foreach (var child in children) item.Items.Add(child);
        return item;
    }

    public static Separator Divider() => new();

    /// <summary>Copies text, reporting through <paramref name="toast"/> either way.
    /// The clipboard is not always reachable and silence would be confusing.</summary>
    public static async void Copy(Visual anchor, string text, Action<string> toast, string what)
    {
        try
        {
            if (TopLevel.GetTopLevel(anchor)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
                toast($"{what} copied.");
                return;
            }
            toast("Could not reach the clipboard.");
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            toast("Could not reach the clipboard.");
        }
    }
}

/// <summary>Hands a URL to the operating system's default handler. Used for
/// pronunciation audio, which the OS media player handles better than we would.</summary>
public static class Launch
{
    public static bool Url(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;

        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException
                                       or FileNotFoundException)
        {
            return false;
        }
    }
}

public static class Motion
{
    public static bool Enabled { get; set; } = true;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, AnimationSlot> Slots = new();

    private sealed class AnimationSlot { public Action? Cancel; }

    public static void Cancel(Control control)
    {
        if (Slots.TryGetValue(control, out var slot)) { slot.Cancel?.Invoke(); slot.Cancel = null; }
    }

    public static void For(Control control, double milliseconds, Action<double> frame, Action? done = null)
    {
        Cancel(control);
        var slot = Slots.GetOrCreateValue(control);
        slot.Cancel = Animate(milliseconds, frame, done);
    }

    public static Action Animate(double milliseconds, Action<double> frame, Action? done = null)
    {
        if (!Enabled) { frame(1); done?.Invoke(); return () => { }; }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var clock = Stopwatch.StartNew();
        timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / milliseconds);
            frame(1 - Math.Pow(1 - t, 3));
            if (t >= 1) { timer.Stop(); done?.Invoke(); }
        };
        frame(0);
        timer.Start();
        return timer.Stop;
    }

    public static void Enter(Control control, double distance = 16)
    {
        var transform = new TranslateTransform(0, distance);
        control.RenderTransform = transform;
        For(control, 300, t => { control.Opacity = t; transform.Y = distance * (1 - t); });
    }
}

public sealed class Glyph(string kind) : Control
{
    public IBrush Color { get; set; } = Ui.Ink;

    private static readonly Dictionary<string, string> Paths = new()
    {
        ["folder"] = "M2,6 L9,6 L11,9 L22,9 L22,20 L2,20 Z M2,9 L22,9",
        ["terminal"] = "M2,4 L22,4 L22,20 L2,20 Z M6,9 L10,12 L6,15 M12,16 L18,16",
        ["refinement"] = "M3,3 L9,3 L9,9 L3,9 Z M15,3 L21,3 L21,9 L15,9 Z M3,15 L9,15 L9,21 L3,21 Z M15,15 L21,15 L21,21 L15,21 Z",
        ["notes"] = "M5,2 L16,2 L21,7 L21,22 L5,22 Z M16,2 L16,7 L21,7 M9,11 L17,11 M9,15 L17,15 M9,19 L14,19",
        ["settings"] = "M12,2 L15,5 L19,5 L19,9 L22,12 L19,15 L19,19 L15,19 L12,22 L9,19 L5,19 L5,15 L2,12 L5,9 L5,5 L9,5 Z M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12",
        ["search"] = "M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10 M15,15 L22,22",
        ["arrow"] = "M4,12 L20,12 M14,6 L20,12 L14,18",
        ["back"] = "M20,12 L4,12 M10,6 L4,12 L10,18",
        ["close"] = "M5,5 L19,19 M19,5 L5,19",
        ["maximize"] = "M4,4 L20,4 L20,20 L4,20 Z",
        ["minimize"] = "M5,16 L19,16",
        ["check"] = "M3,12 L9,18 L21,5",
        ["refresh"] = "M20,9 A9,9 0 1 0 20,17 M20,3 L20,9 L14,9",
        ["star"] = "M12,3 L14.8,9.3 L21.6,10 L16.5,14.6 L18,21.3 L12,17.8 L6,21.3 L7.5,14.6 L2.4,10 L9.2,9.3 Z",
        ["quiz"] = "M12,2 A10,10 0 1 1 11.99,2 M9,9 A3,3 0 1 1 12,13 L12,15 M12,18 L12,18.5",
        ["stats"] = "M3,21 L21,21 M6,21 L6,12 M11,21 L11,6 M16,21 L16,15 M21,21 L21,9",
        ["deck"] = "M4,7 L14,4 L20,6 L20,18 L10,21 L4,19 Z M10,21 L10,9 L20,6 M4,7 L10,9",
        ["tag"] = "M3,3 L11,3 L21,13 L13,21 L3,11 Z M7,7 L7.5,7",
        ["import"] = "M12,3 L12,15 M7,10 L12,15 L17,10 M3,20 L21,20",
        ["export"] = "M12,15 L12,3 M7,8 L12,3 L17,8 M3,20 L21,20",
        ["sound"] = "M4,9 L8,9 L13,5 L13,19 L8,15 L4,15 Z M17,8 A6,6 0 0 1 17,16",
        ["word"] = "M4,20 L9,4 L11,4 L16,20 M6.5,14 L13.5,14 M19,9 L19,20",
        // The app mark: a crown for "Sir" over a W for "Word-a-lot". Drawn on the
        // same 24-unit grid as every other glyph so it keeps the line weight.
        // The W is narrower than the crown and its middle vertex reaches back up to
        // cap height, or it reads as a second crown rather than a letter.
        ["crest"] = "M4.5,10.5 L4.5,3.8 L8.6,7.8 L12,2.4 L15.4,7.8 L19.5,3.8 L19.5,10.5 Z " +
                    "M7,12.9 L9.5,21.6 L12,16.2 L14.5,21.6 L17,12.9",
    };

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using (context.PushTransform(Matrix.CreateScale(Bounds.Width / 24, Bounds.Height / 24)))
            context.DrawGeometry(null,
                new Pen(Color, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
                Geometry.Parse(Paths.GetValueOrDefault(kind, Paths["refinement"])));
    }
}
