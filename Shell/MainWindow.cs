using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using SirWordALot.Core;
using SirWordALot.Views;

namespace SirWordALot.Shell;

/// <summary>Everything a view needs from the shell, passed by constructor so views
/// stay independently constructible in the headless checks.</summary>
public sealed record ShellContext(
    Library Library,
    Settings Settings,
    Search Search,
    Stats Stats,
    ILookup Lookup,
    Action<string> Toast,
    Action<string, object?> Navigate);

public sealed record NavItem(string Id, string Title, string Glyph);

/// <summary>Implemented by views that can be navigated to with an argument,
/// such as the Library opening a specific entry.</summary>
public interface IReceivesArgument
{
    void Receive(object argument);
}

public sealed class MainWindow : Window
{
    private static readonly NavItem[] Items =
    [
        new("library", "ALL WORDS", "word"),
        new("due", "DUE TODAY", "quiz"),
        new("starred", "STARRED", "star"),
        new("recent", "RECENT", "refresh"),
        new("archived", "ARCHIVED", "folder"),
        new("sentences", "SENTENCES", "notes"),
        new("lookup", "LOOKUP", "search"),
        new("quiz", "QUIZ", "refinement"),
        new("decks", "DECKS", "deck"),
        new("stats", "STATS", "stats"),
        new("settings", "SETTINGS", "settings"),
    ];

    private readonly Library _library;
    private readonly Settings _settings;
    private readonly ShellContext _context;
    private readonly Dictionary<string, Control> _views = [];
    private readonly Dictionary<string, Button> _navButtons = [];
    private readonly Panel _overlay = new();
    private readonly TextBlock _statusLeft = Ui.Label("SWL / ALL WORDS");
    private readonly TextBlock _statusRight = Ui.Label("");

    private Capture? _capture;
    private HisashiMenubar? _hisashi;

    public IReadOnlyList<NavItem> Nav => Items;

    /// <summary>Where this window puts its controls. Detected from the platform,
    /// overridable so both layouts can be verified on one machine.</summary>
    public TitlebarStyle TitlebarStyle { get; }

    /// <summary>The centred icon and name.</summary>
    public Control TitleBlock { get; private set; } = new Border();

    /// <summary>The minimize/maximize/close cluster, whichever side it is on.</summary>
    public Control WindowControls { get; private set; } = new Border();

    /// <summary>The three lights, or empty on a Windows-style titlebar.</summary>
    public IReadOnlyList<TrafficLight> TrafficLights { get; private set; } = [];

    /// <summary>Which sidebar entries currently carry the selected highlight. Exactly
    /// one should, always; exposed so a check can assert that rather than eyeball it.</summary>
    public IReadOnlyList<string> HighlightedNavIds =>
        _navButtons.Where(p => p.Value.Classes.Contains("selected")).Select(p => p.Key).ToList();
    public string CurrentViewId { get; private set; } = "";
    public Border Titlebar { get; }
    public Border SidebarPanel { get; }
    public Border Footer { get; }
    public ContentControl Host { get; } = new();
    public TextBlock StatusRight => _statusRight;

    public MainWindow(Library library, Settings settings, Search search, Stats stats, ILookup lookup,
        TitlebarStyle? titlebar = null)
    {
        _library = library;
        _settings = settings;
        TitlebarStyle = titlebar ?? Platform.DefaultTitlebarStyle;

        Title = "Sir Word-a-lot";
        SystemDecorations = SystemDecorations.None;
        Background = Ui.Cream;
        Width = 1180;
        Height = 760;
        MinWidth = 1040;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _context = new ShellContext(library, settings, search, stats, lookup, ShowToast,
            (id, argument) => Navigate(id, argument));

        Titlebar = BuildTitlebar();
        SidebarPanel = BuildSidebar();
        Footer = BuildFooter();

        var body = Ui.Columns("170,*", SidebarPanel, Host);
        Grid.SetRow(body, 1);

        var root = Ui.Rows("43,*,42", Titlebar, body, Footer);
        Content = new Border
        {
            BorderBrush = Ui.WindowBorder,
            BorderThickness = new Thickness(1),
            Child = new Panel { Children = { root, _overlay } },
        };

        EnableEdgeResize();

        Activated += (_, _) => SetTitlebarActive(true);
        Deactivated += (_, _) => SetTitlebarActive(false);
        library.Changed += UpdateStatus;

        Opened += (_, _) =>
        {
            _capture = new Capture(this, _settings, CaptureWord);
            // hoswl is a Windows named pipe; Hisashi only exists there.
            if (OperatingSystem.IsWindows()) _hisashi = new HisashiMenubar(this, _context);
            if (_settings.CaptureEnabled && !_capture.TryRegister())
                ShowToast($"The {_settings.CaptureHotkey} hotkey is taken by another program.");

            try
            {
                var backups = new Backups(_settings.BackupFolder);
                backups.Write(_library, DateTime.UtcNow);
                backups.Prune();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A backup failure must never block startup.
            }
        };

        // Position is gone by the time Closed fires, so bounds are tracked
        // while the window is live and written out as it starts to close.
        PositionChanged += (_, _) => TrackNormalBounds();
        Resized += (_, _) => TrackNormalBounds();
        Closing += (_, _) => SaveBounds();

        Closed += (_, _) =>
        {
            library.Changed -= UpdateStatus;
            _capture?.Dispose();
            _hisashi?.Dispose();
            foreach (var view in _views.Values) (view as IDisposable)?.Dispose();
        };

        RestoreBounds();
        KeyDown += OnKeyDown;
        Navigate("library");
    }

