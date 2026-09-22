using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class LibraryView : UserControl, IDisposable, IReceivesArgument
{
    private static readonly string[] MasteryChoices =
        ["Any mastery", "New", "Learning", "Familiar", "Strong", "Mastered"];

    private readonly ShellContext _context;
    private readonly string _preset;
    private readonly StackPanel _list = Ui.Stack(0);
    private readonly TextBlock _count = Ui.Label("");
    // Stretched rather than content-sized: two ragged columns of differently wide
    // dropdowns is what made the filter row look accidental.
    private readonly ComboBox _sort = FilterBox(new[] { "Recent", "Alphabetic", "Due", "Mastery" });
    private readonly ComboBox _partOfSpeech = FilterBox(null);
    private readonly ComboBox _tag = FilterBox(null);
    private readonly ComboBox _mastery = FilterBox(MasteryChoices);

    private static ComboBox FilterBox(IEnumerable<string>? items) => new()
    {
        ItemsSource = items,
        SelectedIndex = items is null ? -1 : 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private long? _selected;
    private bool _loading;

    public TextBox SearchBox { get; } = new() { Watermark = "Search words and sentences…" };
    public ContentControl Detail { get; } = new();
    public IReadOnlyList<SearchHit> Hits { get; private set; } = [];

    public SearchFilter Filter => new(
        Text: string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text,
        PartOfSpeech: _partOfSpeech.SelectedIndex > 0 ? _partOfSpeech.SelectedItem as string : null,
        Tag: _tag.SelectedIndex > 0 ? _tag.SelectedItem as string : null,
        Mastery: _mastery.SelectedIndex > 0 ? (MasteryBucket)(_mastery.SelectedIndex - 1) : null,
        Due: _preset == "due" ? DueState.DueNow : DueState.Any,
        IncludeArchived: _preset == "archived",
        StarredOnly: _preset == "starred",
        Sort: _preset switch
        {
            "due" => SortOrder.Due,
            "recent" => SortOrder.Recent,
            _ => (SortOrder)Math.Max(0, _sort.SelectedIndex),
        });

    public LibraryView(ShellContext context, string preset)
    {
        _context = context;
        _preset = preset;

        // The list column is narrow, so the controls stack rather than competing for
        // one row; a fixed-width toolbar here crushes the search box to nothing.
        var saveAsDeck = Ui.Button("Save this filter as a deck", PromptForDeckName, "outline");
        saveAsDeck.HorizontalAlignment = HorizontalAlignment.Stretch;

        var toolbar = Ui.Stack(8,
            Ui.Columns("*,8,Auto", SearchBox, new Border(), Ui.Button("+ Word", NewWord, "primary")),
            Ui.Columns("*,8,*", _sort, new Border(), _partOfSpeech),
            Ui.Columns("*,8,*", _tag, new Border(), _mastery),
            saveAsDeck);
        toolbar.Margin = new Thickness(16, 16, 16, 14);

        var listColumn = Ui.Rows("Auto,*,40",
            toolbar,
            Ui.Scroll(_list),
            new Border
            {
                BorderBrush = Ui.Line,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 0),
                Child = _count,
            });

        var listBorder = new Border
        {
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = listColumn,
        };

        Detail.Content = Placeholder();
        // 390 rather than 360: at the narrower width "Any part of speech" clipped
        // against real font metrics, which the headless render did not show.
        Content = Ui.Columns("390,*", listBorder, Detail);

        SearchBox.TextChanged += (_, _) => Refresh();
        _sort.SelectionChanged += (_, _) => Refresh();
        _partOfSpeech.SelectionChanged += (_, _) => { if (!_loading) Refresh(); };
        _tag.SelectionChanged += (_, _) => { if (!_loading) Refresh(); };
        _mastery.SelectionChanged += (_, _) => Refresh();

        context.Library.Changed += Refresh;
        Refresh();
    }

    public void Receive(object argument)
    {
        switch (argument)
        {
            case long entryId:
                Select(entryId);
                break;
            case string tag when _tag.ItemsSource is IEnumerable<string> tags:
                var index = tags.ToList().IndexOf(tag);
                if (index >= 0) _tag.SelectedIndex = index;
                break;
        }
    }

    public void Dispose() => _context.Library.Changed -= Refresh;

    /// <summary>Used by the shell's <c>/</c> shortcut.</summary>
    public void FocusSearch() => SearchBox.Focus();

    /// <summary>Used by the shell's <c>N</c> shortcut.</summary>
    public void BeginNewWord() => NewWord();

    public long SaveFilterAsDeck(string name) =>
        _context.Library.CreateDeck(name, $"Saved from the {_preset} view.",
            System.Text.Json.JsonSerializer.Serialize(Filter));

    public void Refresh()
    {
        ReloadChoices();

        Hits = _context.Search.Run(Filter, DateTime.UtcNow);
        _list.Children.Clear();
        _count.Text = $"{Hits.Count:00} WORDS  ·  {_preset.ToUpperInvariant()}";

        foreach (var hit in Hits)
        {
            var left = Ui.Stack(5,
                Ui.Row(8,
                    hit.Entry.Starred
                        ? new Glyph("star") { Width = 11, Height = 11 }
                        : new Border { Width = 11 },
                    Ui.Text(hit.Entry.Headword, 13)),
                Ui.Row(10,
                    Ui.Label(hit.Entry.PartOfSpeech?.ToUpperInvariant() ?? "—"),
                    Ui.MasteryBar(hit.MasteryPercent)));

            var row = Ui.Columns("*,Auto", left,
                Ui.Label(hit.DueUtc <= DateTime.UtcNow
                    ? "DUE"
                    : hit.DueUtc.ToString("dd MMM").ToUpperInvariant()));

            var button = Ui.Button("", () => Select(hit.Entry.Id));
            button.Content = row;
            button.Padding = new Thickness(16, 13);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            if (hit.Entry.Id == _selected) button.Classes.Add("selected");

            _list.Children.Add(button);
            _list.Children.Add(Ui.Rule(Ui.RowRule));
        }

        if (Hits.Count == 0)
            _list.Children.Add(Ui.Pad(Ui.Text("No words match this filter.", 13, Ui.Muted), 26));

        if (_selected is { } id && Hits.All(h => h.Entry.Id != id) && _context.Library.GetEntry(id) is null)
        {
            _selected = null;
            Detail.Content = Placeholder();
        }
    }

    private void ReloadChoices()
    {
        _loading = true;

        var parts = new List<string> { "Any part of speech" };
        parts.AddRange(_context.Library.AllEntries(includeArchived: true)
            .Select(e => e.PartOfSpeech)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order());

        if (_partOfSpeech.ItemsSource is not IEnumerable<string> existing || !existing.SequenceEqual(parts))
        {
            var selected = _partOfSpeech.SelectedItem as string;
            _partOfSpeech.ItemsSource = parts;
            _partOfSpeech.SelectedIndex = Math.Max(0, parts.IndexOf(selected ?? ""));
        }

        var tags = new List<string> { "Any tag" };
        tags.AddRange(_context.Library.Tags().Select(t => t.Name));

        if (_tag.ItemsSource is not IEnumerable<string> tagged || !tagged.SequenceEqual(tags))
        {
            var selected = _tag.SelectedItem as string;
            _tag.ItemsSource = tags;
            _tag.SelectedIndex = Math.Max(0, tags.IndexOf(selected ?? ""));
        }

        _loading = false;
    }

    public void Select(long entryId)
    {
        if (_context.Library.GetEntry(entryId) is null) return;

        _selected = entryId;
        var detail = new EntryView(_context, entryId, Refresh);
        Detail.Content = detail;
        Motion.Enter(detail, 8);
        Refresh();
    }

    private void NewWord()
    {
        var name = new TextBox { Watermark = "A word or phrase" };
        var error = Ui.Text("", 12, Ui.Danger);

        var form = Ui.Stack(16,
            Ui.Heading("LIBRARY MANAGEMENT", "A new word.", "It will be due for review immediately."),
            name,
            error,
            Ui.Row(12,
                Ui.Button("Cancel", () => Detail.Content = Placeholder(), "outline"),
                Ui.Button("Add word", () =>
                {
                    var value = name.Text?.Trim() ?? "";
                    if (Library.Normalize(value).Length == 0) { error.Text = "Enter a word."; return; }

                    if (_context.Library.FindEntry(value) is { } existing)
                    {
                        _context.Toast("That word is already in the library.");
                        Select(existing.Id);
                        return;
                    }

                    var id = _context.Library.AddEntry(value);
                    _context.Toast("Word added.");
                    Select(id);
                }, "primary")));

        Detail.Content = Ui.Pad(form, 30);
        name.Focus();
    }

    private void PromptForDeckName()
    {
        var name = new TextBox { Watermark = "Deck name" };
        var error = Ui.Text("", 12, Ui.Danger);

        var form = Ui.Stack(16,
            Ui.Heading("SMART DECK", "Save this filter.",
                "The deck re-evaluates every time you open it."),
            name,
            error,
            Ui.Row(12,
                Ui.Button("Cancel", () => Detail.Content = Placeholder(), "outline"),
                Ui.Button("Save deck", () =>
                {
                    var value = name.Text?.Trim() ?? "";
                    if (value.Length == 0) { error.Text = "Name the deck."; return; }
                    if (_context.Library.Decks().Any(d => d.Name == value))
                    { error.Text = "A deck already has that name."; return; }

                    SaveFilterAsDeck(value);
                    _context.Toast("Smart deck saved.");
                    Detail.Content = Placeholder();
                }, "primary")));

        Detail.Content = Ui.Pad(form, 30);
        name.Focus();
    }

    private static Control Placeholder() => Ui.Pad(Ui.Stack(12,
        Ui.Label("NO SELECTION"),
        Ui.Display("Choose a word.", 26),
        Ui.Text("Or add one. Every word you keep is a word you will meet again.", 12, Ui.Muted)), 34);
}
