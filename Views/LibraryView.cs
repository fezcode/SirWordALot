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

    private readonly HashSet<long> _checked = [];
    private readonly Border _selectionBar = new()
    {
        Background = Avalonia.Media.Brush.Parse("#E4E8D8"),
        BorderBrush = Ui.Line,
        BorderThickness = new Thickness(0, 1, 0, 1),
        Padding = new Thickness(16, 10),
        IsVisible = false,
    };

    /// <summary>True while the list shows checkboxes and a bulk action bar.</summary>
    public bool SelectMode { get; private set; }

    /// <summary>The words currently ticked. Always a subset of what is on screen:
    /// anything filtered away or deleted is dropped on the next refresh.</summary>
    public IReadOnlyCollection<long> Checked => _checked;

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
            Ui.Columns("*,8,Auto", saveAsDeck, new Border(), Ui.Button("Select", ToggleSelectMode, "outline")));
        toolbar.Margin = new Thickness(16, 16, 16, 14);

        var listColumn = Ui.Rows("Auto,Auto,*,40",
            toolbar,
            _selectionBar,
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

        // A tick only means anything while the word is on screen; filtering it away
        // or deleting it must not leave it silently selected for a bulk action.
        _checked.IntersectWith(Hits.Select(h => h.Entry.Id));

        _list.Children.Clear();
        _count.Text = SelectMode && _checked.Count > 0
            ? $"{_checked.Count:00} OF {Hits.Count:00} SELECTED"
            : $"{Hits.Count:00} WORDS  ·  {_preset.ToUpperInvariant()}";
        RenderSelectionBar();

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

            var due = Ui.Label(hit.DueUtc <= DateTime.UtcNow
                ? "DUE"
                : hit.DueUtc.ToString("dd MMM").ToUpperInvariant());

            var entry = hit.Entry;
            Control row;

            if (SelectMode)
            {
                // The checkbox is display only; the row button owns the click, so
                // hitting anywhere on the row toggles rather than only the 16px box.
                var box = new CheckBox
                {
                    IsChecked = _checked.Contains(entry.Id),
                    IsHitTestVisible = false,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                row = Ui.Columns("Auto,*,Auto", box, left, due);
            }
            else
            {
                row = Ui.Columns("*,Auto", left, due);
            }

            var button = Ui.Button("", () =>
            {
                if (SelectMode) ToggleChecked(entry.Id); else Select(entry.Id);
            });
            button.Content = row;
            button.Padding = new Thickness(16, 13);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.ContextMenu = RowMenu(entry);
            if (SelectMode ? _checked.Contains(entry.Id) : entry.Id == _selected) button.Classes.Add("selected");

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

    // ---- select mode ---------------------------------------------------

    public void ToggleSelectMode()
    {
        SelectMode = !SelectMode;
        _checked.Clear();
        Refresh();
    }

    public void ToggleChecked(long entryId)
    {
        if (!_checked.Remove(entryId)) _checked.Add(entryId);
        Refresh();
    }

    public void SelectAll()
    {
        foreach (var hit in Hits) _checked.Add(hit.Entry.Id);
        Refresh();
    }

    public void SelectNone()
    {
        _checked.Clear();
        Refresh();
    }

    private void RenderSelectionBar()
    {
        _selectionBar.IsVisible = SelectMode;
        if (!SelectMode) return;

        var count = _checked.Count;
        var all = Hits.Count > 0 && Hits.All(h => _checked.Contains(h.Entry.Id));

        // Five buttons and a label do not fit across a 390px column, so the label
        // sits above them rather than being squeezed to "03 SEL".
        var actions = Ui.Row(6,
            Ui.Button(all ? "None" : "All", () => { if (all) SelectNone(); else SelectAll(); }, "outline"),
            Ui.Button("Star", () => BulkStar(true), "outline"),
            Ui.Button("Archive", () => BulkArchive(true), "outline"),
            Ui.Button("Delete", ConfirmBulkDelete, "outline"),
            Ui.Button("Done", ToggleSelectMode, "outline"));

        foreach (var button in actions.Children.OfType<Button>())
        {
            button.Padding = new Thickness(10, 0);
            button.MinHeight = 32;
        }
        foreach (var button in actions.Children.OfType<Button>().Skip(1).Take(3))
            button.IsEnabled = count > 0;

        _selectionBar.Child = Ui.Stack(9,
            Ui.Label(count == 0
                ? "SELECT MODE  ·  TAP ROWS TO CHOOSE"
                : $"{count:00} OF {Hits.Count:00} SELECTED"),
            actions);
    }

    private long[] Chosen() => _checked.ToArray();

    private void BulkStar(bool starred)
    {
        var ids = Chosen();
        foreach (var id in ids) _context.Library.SetStarred(id, starred);
        _context.Toast($"{ids.Length} word(s) starred.");
    }

    private void BulkArchive(bool archived)
    {
        var ids = Chosen();
        foreach (var id in ids) _context.Library.SetArchived(id, archived);
        _checked.Clear();
        _context.Toast($"{ids.Length} word(s) archived.");
    }

    /// <summary>Bulk delete asks first and names the count. Deleting a list of words
    /// takes their sentences, relations and review history with them.</summary>
    private void ConfirmBulkDelete()
    {
        var ids = Chosen();
        if (ids.Length == 0) return;

        var names = Hits.Where(h => _checked.Contains(h.Entry.Id)).Select(h => h.Entry.Headword).ToList();
        var preview = string.Join(", ", names.Take(8)) + (names.Count > 8 ? $", and {names.Count - 8} more" : "");

        Detail.Content = Ui.Pad(Ui.Stack(18,
            Ui.Heading("LIBRARY MANAGEMENT", $"Remove {ids.Length} words?",
                "Their sentences, relations and review history go with them. This cannot be undone."),
            Ui.Text(preview, 13, Ui.Muted),
            Ui.Row(12,
                Ui.Button("Keep them", () => Detail.Content = Placeholder(), "outline"),
                Ui.Button($"Delete {ids.Length}", () =>
                {
                    foreach (var id in ids) _context.Library.DeleteEntry(id);
                    _checked.Clear();
                    _context.Toast($"{ids.Length} word(s) deleted.");
                    Detail.Content = Placeholder();
                }, "primary"))), 30);
    }

    /// <summary>Right-click CRUD on a word, so the common edits do not require
    /// opening the detail pane first.</summary>
    public ContextMenu RowMenu(Entry entry)
    {
        var decks = _context.Library.Decks();

        var deckItems = decks
            .Where(d => d.FilterJson is not { Length: > 0 })
            .Select(deck =>
            {
                var already = _context.Library.DeckEntries(deck.Id).Any(e => e.Id == entry.Id);
                return (Control)Ui.Item(deck.Name, () =>
                {
                    _context.Library.AddToDeck(deck.Id, entry.Id);
                    _context.Toast($"Added to {deck.Name}.");
                }, enabled: !already);
            })
            .ToArray();

        return Ui.Menu(
            Ui.Item("Open", () => Select(entry.Id)),
            Ui.Item("Quiz this word",
                () => _context.Navigate("quiz", new ReviewScope(ScopeKind.Single, EntryId: entry.Id))),
            Ui.Divider(),
            Ui.Item(entry.Starred ? "Unstar" : "Star",
                () => _context.Library.SetStarred(entry.Id, !entry.Starred)),
            Ui.Item(entry.Archived ? "Unarchive" : "Archive", () =>
            {
                _context.Library.SetArchived(entry.Id, !entry.Archived);
                _context.Toast(entry.Archived ? "Word restored." : "Word archived.");
            }),
            Ui.Submenu("Add to deck", deckItems),
            Ui.Divider(),
            Ui.Item("Look this word up", () => _context.Navigate("lookup", entry.Headword)),
            Ui.Item("Copy headword", () => Ui.Copy(this, entry.Headword, _context.Toast, "Headword")),
            Ui.Divider(),
            Ui.Item("Delete...", () => ConfirmDelete(entry), cls: "danger"));
    }

    /// <summary>Deleting from the list still asks, using the same inline confirm
    /// panel as the detail pane rather than a modal.</summary>
    private void ConfirmDelete(Entry entry)
    {
        _selected = entry.Id;

        Detail.Content = Ui.Pad(Ui.Stack(18,
            Ui.Heading("LIBRARY MANAGEMENT", "Remove this word?",
                "Its sentences, relations and review history go with it. This cannot be undone."),
            Ui.Display(entry.Headword, 24),
            Ui.Row(12,
                Ui.Button("Keep it", () => Select(entry.Id), "outline"),
                Ui.Button("Delete", () =>
                {
                    _context.Library.DeleteEntry(entry.Id);
                    _context.Toast("Word deleted.");
                    _selected = null;
                    Detail.Content = Placeholder();
                }, "primary"))), 30);
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