    private Border BuildTitlebar()
    {
        // The identity is centred on every platform. Absolute centring in a Panel
        // rather than a Grid column, so it does not shift when the controls on one
        // side are a different width from the space on the other.
        var identity = Ui.Row(11,
            new Glyph("crest") { Width = 17, Height = 17 },
            Ui.Label("SIR WORD-A-LOT", Ui.Ink));
        identity.HorizontalAlignment = HorizontalAlignment.Center;
        identity.VerticalAlignment = VerticalAlignment.Center;
        identity.Background = Brushes.Transparent;
        TitleBlock = identity;

        Control controls;
        if (TitlebarStyle == TitlebarStyle.TrafficLightsLeft)
        {
            var (panel, lights) = TitlebarControls.TrafficLights(
                Close, () => WindowState = WindowState.Minimized, ToggleMaximize);
            panel.HorizontalAlignment = HorizontalAlignment.Left;
            TrafficLights = lights;
            controls = panel;
        }
        else
        {
            var buttons = TitlebarControls.WindowsButtons(
                () => WindowState = WindowState.Minimized, ToggleMaximize, Close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            controls = buttons;
        }

        WindowControls = controls;

        var bar = new Border
        {
            Height = 43,
            Background = Ui.TitlebarActive,
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Panel { Children = { identity, controls } },
        };

        bar.PointerPressed += (_, e) =>
        {
            if (IsTitleButton(e.Source)) return;
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        bar.DoubleTapped += (_, e) =>
        {
            if (!IsTitleButton(e.Source)) ToggleMaximize();
        };

        return bar;

        static bool IsTitleButton(object? source) =>
            source is Visual visual && visual.GetSelfAndVisualAncestors().OfType<Button>().Any();
    }

    /// <summary>How close to an edge counts as grabbing it. Windows itself uses
    /// roughly this, and anything smaller is fiddly to hit.</summary>
    private const double ResizeMargin = 6;

    private static readonly Dictionary<WindowEdge, Cursor> ResizeCursors = new()
    {
        [WindowEdge.North] = new Cursor(StandardCursorType.SizeNorthSouth),
        [WindowEdge.South] = new Cursor(StandardCursorType.SizeNorthSouth),
        [WindowEdge.West] = new Cursor(StandardCursorType.SizeWestEast),
        [WindowEdge.East] = new Cursor(StandardCursorType.SizeWestEast),
        [WindowEdge.NorthWest] = new Cursor(StandardCursorType.TopLeftCorner),
        [WindowEdge.NorthEast] = new Cursor(StandardCursorType.TopRightCorner),
        [WindowEdge.SouthWest] = new Cursor(StandardCursorType.BottomLeftCorner),
        [WindowEdge.SouthEast] = new Cursor(StandardCursorType.BottomRightCorner),
    };

    /// <summary>A frameless window gets no resize border from the platform, so every
    /// edge and corner is hit-tested here instead. Without this the only way to
    /// resize is the corner grip, which is not how a window is expected to behave.</summary>
    private void EnableEdgeResize()
    {
        PointerMoved += (_, e) =>
        {
            if (Content is not Border frame) return;
            var edge = ResizeEdgeAt(e.GetPosition(this));
            frame.Cursor = edge is { } value ? ResizeCursors[value] : Cursor.Default;
        };

        // Tunnelling so an edge beats whatever control happens to sit under it,
        // including the titlebar's drag handler along the top edge.
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (ResizeEdgeAt(e.GetPosition(this)) is not { } edge) return;

            BeginResizeDrag(edge, e);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    /// <summary>The edge a point grabs, or null for the interior. Null throughout
    /// while maximized, where dragging an edge would be meaningless.</summary>
    public WindowEdge? ResizeEdgeAt(Point point)
    {
        if (WindowState != WindowState.Normal) return null;

        var left = point.X <= ResizeMargin;
        var right = point.X >= Bounds.Width - ResizeMargin;
        var top = point.Y <= ResizeMargin;
        var bottom = point.Y >= Bounds.Height - ResizeMargin;

        if (point.X < 0 || point.Y < 0 || point.X > Bounds.Width || point.Y > Bounds.Height) return null;

        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => WindowEdge.NorthWest,
            (_, true, true, _) => WindowEdge.NorthEast,
            (true, _, _, true) => WindowEdge.SouthWest,
            (_, true, _, true) => WindowEdge.SouthEast,
            (true, _, _, _) => WindowEdge.West,
            (_, true, _, _) => WindowEdge.East,
            (_, _, true, _) => WindowEdge.North,
            (_, _, _, true) => WindowEdge.South,
            _ => null,
        };
    }

    /// <summary>Focus drives both the titlebar tint and the traffic lights, which
    /// grey out on an unfocused window exactly as macOS does.</summary>
    public void SetTitlebarActive(bool active)
    {
        Titlebar.Background = active ? Ui.TitlebarActive : Ui.TitlebarInactive;
        foreach (var light in TrafficLights) light.SetLit(active);
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private Border BuildSidebar()
    {
        var panel = Ui.Stack(2);
        panel.Margin = new Thickness(0, 22, 0, 18);

        void Section(string caption)
        {
            var label = Ui.Label(caption);
            label.Margin = new Thickness(18, 4, 18, 6);
            panel.Children.Add(label);
        }

        void Divider()
        {
            var rule = Ui.Rule();
            rule.Margin = new Thickness(18, 14, 18, 8);
            panel.Children.Add(rule);
        }

        void Add(NavItem item)
        {
            var button = Ui.Button("", () => Navigate(item.Id), "nav");
            button.Content = Ui.Row(11,
                new Glyph(item.Glyph) { Width = 14, Height = 14 },
                Ui.Text(TitleCase(item.Title), 12.5));
            _navButtons[item.Id] = button;
            panel.Children.Add(button);
        }

        Section("LIBRARY");
        foreach (var item in Items[..5]) Add(item);

        Divider();
        foreach (var item in Items[5..10]) Add(item);

        Divider();
        foreach (var item in Items[10..]) Add(item);

        // Items[10..] is Settings alone once the console is gone; the split is kept
        // so adding a tool later does not need the sidebar restructured.

        return new Border
        {
            Width = 170,
            Background = Ui.Sidebar,
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = Ui.Scroll(panel),
        };
    }

    private static string TitleCase(string caption) =>
        caption.Length == 0 ? caption : char.ToUpperInvariant(caption[0]) + caption[1..].ToLowerInvariant();

    private Border BuildFooter()
    {
        var grip = new Border
        {
            Width = 19,
            Height = 19,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.BottomRightCorner),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "◢",
                FontSize = 10,
                Foreground = Ui.Muted,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
            },
        };
        grip.PointerPressed += (_, e) =>
        {
            if (WindowState == WindowState.Normal) BeginResizeDrag(WindowEdge.SouthEast, e);
        };

        var right = Ui.Row(14, _statusRight, grip);
        right.HorizontalAlignment = HorizontalAlignment.Right;

        return new Border
        {
            Height = 42,
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 0),
            Child = Ui.Columns("*,Auto", _statusLeft, right),
        };
    }

