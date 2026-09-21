using Avalonia;
using Avalonia.Controls;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class DecksView : UserControl, IDisposable
{
    private readonly ShellContext _context;
    private readonly ContentControl _body = new();

    public IReadOnlyList<Deck> Decks { get; private set; } = [];

    public DecksView(ShellContext context)
    {
        _context = context;
        Content = _body;
        context.Library.Changed += Refresh;
        Refresh();
    }

    public void Dispose() => _context.Library.Changed -= Refresh;

    /// <summary>A deck with a stored filter is evaluated live; otherwise its
    /// membership rows are the answer.</summary>
    public IReadOnlyList<Entry> MembersOf(Deck deck)
    {
        if (deck.FilterJson is not { Length: > 0 } json) return _context.Library.DeckEntries(deck.Id);

        try
        {
            var filter = System.Text.Json.JsonSerializer.Deserialize<SearchFilter>(json);
            return filter is null ? [] : _context.Search.Run(filter, DateTime.UtcNow).Select(h => h.Entry).ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    public void Refresh()
    {
        Decks = _context.Library.Decks();

        var list = Ui.Stack(0);

        foreach (var deck in Decks)
        {
            var members = MembersOf(deck);
            var smart = deck.FilterJson is { Length: > 0 };
            var current = deck;

            var left = Ui.Stack(6,
                Ui.Row(9,
                    new Glyph(smart ? "search" : "deck") { Width = 14, Height = 14 },
                    Ui.Text(deck.Name, 14)),
                Ui.Label($"{members.Count:00} WORDS  ·  {(smart ? "SMART" : "MANUAL")}" +
                         (deck.Description is { Length: > 0 } d ? $"  ·  {d.ToUpperInvariant()}" : "")));

            var actions = Ui.Row(0,
                Ui.IconButton("quiz", "Quiz this deck",
                    () => _context.Navigate("quiz", new ReviewScope(ScopeKind.Deck, DeckId: current.Id))),
                Ui.IconButton("arrow", "Open this deck", () => Open(current.Id)),
                Ui.IconButton("close", "Delete this deck", () => ConfirmDelete(current)));

            var row = Ui.Columns("*,Auto", left, actions);
            row.Margin = new Thickness(30, 16);

            list.Children.Add(row);
            list.Children.Add(Ui.Rule(Ui.RowRule));
        }

        if (Decks.Count == 0)
            list.Children.Add(Ui.Pad(Ui.Text(
                "No decks yet. Filter the library and save the filter, or build one by hand.", 13, Ui.Muted), 30));

        var header = Ui.Columns("*,Auto",
            Ui.Heading("COLLECTIONS", "Decks.", "A deck is a smaller promise than the whole library."),
            Ui.Button("+ Deck", PromptForDeck, "primary"));
        header.Margin = new Thickness(30, 26, 30, 20);

        _body.Content = Ui.Rows("Auto,Auto,*", header, Ui.Rule(), Ui.Scroll(list));
    }

    public void Open(long deckId)
    {
        if (Decks.FirstOrDefault(d => d.Id == deckId) is not { } deck) return;

        var members = MembersOf(deck);
        var smart = deck.FilterJson is { Length: > 0 };
        var list = Ui.Stack(0);

        foreach (var entry in members)
        {
            var current = entry;
            var actions = smart
                ? (Control)Ui.Label("VIA FILTER")
                : Ui.IconButton("close", "Remove from this deck", () =>
                {
                    _context.Library.RemoveFromDeck(deck.Id, current.Id);
                    Open(deck.Id);
                });

            var row = Ui.Columns("*,Auto", Ui.Text(entry.Headword, 13), actions);
            row.Margin = new Thickness(30, 13);

            list.Children.Add(row);
            list.Children.Add(Ui.Rule(Ui.RowRule));
        }

        if (members.Count == 0)
            list.Children.Add(Ui.Pad(Ui.Text("This deck is empty.", 13, Ui.Muted), 30));

        Control adder = new Border();
        if (!smart)
        {
            var candidates = _context.Library.AllEntries()
                .Where(e => members.All(m => m.Id != e.Id)).ToList();

            var picker = new ComboBox
            {
                ItemsSource = candidates.Select(e => e.Headword).ToList(),
                Width = 220,
                PlaceholderText = "Add a word…",
            };
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedIndex < 0 || picker.SelectedIndex >= candidates.Count) return;
                _context.Library.AddToDeck(deck.Id, candidates[picker.SelectedIndex].Id);
                Open(deck.Id);
            };
            adder = picker;
        }

        var header = Ui.Columns("*,Auto",
            Ui.Heading($"DECK / {Ui.HexId(deck.Id)}", deck.Name,
                smart
                    ? "A smart deck. Its membership follows the filter it was saved from."
                    : "A manual deck."),
            Ui.Row(10, adder, Ui.Button("Back", Refresh, "outline")));
        header.Margin = new Thickness(30, 26, 30, 20);

        _body.Content = Ui.Rows("Auto,Auto,*", header, Ui.Rule(), Ui.Scroll(list));
    }

    private void PromptForDeck()
    {
        var name = new TextBox { Watermark = "Deck name" };
        var description = new TextBox { Watermark = "What it is for (optional)" };
        var error = Ui.Text("", 12, Ui.Danger);

        _body.Content = Ui.Pad(Ui.Stack(16,
            Ui.Heading("COLLECTIONS", "A new deck.", "Add words to it from the library or from here."),
            name,
            description,
            error,
            Ui.Row(12,
                Ui.Button("Cancel", Refresh, "outline"),
                Ui.Button("Create deck", () =>
                {
                    var value = name.Text?.Trim() ?? "";
                    if (value.Length == 0) { error.Text = "Name the deck."; return; }
                    if (_context.Library.Decks().Any(d => d.Name == value))
                    { error.Text = "A deck already has that name."; return; }

                    _context.Library.CreateDeck(value,
                        string.IsNullOrWhiteSpace(description.Text) ? null : description.Text!.Trim());
                    _context.Toast("Deck created.");
                }, "primary"))), 34);

        name.Focus();
    }

    private void ConfirmDelete(Deck deck)
    {
        _body.Content = Ui.Pad(Ui.Stack(18,
            Ui.Heading("COLLECTIONS", "Remove this deck?", "The words themselves stay in the library."),
            Ui.Display(deck.Name, 24),
            Ui.Row(12,
                Ui.Button("Keep it", Refresh, "outline"),
                Ui.Button("Delete", () =>
                {
                    _context.Library.DeleteDeck(deck.Id);
                    _context.Toast("Deck deleted.");
                }, "primary"))), 34);
    }
}
