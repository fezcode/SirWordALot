using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class EntryView : UserControl
{
    private readonly ShellContext _context;
    private readonly Action _refreshList;
    private readonly ContentControl _body = new();
    private readonly TextBox _headword = new();
    private readonly TextBox _pronunciation = new() { Watermark = "/ˈɛmɪnəns/" };
    private readonly TextBox _partOfSpeech = new() { Watermark = "noun" };
    private readonly TextBox _definition = new()
    { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 74 };
    private readonly TextBox _notes = new()
    { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
    private readonly TextBox _translation = new() { Watermark = "saygınlık, yücelik" };

    public Entry Entry { get; private set; }

    public EntryView(ShellContext context, long entryId, Action refreshList)
    {
        _context = context;
        _refreshList = refreshList;
        Entry = context.Library.GetEntry(entryId)
                ?? throw new InvalidOperationException($"No entry {entryId}.");

        Content = _body;
        Render();
    }

    private void Render()
    {
        if (_context.Library.GetEntry(Entry.Id) is not { } current) return;
        Entry = current;

        var scheduling = _context.Library.GetScheduling(Entry.Id);
        var reviews = _context.Library.ReviewsFor(Entry.Id);
        var percent = Mastery.Percent(scheduling.IntervalDays, reviews.Count(r => r.Correct), reviews.Count);

        _headword.Text = Entry.Headword;
        _pronunciation.Text = Entry.Pronunciation ?? "";
        _partOfSpeech.Text = Entry.PartOfSpeech ?? "";
        _definition.Text = Entry.Definition ?? "";
        _notes.Text = Entry.Notes ?? "";
        _translation.Text = Entry.Translation ?? "";

        var due = scheduling.DueUtc <= DateTime.UtcNow
            ? "DUE NOW"
            : $"DUE IN {(scheduling.DueUtc.Date - DateTime.UtcNow.Date).Days} D";

        var stack = Ui.Stack(18,
            Ui.Columns("*,Auto",
                Ui.Label($"ENTRY / {Ui.HexId(Entry.Id)}"),
                Ui.Label($"{due}  ·  MASTERY {percent}%")),
            Ui.Display(Entry.Headword),
            Ui.Row(12,
                Ui.Text(Entry.Pronunciation ?? "—", 12, Ui.Muted, mono: true),
                Ui.Text(Entry.PartOfSpeech ?? "—", 12, Ui.Muted, mono: true),
                Entry.AudioUrl is null
                    ? new Border()
                    : Ui.IconButton("sound", "Play the pronunciation", () =>
                    {
                        if (!Launch.Url(Entry.AudioUrl)) _context.Toast("Could not open the pronunciation.");
                    })),
            Ui.Rule());

        stack.Children.Add(Section("HEADWORD", _headword));
        stack.Children.Add(Ui.Columns("*,14,*",
            Section("PRONUNCIATION", _pronunciation),
            new Border(),
            Section("PART OF SPEECH", _partOfSpeech)));
        stack.Children.Add(Section("DEFINITION", _definition));
        stack.Children.Add(TranslationSection());

        foreach (var kind in new[] { RelationKind.Synonym, RelationKind.Antonym, RelationKind.Related })
            stack.Children.Add(RelationSection(kind));

        stack.Children.Add(SentenceSection());
        stack.Children.Add(TagSection());
        stack.Children.Add(Section("NOTES", _notes));
        stack.Children.Add(Ui.Rule());

        stack.Children.Add(Ui.Row(10,
            Ui.Button("Save", Save, "primary"),
            Ui.Button(Entry.Starred ? "Unstar" : "Star", () =>
            {
                _context.Library.SetStarred(Entry.Id, !Entry.Starred);
                Render();
            }, "outline"),
            Ui.Button(Entry.Archived ? "Unarchive" : "Archive", () =>
            {
                var archiving = !Entry.Archived;
                _context.Library.SetArchived(Entry.Id, archiving);
                _context.Toast(archiving ? "Word archived." : "Word restored.");
                Render();
            }, "outline"),
            Ui.Button("Delete", ConfirmDelete, "outline"),
            Ui.Button("Quiz this word", () => _context.Navigate("quiz", Entry.Id), "outline")));

        _body.Content = Ui.Scroll(Ui.Pad(stack, 30));
    }

    private static Control Section(string caption, Control control) => Ui.Stack(7, Ui.Label(caption), control);

    /// <summary>The translation field. The library fills it from the bundled
    /// atlas.dict data when a word is added, so there is nothing to fetch here.</summary>
    private Control TranslationSection() => Section("TRANSLATION", _translation);

    private Control RelationSection(RelationKind kind)
    {
        var words = _context.Library.Relations(Entry.Id).Where(r => r.Kind == kind).ToList();

        var chips = new WrapPanelHost(words.Select(relation => Ui.Chip(
            relation.Word,
            click: () => OpenOrOffer(relation.Word),
            remove: () => { _context.Library.RemoveRelation(Entry.Id, kind, relation.Word); Render(); })));

        var box = new TextBox
        {
            Watermark = $"Add a {Singular(kind)}…",
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var word = box.Text?.Trim() ?? "";
            if (word.Length == 0) return;
            _context.Library.AddRelation(Entry.Id, kind, word);
            Render();
        };

        return Ui.Stack(8, Ui.Label(Plural(kind)), chips, box);
    }

    /// <summary>"related" is already an adjective, so it takes no plural "s".</summary>
    private static string Plural(RelationKind kind) => kind switch
    {
        RelationKind.Related => "RELATED WORDS",
        RelationKind.Rhyme => "RHYMES",
        RelationKind.Form => "FORMS",
        RelationKind.Antonym => "ANTONYMS",
        _ => "SYNONYMS",
    };

    private static string Singular(RelationKind kind) =>
        kind == RelationKind.Related ? "related word" : kind.ToColumn();

    private void OpenOrOffer(string word)
    {
        if (_context.Library.FindEntry(word) is { } existing)
        {
            _context.Navigate("library", existing.Id);
            return;
        }

        _body.Content = Ui.Pad(Ui.Stack(16,
            Ui.Heading("NOT YET IN THE LIBRARY", word, "Add it and the network grows by one."),
            Ui.Row(10,
                Ui.Button("Back", Render, "outline"),
                Ui.Button("+ Add as new entry", () =>
                {
                    var id = _context.Library.AddEntry(word, "lookup");
                    _context.Toast($"{word} added.");
                    _context.Navigate("library", id);
                }, "primary"))), 30);
    }

    private Control SentenceSection()
    {
        var stack = Ui.Stack(9, Ui.Label("EXAMPLES"));

        foreach (var sentence in _context.Library.SentencesFor(Entry.Id))
        {
            var text = Ui.Text(sentence.Text, 13);
            text.TextWrapping = TextWrapping.Wrap;

            var marked = sentence.ClozeStart is not null;

            stack.Children.Add(new Border
            {
                BorderBrush = Ui.Line,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 9),
                Child = Ui.Columns("*,Auto",
                    Ui.Stack(5, text,
                        Ui.Label((sentence.Source?.ToUpperInvariant() ?? "NO SOURCE") +
                                 (marked ? "  ·  CLOZE SET" : ""))),
                    Ui.Row(0,
                        Ui.IconButton("check", "Set the cloze blank from this sentence", () =>
                        {
                            if (Cloze.Find(sentence.Text, Entry.Headword) is not { } span)
                            {
                                _context.Toast("This sentence does not contain the word.");
                                return;
                            }
                            _context.Library.UpdateSentence(sentence with
                            { ClozeStart = span.Start, ClozeLength = span.Length });
                            _context.Toast("Cloze blank set.");
                            Render();
                        }),
                        Ui.IconButton("close", "Delete this sentence", () =>
                        {
                            _context.Library.DeleteSentence(sentence.Id);
                            Render();
                        }))),
            });
        }

        var box = new TextBox { Watermark = "Add an example sentence…" };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var text = box.Text?.Trim() ?? "";
            if (text.Length == 0) return;
            _context.Library.AddSentence(text, Entry.Id);
            Render();
        };
        stack.Children.Add(box);

        return stack;
    }

    private Control TagSection()
    {
        var chips = new WrapPanelHost(_context.Library.TagsFor(Entry.Id).Select(tag => Ui.Chip(
            tag, remove: () => { _context.Library.UntagEntry(Entry.Id, tag); Render(); })));

        var box = new TextBox { Watermark = "Add a tag…", Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var tag = box.Text?.Trim() ?? "";
            if (tag.Length == 0) return;
            _context.Library.TagEntry(Entry.Id, tag);
            Render();
        };

        return Ui.Stack(8, Ui.Label("TAGS"), chips, box);
    }

    public void Save()
    {
        var headword = _headword.Text?.Trim() ?? "";
        if (Library.Normalize(headword).Length == 0)
        {
            _context.Toast("A word needs a headword.");
            return;
        }

        var clash = _context.Library.FindEntry(headword);
        if (clash is not null && clash.Id != Entry.Id)
        {
            _context.Toast("Another entry already uses that headword.");
            return;
        }

        _context.Library.UpdateEntry(Entry with
        {
            Headword = headword,
            Pronunciation = Blank(_pronunciation.Text),
            PartOfSpeech = Blank(_partOfSpeech.Text),
            Definition = Blank(_definition.Text),
            Notes = Blank(_notes.Text),
            Translation = Blank(_translation.Text),
        });

        _context.Toast("Saved.");
        _refreshList();
        Render();

        static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public void ConfirmDelete()
    {
        _body.Content = Ui.Pad(Ui.Stack(18,
            Ui.Heading("LIBRARY MANAGEMENT", "Remove this word?",
                "Its sentences, relations and review history go with it. This cannot be undone."),
            Ui.Display(Entry.Headword, 24),
            Ui.Row(12,
                Ui.Button("Keep it", Render, "outline"),
                Ui.Button("Delete", () =>
                {
                    _context.Library.DeleteEntry(Entry.Id);
                    _context.Toast("Word deleted.");
                    _refreshList();
                }, "primary"))), 30);
    }
}

/// <summary>A wrapping row of chips. Avalonia's core package has no WrapPanel, so this
/// lays children out left to right and wraps at the available width.</summary>
public sealed class WrapPanelHost : Panel
{
    private const double Gap = 6;

    public WrapPanelHost(IEnumerable<Control> children)
    {
        foreach (var child in children) Children.Add(child);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        double x = 0, y = 0, lineHeight = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            if (x + child.DesiredSize.Width > width && x > 0)
            {
                x = 0;
                y += lineHeight + Gap;
                lineHeight = 0;
            }
            x += child.DesiredSize.Width + Gap;
            lineHeight = Math.Max(lineHeight, child.DesiredSize.Height);
        }

        return new Size(width, y + lineHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;

        foreach (var child in Children)
        {
            if (x + child.DesiredSize.Width > finalSize.Width && x > 0)
            {
                x = 0;
                y += lineHeight + Gap;
                lineHeight = 0;
            }
            child.Arrange(new Rect(x, y, child.DesiredSize.Width, child.DesiredSize.Height));
            x += child.DesiredSize.Width + Gap;
            lineHeight = Math.Max(lineHeight, child.DesiredSize.Height);
        }

        return finalSize;
    }
}