    public void Navigate(string id, object? argument = null)
    {
        var item = Items.FirstOrDefault(i => i.Id == id) ?? Items[0];

        if (!_views.TryGetValue(item.Id, out var view))
        {
            view = Build(item.Id);
            _views[item.Id] = view;
        }

        if (argument is not null && view is IReceivesArgument receiver) receiver.Receive(argument);

        Host.Content = view;
        CurrentViewId = item.Id;
        _statusLeft.Text = $"SWL / {item.Title}";

        foreach (var (buttonId, button) in _navButtons)
        {
            button.Classes.Remove("selected");
            if (buttonId == item.Id) button.Classes.Add("selected");
        }

        Motion.Enter(view, 8);
        UpdateStatus();
        _hisashi?.Refresh();
    }

    public void FocusSearch()
    {
        Navigate("library");
        (Host.Content as LibraryView)?.FocusSearch();
    }

    public void BeginNewWord()
    {
        Navigate("library");
        (Host.Content as LibraryView)?.BeginNewWord();
    }

    public void ShowCommandPalette() => CommandPalette.Show(_overlay, _context, Items);

    private Control Build(string id) => id switch
    {
        "sentences" => new SentencesView(_context),
        "lookup" => new LookupView(_context),
        "quiz" => new QuizView(_context),
        "decks" => new DecksView(_context),
        "stats" => new StatsView(_context),
        "settings" => new SettingsView(_context),
        _ => new LibraryView(_context, id),
    };

