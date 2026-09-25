using Avalonia.Threading;
using SirWordALot.Core;

namespace SirWordALot.Shell;

/// <summary>Publishes the window's commands to Hisashi's menubar over hoswl and
/// runs them when clicked there. There is deliberately no menu named after the
/// app: Hisashi supplies that one itself, with the window controls and Close.</summary>
public sealed class HisashiMenubar : IDisposable
{
    public const string AppId = "com.fezcode.sirwordalot";

    private readonly MainWindow _window;
    private readonly ShellContext _context;
    private readonly HoswlClient _client = new(AppId, Product.Name, Product.Version);

    public HisashiMenubar(MainWindow window, ShellContext context)
    {
        _window = window;
        _context = context;
        _client.Click += id => Dispatcher.UIThread.Post(() => Run(id));
        _context.Settings.Changed += ApplyEnabled;
        ApplyEnabled();
        Refresh();
        _client.Start();
    }

    /// <summary>Re-sends the menus, so the Go menu's tick follows the current view.</summary>
    public void Refresh() => _client.SetMenus(Menus(_window.Nav, _window.CurrentViewId));

    public static IReadOnlyList<HoswlMenu> Menus(IReadOnlyList<NavItem> nav, string currentViewId)
    {
        var go = new List<HoswlItem>();
        for (var i = 0; i < nav.Count; i++)
        {
            // Same grouping as the sidebar: the library filters, the tools, then Settings.
            if (i is 5 or 10) go.Add(HoswlItem.Separator);
            go.Add(new HoswlItem("go." + nav[i].Id, Title(nav[i].Title), Check: nav[i].Id == currentViewId));
        }
        go.Add(HoswlItem.Separator);
        go.Add(new HoswlItem("go.palette", "Command Palette…", "Ctrl+Space"));

        return
        [
            new HoswlMenu("file", "File",
            [
                new HoswlItem("file.new", "New Word", "N"),
                new HoswlItem("file.search", "Search Library", "/"),
                new HoswlItem("file.lookup", "Look Up a Word"),
                new HoswlItem("file.quiz", "Start a Quiz", "Q"),
                HoswlItem.Separator,
                new HoswlItem("file.export", "Import and Export…"),
                new HoswlItem("file.backup", "Back Up Now"),
            ]),
            new HoswlMenu("go", "Go", go),
        ];
    }

    private void Run(string id)
    {
        switch (id)
        {
            case "file.new": _window.BeginNewWord(); break;
            case "file.search": _window.FocusSearch(); break;
            case "file.lookup": _window.Navigate("lookup"); break;
            case "file.quiz": _window.Navigate("quiz"); break;
            case "file.export": _window.Navigate("settings"); break;
            case "file.backup": CommandPalette.BackUpNow(_context); break;
            case "go.palette": _window.ShowCommandPalette(); break;
            default:
                if (id.StartsWith("go.", StringComparison.Ordinal)) _window.Navigate(id[3..]);
                break;
        }
    }

    private void ApplyEnabled() => _client.SetEnabled(_context.Settings.HisashiMenus);

    private static string Title(string caption) =>
        caption.Length == 0 ? caption : char.ToUpperInvariant(caption[0]) + caption[1..].ToLowerInvariant();

    public void Dispose()
    {
        _context.Settings.Changed -= ApplyEnabled;
        _client.Dispose();
    }
}
