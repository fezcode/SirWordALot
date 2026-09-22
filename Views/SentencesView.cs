using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class SentencesView : UserControl, IDisposable
{
    private readonly ShellContext _context;
    private readonly StackPanel _list = Ui.Stack(0);
    private readonly TextBlock _count = Ui.Label("");
    private readonly ComboBox _scope = new()
    {
        ItemsSource = new[] { "All sentences", "Loose only", "Linked only" },
        SelectedIndex = 0,
        Width = 168,
    };
    private readonly ContentControl _panel = new();
    private readonly Control _listContent;

    public TextBox SearchBox { get; } = new() { Watermark = "Search sentences…" };
    public IReadOnlyList<Sentence> Rows { get; private set; } = [];

    public SentencesView(ShellContext context)
    {
        _context = context;

        // The search box is capped so it does not run the full width of a wide
        // window while the two controls beside it stay small.
        SearchBox.MaxWidth = 520;
        SearchBox.HorizontalAlignment = HorizontalAlignment.Left;

        var toolbar = Ui.Columns("*,10,Auto,10,Auto",
            SearchBox,
            new Border(),
            _scope,
            new Border(),
            Ui.Button("+ Sentence", ShowAddForm, "primary"));
        toolbar.Margin = new Thickness(26, 22, 26, 16);

        var header = Ui.Columns("*,210,130", Ui.Label("SENTENCE"), Ui.Label("WORD"), Ui.Label("SOURCE"));
        header.Margin = new Thickness(26, 0, 26, 10);

        _listContent = Ui.Rows("Auto,Auto,Auto,*,40",
            toolbar,
            header,
            Ui.Rule(),
            Ui.Scroll(_list),
            new Border
            {
                BorderBrush = Ui.Line,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(26, 0),
                Child = _count,
            });

        _panel.Content = _listContent;
        Content = _panel;

        SearchBox.TextChanged += (_, _) => Refresh();
        _scope.SelectionChanged += (_, _) => Refresh();
        context.Library.Changed += Refresh;
        Refresh();
    }

    public void Dispose() => _context.Library.Changed -= Refresh;

    public IReadOnlyList<Entry> SuggestLinks(string text) =>
        _context.Library.AllEntries()
            .Where(entry => Cloze.ContainsWord(text, entry.Headword))
            .ToList();

    public long Add(string text, string? source)
    {
        var id = _context.Library.AddSentence(text, null, source);
        var suggestions = SuggestLinks(text);
        if (suggestions.Count == 1) _context.Library.LinkSentence(id, suggestions[0].Id);
        return id;
    }

    public void Refresh()
    {
        var query = SearchBox.Text?.Trim() ?? "";

        Rows = _context.Library.AllSentences()
            .Where(s => _scope.SelectedIndex switch
            {
                1 => s.EntryId is null,
                2 => s.EntryId is not null,
                _ => true,
            })
            .Where(s => query.Length == 0 ||
                        s.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (s.Source ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _list.Children.Clear();
        _count.Text = $"{Rows.Count:00} SENTENCES  ·  {Rows.Count(r => r.EntryId is null):00} LOOSE";

        foreach (var sentence in Rows)
        {
            var word = sentence.EntryId is { } entryId
                ? _context.Library.GetEntry(entryId)?.Headword ?? "—"
                : "—";

            var text = Ui.Text(sentence.Text, 13);
            text.TextWrapping = TextWrapping.Wrap;

            var actions = Ui.Row(0,
                Ui.IconButton(sentence.EntryId is null ? "arrow" : "back",
                    sentence.EntryId is null ? "Link to a word" : "Unlink from its word",
                    () => ToggleLink(sentence)),
                Ui.IconButton("close", "Delete this sentence", () =>
                {
                    _context.Library.DeleteSentence(sentence.Id);
                    _context.Toast("Sentence deleted.");
                }));

            var row = Ui.Columns("*,210,130",
                text,
                Ui.Row(8, Ui.Text(word, 12, word == "—" ? Ui.Muted : Ui.Ink), actions),
                Ui.Label(sentence.Source?.ToUpperInvariant() ?? "—"));
            row.Margin = new Thickness(26, 15);
            row.ContextMenu = RowMenu(sentence);

            _list.Children.Add(row);
            _list.Children.Add(Ui.Rule(Ui.RowRule));
        }

        if (Rows.Count == 0)
            _list.Children.Add(Ui.Pad(
                Ui.Text("No sentences yet. Paste one you met in the wild.", 13, Ui.Muted), 30));
    }

    public ContextMenu RowMenu(Sentence sentence)
    {
        var linked = sentence.EntryId is not null;

        return Ui.Menu(
            Ui.Item("Edit...", () => ShowEditForm(sentence)),
            Ui.Item(linked ? "Unlink from its word" : "Link to a word", () => ToggleLink(sentence)),
            Ui.Item("Open its word",
                () => _context.Navigate("library", sentence.EntryId ?? 0L), enabled: linked),
            Ui.Divider(),
            Ui.Item("Copy sentence", () => Ui.Copy(this, sentence.Text, _context.Toast, "Sentence")),
            Ui.Divider(),
            Ui.Item("Delete", () =>
            {
                _context.Library.DeleteSentence(sentence.Id);
                _context.Toast("Sentence deleted.");
            }, cls: "danger"));
    }

    /// <summary>Editing a stored sentence. The bank could previously only add,
    /// link and delete, so a typo meant deleting and retyping.</summary>
    private void ShowEditForm(Sentence sentence)
    {
        var text = new TextBox
        {
            Text = sentence.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 96,
        };
        var source = new TextBox { Text = sentence.Source ?? "", Watermark = "Where it came from (optional)" };
        var error = Ui.Text("", 12, Ui.Danger);

        ShowPanel(Ui.Stack(16,
            Ui.Heading("SENTENCE BANK", "Edit this sentence.",
                sentence.ClozeStart is not null
                    ? "It has a cloze blank set; changing the text clears it."
                    : "Fix a typo, or trim it down to the part that matters."),
            text,
            source,
            error,
            Ui.Row(12,
                Ui.Button("Cancel", RestoreList, "outline"),
                Ui.Button("Save sentence", () =>
                {
                    var value = text.Text?.Trim() ?? "";
                    if (value.Length == 0) { error.Text = "A sentence cannot be blank."; return; }

                    // A stored cloze span indexes into the old text, so it cannot
                    // survive an edit; drop it rather than blank the wrong word.
                    var changed = value != sentence.Text;

                    _context.Library.UpdateSentence(sentence with
                    {
                        Text = value,
                        Source = string.IsNullOrWhiteSpace(source.Text) ? null : source.Text!.Trim(),
                        ClozeStart = changed ? null : sentence.ClozeStart,
                        ClozeLength = changed ? null : sentence.ClozeLength,
                    });

                    _context.Toast("Sentence saved.");
                    RestoreList();
                }, "primary"))));

        text.Focus();
    }

    private void ToggleLink(Sentence sentence)
    {
        if (sentence.EntryId is not null)
        {
            _context.Library.LinkSentence(sentence.Id, null);
            _context.Toast("Sentence returned to the bank.");
            return;
        }

        var suggestions = SuggestLinks(sentence.Text);
        var options = suggestions.Count > 0 ? suggestions : _context.Library.AllEntries();

        if (options.Count == 0)
        {
            _context.Toast("There are no words to link it to yet.");
            return;
        }

        var picker = new ComboBox
        {
            ItemsSource = options.Select(e => e.Headword).ToList(),
            SelectedIndex = 0,
            Width = 260,
        };

        ShowPanel(Ui.Stack(16,
            Ui.Heading("SENTENCE BANK", "Link this sentence.",
                suggestions.Count > 0
                    ? "These words appear in it."
                    : "No word in the library appears in it; choose one anyway."),
            Ui.Text(sentence.Text, 14),
            picker,
            Ui.Row(12,
                Ui.Button("Cancel", RestoreList, "outline"),
                Ui.Button("Link", () =>
                {
                    if (picker.SelectedIndex >= 0)
                    {
                        _context.Library.LinkSentence(sentence.Id, options[picker.SelectedIndex].Id);
                        _context.Toast("Sentence linked.");
                    }
                    RestoreList();
                }, "primary"))));
    }

    private void ShowAddForm()
    {
        var text = new TextBox
        {
            Watermark = "Paste the sentence you found…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 96,
        };
        var source = new TextBox { Watermark = "Where it came from (optional)" };
        var error = Ui.Text("", 12, Ui.Danger);

        ShowPanel(Ui.Stack(16,
            Ui.Heading("SENTENCE BANK", "A sentence worth keeping.",
                "If a word you already own appears in it, it will be linked automatically."),
            text,
            source,
            error,
            Ui.Row(12,
                Ui.Button("Cancel", RestoreList, "outline"),
                Ui.Button("Add sentence", () =>
                {
                    var value = text.Text?.Trim() ?? "";
                    if (value.Length == 0) { error.Text = "Enter a sentence."; return; }

                    Add(value, string.IsNullOrWhiteSpace(source.Text) ? null : source.Text!.Trim());
                    _context.Toast("Sentence added.");
                    RestoreList();
                }, "primary"))));

        text.Focus();
    }

    private void ShowPanel(Control content) => _panel.Content = Ui.Pad(content, 34);

    private void RestoreList()
    {
        _panel.Content = _listContent;
        Refresh();
    }
}