    public void ShowToast(string message) => Shell.Toast.Show(_overlay, message);

    private void UpdateStatus()
    {
        var summary = _context.Stats.Summary(DateTime.UtcNow);
        var connectivity = _settings.LookupEnabled ? "ONLINE" : "LOOKUP OFF";
        _statusRight.Text = $"{summary.TotalWords} WORDS  ·  {summary.DueToday} DUE  ·  {connectivity}";
    }

    private void CaptureWord(string word)
    {
        Activate();
        _library.AddEntry(word, "capture");
        ShowToast($"Captured '{word}'.");
        Navigate("lookup", word);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
        {
            ShowCommandPalette();
            e.Handled = true;
            return;
        }

        // A bare letter must never steal a keystroke from a field the user is
        // typing into, so the guard is the focused element, not the hosted view.
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (FocusManager?.GetFocusedElement() is TextBox or ComboBox) return;

        switch (e.Key)
        {
            case Key.OemQuestion or Key.Divide:
                FocusSearch();
                e.Handled = true;
                break;
            case Key.N:
                BeginNewWord();
                e.Handled = true;
                break;
            case Key.Q:
                Navigate("quiz");
                e.Handled = true;
                break;
        }
    }

    // Stored as "x,y,width,height[,max]": the restored (not maximised) rectangle,
    // with the position in physical pixels and the size in DIPs, as Avalonia uses them.
    private PixelPoint? _normalPosition;
    private Size _normalSize;
    private bool _restoring;

    private void TrackNormalBounds()
    {
        if (_restoring || WindowState != WindowState.Normal) return;
        _normalPosition = Position;
        _normalSize = new Size(Width, Height);
    }

    private void RestoreBounds()
    {
        if (_settings.WindowBounds is not { } stored) return;
        var parts = stored.Split(',');
        if (parts.Length is not (4 or 5)) return;
        var invariant = CultureInfo.InvariantCulture;
        if (!int.TryParse(parts[0], NumberStyles.Integer, invariant, out var x) ||
            !int.TryParse(parts[1], NumberStyles.Integer, invariant, out var y) ||
            !double.TryParse(parts[2], NumberStyles.Float, invariant, out var width) ||
            !double.TryParse(parts[3], NumberStyles.Float, invariant, out var height)) return;

        _restoring = true;
        Width = Math.Max(MinWidth, width);
        Height = Math.Max(MinHeight, height);

        // A monitor that was unplugged since last time would strand the window
        // off-screen; fall back to centring unless the titlebar lands on a screen.
        var position = new PixelPoint(x, y);
        if (IsOnScreen(position))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = position;
            _normalPosition = position;
        }
        _normalSize = new Size(Width, Height);

        if (parts.Length == 5 && parts[4] == "max") WindowState = WindowState.Maximized;
        _restoring = false;
    }

    private bool IsOnScreen(PixelPoint position)
    {
        var screens = Screens?.All;
        if (screens is null || screens.Count == 0) return false;
        var grip = new PixelRect(position.X + 40, position.Y + 8, 200, 24);
        return screens.Any(screen => screen.WorkingArea.Intersects(grip));
    }

    private void SaveBounds()
    {
        TrackNormalBounds();
        if (_normalPosition is not { } position) return;
        var invariant = CultureInfo.InvariantCulture;
        var bounds = string.Join(',',
            position.X.ToString(invariant), position.Y.ToString(invariant),
            _normalSize.Width.ToString(invariant), _normalSize.Height.ToString(invariant));
        if (WindowState == WindowState.Maximized) bounds += ",max";
        _settings.WindowBounds = bounds;
    }
}
