using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Diagnostics;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class QuizView : UserControl, IReceivesArgument
{
    private readonly ShellContext _context;
    private readonly ContentControl _body = new();
    private readonly Stopwatch _clock = new();

    public ReviewSession? Session { get; private set; }
    public bool IsRevealed { get; private set; }
    public TextBox AnswerBox { get; private set; } = new();

    /// <summary>The grade the app worked out for you on an auto-graded card. It is
    /// preselected rather than recorded, so seeing the answer can still change it.</summary>
    public Grade SuggestedGrade { get; private set; } = Grade.Good;

    public QuizView(ShellContext context)
    {
        _context = context;
        Content = _body;
        Focusable = true;
        KeyDown += OnKeyDown;
        ShowSetup();
    }

    public void Receive(object argument)
    {
        switch (argument)
        {
            case ReviewScope scope:
                Start(scope, null);
                break;
            case long entryId:
                Start(new ReviewScope(ScopeKind.Single, EntryId: entryId), null);
                break;
        }
    }

    private void ShowSetup()
    {
        Session = null;
        IsRevealed = false;

        var modes = new ComboBox
        {
            ItemsSource = new[] { "Mixed", "Flip", "Multiple choice", "Cloze", "Typed" },
            SelectedIndex = 0,
            Width = 200,
        };

        var decks = _context.Library.Decks();
        var tags = _context.Library.Tags();
        var scopes = new List<string> { "Due queue", "Starred", "Everything", "Free practice" };
        scopes.AddRange(decks.Select(d => $"Deck: {d.Name}"));
        scopes.AddRange(tags.Select(t => $"Tag: {t.Name}"));

        var scopeBox = new ComboBox { ItemsSource = scopes, SelectedIndex = 0, Width = 200 };

        var summary = _context.Stats.Summary(DateTime.UtcNow);

        var tiles = Ui.Columns("*,14,*,14,*,14,*",
            Ui.StatTile("01", $"{summary.DueToday}", "DUE TODAY"),
            new Border(),
            Ui.StatTile("02", $"{summary.ReviewsToday}/{_context.Settings.DailyGoal}", "TODAY'S GOAL"),
            new Border(),
            Ui.StatTile("03", $"{summary.StreakDays}", "DAY STREAK"),
            new Border(),
            Ui.StatTile("04", $"{summary.Retention:P0}", "RETENTION"));

        _body.Content = Ui.Pad(Ui.Stack(24,
            Ui.Heading("REFINEMENT PROTOCOL", "Time to remember.",
                "Grade honestly. The schedule only works if you do."),
            tiles,
            Ui.Rule(),
            Ui.Row(16,
                Ui.Stack(7, Ui.Label("MODE"), modes),
                Ui.Stack(7, Ui.Label("SCOPE"), scopeBox)),
            Ui.Row(12,
                Ui.Button("Begin session", () =>
                {
                    var mode = modes.SelectedIndex switch
                    {
                        1 => QuizMode.Flip,
                        2 => QuizMode.Choice,
                        3 => QuizMode.Cloze,
                        4 => QuizMode.Typed,
                        _ => (QuizMode?)null,
                    };

                    var index = scopeBox.SelectedIndex;
                    var scope = index switch
                    {
                        0 => new ReviewScope(ScopeKind.Due),
                        1 => new ReviewScope(ScopeKind.Starred),
                        2 => new ReviewScope(ScopeKind.All),
                        3 => new ReviewScope(ScopeKind.FreePractice),
                        _ when index - 4 < decks.Count => new ReviewScope(ScopeKind.Deck, DeckId: decks[index - 4].Id),
                        _ => new ReviewScope(ScopeKind.Tag, Tag: tags[index - 4 - decks.Count].Name),
                    };

                    Start(scope, mode);
                }, "primary")),
            Ui.Text("Space flips · 1–4 grade Again, Hard, Good, Easy · Esc ends the session.", 12, Ui.Muted)), 34);
    }

    public void Start(ReviewScope scope, QuizMode? mode)
    {
        Session = new ReviewSession(_context.Library, scope,
            new SessionOptions(_context.Settings.NewPerDay, mode), DateTime.UtcNow);

        if (Session.IsFinished)
        {
            Session = null;
            _body.Content = Ui.Pad(Ui.Stack(16,
                Ui.Heading("NOTHING DUE", "You are up to date.",
                    "Add words, or run a free practice session over everything you own."),
                Ui.Row(12, Ui.Button("Back", ShowSetup, "outline"))), 34);
            return;
        }

        RenderCard();
    }

    private void RenderCard()
    {
        if (Session?.Current is not { } card) { RenderSummary(); return; }

        IsRevealed = false;
        _clock.Restart();
        Focus();

        var header = Ui.Columns("*,Auto",
            Ui.Label($"{Session.Position:00} / {Session.Total:00}  ·  {card.Mode.ToColumn().ToUpperInvariant()}"),
            Ui.Button("End session", RenderSummary, "outline"));

        var face = card.Mode switch
        {
            QuizMode.Choice => ChoiceFace(card),
            QuizMode.Cloze => TypedFace(card, card.Prompt, "Which word fills the blank?"),
            QuizMode.Typed => TypedFace(card, card.Prompt, "Which word is this?"),
            _ => FlipFace(card),
        };

        _body.Content = Ui.Pad(Ui.Rows("Auto,*", header, face), 34);
        Motion.Enter(face, 10);
    }

    private Control FlipFace(Card card) => Ui.Stack(26,
        Ui.Display(card.Entry.Headword, 44),
        Ui.Row(12, Ui.Button("Reveal  ·  Space", Reveal, "primary")));

    private Control ChoiceFace(Card card)
    {
        var options = Ui.Stack(8);

        for (var i = 0; i < card.Choices.Count; i++)
        {
            var index = i;
            var button = Ui.Button(card.Choices[i], () => AnswerChoice(card, index), "outline");
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(16, 12);
            options.Children.Add(button);
        }

        return Ui.Stack(24, Ui.Label("SELECT THE WORD"), Ui.Display(card.Prompt, 24), options);
    }

    private Control TypedFace(Card card, string prompt, string caption)
    {
        AnswerBox = new TextBox { Watermark = "Your answer…" };
        AnswerBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) SubmitTyped(); };

        return Ui.Stack(24,
            Ui.Label(caption.ToUpperInvariant()),
            Ui.Display(prompt, 26),
            AnswerBox,
            Ui.Row(12,
                Ui.Button("Submit", SubmitTyped, "primary"),
                Ui.Button("I don't know", () => Commit(Grade.Again), "outline")));
    }

    private void AnswerChoice(Card card, int index)
    {
        var correct = index == card.CorrectChoice;
        RevealAnswer(card, correct
            ? "Correct."
            : $"Not quite — the word is {card.Entry.Headword}.", correct ? Grade.Good : Grade.Again);
    }

    public void SubmitTyped()
    {
        if (Session?.Current is not { } card) return;

        var answer = AnswerBox.Text?.Trim() ?? "";
        var distance = Cloze.Distance(answer, card.Entry.Headword);
        var tolerance = card.Mode == QuizMode.Cloze ? 1 : 2;

        var grade = distance == 0 ? Grade.Good : distance <= tolerance ? Grade.Hard : Grade.Again;
        var message = grade switch
        {
            Grade.Good => "Correct.",
            Grade.Hard => $"Almost — the word is {card.Entry.Headword}.",
            _ => $"The word is {card.Entry.Headword}.",
        };

        RevealAnswer(card, message, grade);
    }

    private void RevealAnswer(Card card, string verdict, Grade suggested)
    {
        IsRevealed = true;
        SuggestedGrade = suggested;

        var back = Ui.Stack(18,
            Ui.Label("ANSWER"),
            Ui.Display(card.Entry.Headword, 34));

        if (verdict.Length > 0)
            back.Children.Add(Ui.Text(verdict, 13, suggested == Grade.Again ? Ui.Danger : Ui.Ink));

        back.Children.Add(Ui.Rule());
        back.Children.Add(Details(card));
        back.Children.Add(Ui.Rule());
        back.Children.Add(GradeRow());

        _body.Content = Ui.Pad(Ui.Rows("Auto,*",
            Ui.Label($"{Session!.Position:00} / {Session.Total:00}  ·  {card.Mode.ToColumn().ToUpperInvariant()}"),
            Ui.Scroll(back)), 34);

        Motion.Enter(back, 8);
        Focus();
    }

    public void Reveal()
    {
        if (Session?.Current is not { } card || IsRevealed) return;
        RevealAnswer(card, "", Grade.Good);
    }

    private Control Details(Card card)
    {
        var stack = Ui.Stack(12);

        if (!string.IsNullOrWhiteSpace(card.Entry.Definition))
        {
            var definition = Ui.Text(card.Entry.Definition!, 14);
            definition.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(Ui.Stack(6, Ui.Label("DEFINITION"), definition));
        }

        var synonyms = _context.Library.Relations(card.Entry.Id)
            .Where(r => r.Kind == RelationKind.Synonym).Select(r => r.Word).ToList();

        if (synonyms.Count > 0)
            stack.Children.Add(Ui.Stack(6, Ui.Label("SYNONYMS"),
                Ui.Text(string.Join("   ", synonyms), 13, Ui.Muted)));

        if (card.Sentence is { } sentence)
        {
            var text = Ui.Text("“" + sentence.Text + "”", 14, Ui.Ink);
            text.FontFamily = Ui.Serif;
            text.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(Ui.Stack(6, Ui.Label("EXAMPLE"), text));
        }

        return stack;
    }

    private Control GradeRow()
    {
        var row = Ui.Row(10);

        foreach (var (grade, caption) in new[]
        {
            (Grade.Again, "Again"), (Grade.Hard, "Hard"), (Grade.Good, "Good"), (Grade.Easy, "Easy"),
        })
        {
            var days = Session!.PreviewIntervalDays(grade);
            var chosen = grade;
            var preselected = grade == SuggestedGrade;

            var button = Ui.Button("", () => Commit(chosen), preselected ? "primary" : "outline");
            button.Content = Ui.Stack(4,
                Ui.Text(caption + (preselected ? "  ·  Enter" : ""), 13,
                    preselected ? Ui.OnPrimary : Ui.Ink),
                Ui.Label(days == 0 ? "NOW" : $"{days} D",
                    preselected ? Ui.OnPrimaryMuted : Ui.Muted));
            row.Children.Add(button);
        }

        return row;
    }

    public void GradeCard(Grade grade)
    {
        if (Session?.Current is null || !IsRevealed) return;
        Commit(grade);
    }

    private void Commit(Grade grade)
    {
        if (Session?.Current is null) return;
        Session.Answer(grade, (int)_clock.ElapsedMilliseconds, DateTime.UtcNow);
        RenderCard();
    }

    private void RenderSummary()
    {
        var summary = Session?.Summary ?? new SessionSummary(0, 0, 0, TimeSpan.Zero);
        Session = null;
        IsRevealed = false;

        var accuracy = summary.Reviewed == 0 ? 0 : summary.Correct / (double)summary.Reviewed;

        _body.Content = Ui.Pad(Ui.Stack(24,
            Ui.Heading("SESSION COMPLETE", "Please enjoy each word equally.",
                "Your schedule has been updated."),
            Ui.Columns("*,14,*,14,*",
                Ui.StatTile("01", $"{summary.Reviewed}", "REVIEWED"),
                new Border(),
                Ui.StatTile("02", $"{accuracy:P0}", "ACCURACY"),
                new Border(),
                Ui.StatTile("03", $"{summary.Lapsed}", "LAPSED")),
            Ui.Row(12,
                Ui.Button("Another session", ShowSetup, "primary"),
                Ui.Button("Back to the library", () => _context.Navigate("library", null), "outline"))), 34);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Session?.Current is null) return;
        if (e.KeyModifiers != KeyModifiers.None) return;

        switch (e.Key)
        {
            case Key.Space when !IsRevealed:
                Reveal(); e.Handled = true; break;
            case Key.Enter when IsRevealed:
                GradeCard(SuggestedGrade); e.Handled = true; break;
            case Key.D1 or Key.NumPad1: GradeCard(Grade.Again); e.Handled = true; break;
            case Key.D2 or Key.NumPad2: GradeCard(Grade.Hard); e.Handled = true; break;
            case Key.D3 or Key.NumPad3: GradeCard(Grade.Good); e.Handled = true; break;
            case Key.D4 or Key.NumPad4: GradeCard(Grade.Easy); e.Handled = true; break;
            case Key.Escape: RenderSummary(); e.Handled = true; break;
        }
    }
}
