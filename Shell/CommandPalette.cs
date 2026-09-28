using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SirWordALot.Core;

namespace SirWordALot.Shell;

public sealed record PaletteEntry(string Label, string Caption, Action Run);

public static class CommandPalette
{
    public static IReadOnlyList<PaletteEntry> Build(ShellContext context, IReadOnlyList<NavItem> nav)
    {
        var entries = new List<PaletteEntry>();

        foreach (var item in nav)
            entries.Add(new PaletteEntry(TitleCase(item.Title), "View",
                () => context.Navigate(item.Id, null)));

        foreach (var entry in context.Library.AllEntries(includeArchived: true))
            entries.Add(new PaletteEntry(entry.Headword, entry.PartOfSpeech ?? "Word",
                () => context.Navigate("library", entry.Id)));

        foreach (var deck in context.Library.Decks())
            entries.Add(new PaletteEntry(deck.Name, "Deck", () => context.Navigate("decks", deck.Id)));

        foreach (var tag in context.Library.Tags())
            entries.Add(new PaletteEntry(tag.Name, "Tag", () => context.Navigate("library", tag.Name)));

        entries.Add(new PaletteEntry("Start a quiz", "Action", () => context.Navigate("quiz", null)));
        entries.Add(new PaletteEntry("Look up a word", "Action", () => context.Navigate("lookup", null)));
        entries.Add(new PaletteEntry("Export the library", "Action", () => context.Navigate("settings", null)));
        entries.Add(new PaletteEntry("Back up now", "Action", () => BackUpNow(context)));

        return entries;
    }

    public static void BackUpNow(ShellContext context)
    {
        var backups = new Backups(context.Settings.BackupFolder);
        var path = backups.Write(context.Library, DateTime.UtcNow);
        backups.Prune();
        context.Toast($"Snapshot written to {path}.");
    }

    public static IReadOnlyList<PaletteEntry> Filter(IReadOnlyList<PaletteEntry> entries, string query) =>
        entries
            .Select(entry => (Entry: entry, Score: Fuzzy.Score(entry.Label, query)))
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Label.Length)
            .Select(x => x.Entry)
            .Take(40)
            .ToList();

    public static void Show(Panel host, ShellContext context, IReadOnlyList<NavItem> nav)
    {
        var entries = Build(context, nav);
        var visible = new List<PaletteEntry>();
        var results = Ui.Stack(0);
        var query = new TextBox { Watermark = "Go to a word, a view or an action…", FontSize = 15 };

        var card = new Border
        {
            Width = 560,
            Background = Ui.Cream,
            BorderBrush = Ui.WindowBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(AppTheme.Current.Radius * 1.25),
            Padding = new Thickness(0, 0, 0, AppTheme.Current.Radius / 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 120, 0, 0),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 14, Blur = 38, Color = Color.Parse("#45091619") }),
            Child = Ui.Rows("Auto,Auto,*",
                Ui.Pad(query, 16),
                Ui.Rule(),
                new ScrollViewer { Content = results, MaxHeight = 340 }),
        };

        var scrim = new Border { Background = Brush.Parse("#33091619"), Child = card };

        void Close() => host.Children.Remove(scrim);

        void Render()
        {
            results.Children.Clear();
            visible.Clear();
            visible.AddRange(Filter(entries, query.Text ?? ""));

            foreach (var entry in visible)
            {
                var button = Ui.Button("", () => { Close(); entry.Run(); });
                button.Content = Ui.Columns("*,Auto",
                    Ui.Text(entry.Label, 13),
                    Ui.Label(entry.Caption.ToUpperInvariant()));
                button.Padding = new Thickness(18, 11);
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                results.Children.Add(button);
            }

            if (results.Children.Count == 0)
                results.Children.Add(Ui.Pad(Ui.Text("Nothing matches.", 13, Ui.Muted), 20));
        }

        query.TextChanged += (_, _) => Render();
        query.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
            if (e.Key != Key.Enter || visible.Count == 0) return;

            var first = visible[0];
            Close();
            first.Run();
            e.Handled = true;
        };
        scrim.PointerPressed += (_, e) => { if (ReferenceEquals(e.Source, scrim)) Close(); };

        Render();
        host.Children.Add(scrim);
        query.Focus();
    }

    private static string TitleCase(string caption) =>
        caption.Length == 0 ? caption : char.ToUpperInvariant(caption[0]) + caption[1..].ToLowerInvariant();
}
