using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class LookupView : UserControl, IReceivesArgument
{
    private readonly ShellContext _context;
    private readonly ContentControl _body = new();
    private readonly Dictionary<(RelationKind Kind, string Word), CheckBox> _wordBoxes = [];
    private readonly Dictionary<LookupSense, CheckBox> _senseBoxes = [];
    private CancellationTokenSource? _inFlight;

    public TextBox QueryBox { get; } = new() { Watermark = "A word to look up…" };
    public LookupResult? Result { get; private set; }

    public IReadOnlyList<(RelationKind Kind, string Word)> SelectedWords =>
        _wordBoxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();

    public IReadOnlyList<LookupSense> SelectedSenses =>
        _senseBoxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();

    public LookupView(ShellContext context)
    {
        _context = context;

        var search = Ui.Columns("*,Auto",
            QueryBox,
            Ui.Button("Look up", () => _ = RunAsync(QueryBox.Text ?? ""), "primary"));
        search.Margin = new Thickness(0, 0, 0, 20);

        QueryBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) _ = RunAsync(QueryBox.Text ?? "");
        };

        _body.Content = Idle();
        Content = Ui.Pad(Ui.Rows("Auto,*", search, Ui.Scroll(_body)), 30);
    }

    public void Receive(object argument)
    {
        if (argument is not string word) return;
        QueryBox.Text = word;
        _ = RunAsync(word);
    }

    public async Task RunAsync(string word)
    {
        var term = word.Trim();
        if (term.Length == 0) return;

        if (!_context.Settings.LookupEnabled)
        {
            Result = Lookup.Offline(term, "Lookup is switched off in Settings.");
            Render();
            return;
        }

        _inFlight?.Cancel();
        _inFlight = new CancellationTokenSource();
        _body.Content = Ui.Pad(Ui.Label("CONSULTING THE RECORD…"), 20);

        LookupResult result;
        try
        {
            result = await _context.Lookup.LookupAsync(term, _inFlight.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Result = result;

        if (Dispatcher.UIThread.CheckAccess()) Render();
        else Dispatcher.UIThread.Post(Render);
    }

    private void Render()
    {
        _wordBoxes.Clear();
        _senseBoxes.Clear();

        if (Result is not { } result) { _body.Content = Idle(); return; }

        if (!result.Online)
        {
            _body.Content = Ui.Stack(16,
                Ui.Label("LOOKUP UNAVAILABLE  ·  OFFLINE"),
                Ui.Display(result.Word, 26),
                Ui.Text(result.Message ?? "No connection.", 13, Ui.Muted),
                Ui.Row(12, Ui.Button("Add by hand", () =>
                {
                    var id = _context.Library.AddEntry(result.Word);
                    _context.Toast("Word added. Fill it in yourself.");
                    _context.Navigate("library", id);
                }, "primary")));
            return;
        }

        var existing = _context.Library.FindEntry(result.Word);

        var stack = Ui.Stack(20,
            Ui.Label($"LOOKUP / {(existing is null ? "NEW" : "ALREADY IN THE LIBRARY")}"),
            Ui.Display(result.Word),
            Ui.Row(12,
                Ui.Text(result.Pronunciation ?? "—", 12, Ui.Muted, mono: true),
                result.AudioUrl is null
                    ? new Border()
                    : Ui.Button("▸ Pronounce", () =>
                    {
                        if (!Launch.Url(result.AudioUrl)) _context.Toast("Could not open the pronunciation.");
                    }, "outline")),
            Ui.Rule());

        foreach (var kind in new[]
                 { RelationKind.Synonym, RelationKind.Antonym, RelationKind.Related, RelationKind.Rhyme })
        {
            var words = result.Relations.GetValueOrDefault(kind) ?? [];
            if (words.Count == 0) continue;

            var boxes = words.Select(word =>
            {
                var box = new CheckBox
                {
                    Content = word,
                    IsChecked = kind is RelationKind.Synonym or RelationKind.Antonym,
                };
                _wordBoxes[(kind, word)] = box;
                return (Control)box;
            });

            stack.Children.Add(Ui.Stack(8,
                Ui.Label(kind.ToColumn().ToUpperInvariant() + "S"),
                new WrapPanelHost(boxes)));
        }

        if (result.Senses.Count > 0)
        {
            var senses = Ui.Stack(12, Ui.Label("SENSES"));

            foreach (var sense in result.Senses)
            {
                var box = new CheckBox { IsChecked = _senseBoxes.Count == 0 };
                _senseBoxes[sense] = box;

                var definition = Ui.Text(sense.Definition, 13);
                definition.TextWrapping = TextWrapping.Wrap;

                var detail = Ui.Stack(6, Ui.Label(sense.PartOfSpeech.ToUpperInvariant()), definition);

                foreach (var example in sense.Examples)
                {
                    var text = Ui.Text("“" + example + "”", 12.5, Ui.Muted);
                    text.FontFamily = Ui.Serif;
                    text.TextWrapping = TextWrapping.Wrap;
                    detail.Children.Add(text);
                }

                senses.Children.Add(new Border
                {
                    BorderBrush = Ui.Line,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(0, 10),
                    Child = Ui.Columns("Auto,*", box, detail),
                });
            }

            stack.Children.Add(senses);
        }

        stack.Children.Add(Ui.Rule());
        stack.Children.Add(Ui.Row(12,
            Ui.Button(existing is null ? "Add to library" : "Merge into existing", () =>
            {
                var id = Commit();
                _context.Toast(existing is null ? "Word added." : "Word merged.");
                _context.Navigate("library", id);
            }, "primary"),
            Ui.Button("Tick everything", () =>
            {
                foreach (var box in _wordBoxes.Values) box.IsChecked = true;
                foreach (var box in _senseBoxes.Values) box.IsChecked = true;
            }, "outline"),
            Ui.Button("Tick nothing", () =>
            {
                foreach (var box in _wordBoxes.Values) box.IsChecked = false;
                foreach (var box in _senseBoxes.Values) box.IsChecked = false;
            }, "outline")));

        _body.Content = stack;
        Motion.Enter(stack, 8);
    }

    public long Commit()
    {
        if (Result is not { Online: true } result)
            throw new InvalidOperationException("There is nothing to commit.");

        var id = _context.Library.AddEntry(result.Word, "lookup");
        var entry = _context.Library.GetEntry(id)!;
        var senses = SelectedSenses;

        _context.Library.UpdateEntry(entry with
        {
            Pronunciation = entry.Pronunciation ?? result.Pronunciation,
            AudioUrl = entry.AudioUrl ?? result.AudioUrl,
            PartOfSpeech = entry.PartOfSpeech ?? senses.FirstOrDefault()?.PartOfSpeech,
            Definition = string.IsNullOrWhiteSpace(entry.Definition)
                ? string.Join("  ·  ", senses.Select(s => s.Definition))
                : entry.Definition,
        });

        foreach (var (kind, word) in SelectedWords)
            _context.Library.AddRelation(id, kind, word, "lookup");

        var known = _context.Library.SentencesFor(id).Select(s => s.Text).ToHashSet();
        foreach (var example in senses.SelectMany(s => s.Examples))
            if (known.Add(example)) _context.Library.AddSentence(example, id, "dictionaryapi.dev");

        return id;
    }

    private Control Idle() => Ui.Stack(12,
        Ui.Label("LOOKUP"),
        Ui.Display("Find a word.", 26),
        Ui.Text(_context.Settings.LookupEnabled
            ? "Synonyms and antonyms come from Datamuse; senses and examples from dictionaryapi.dev. Nothing else leaves this machine."
            : "Lookup is switched off in Settings. Words can still be added by hand.", 12, Ui.Muted));
}
