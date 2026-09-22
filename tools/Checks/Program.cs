using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using SirWordALot.Core;
using SirWordALot.Shell;
using SirWordALot.Views;

namespace SirWordALot.Checks;

/// <summary>Headless verification against real Avalonia controls. Never touches the
/// live desktop; exits non-zero when anything fails.</summary>
internal static class Program
{
    private static int _failures;
    private static int _passes;

    [STAThread]
    public static int Main(string[] args)
    {
        AppBuilder.Configure<HeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .WithInterFont()
            .SetupWithoutStarting();

        Section("palette", CheckPalette);
        Section("typography", CheckTypography);
        Section("factories", CheckFactories);
        Section("shell", CheckShell);
        Section("library view", CheckLibraryView);
        Section("sentences view", CheckSentencesView);
        Section("lookup view", CheckLookupView);
        Section("quiz view", CheckQuizView);
        Section("decks view", CheckDecksView);
        Section("stats view", CheckStatsView);
        Section("settings view", CheckSettingsView);
        Section("command palette", CheckCommandPalette);

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? $"All {_passes} checks passed."
            : $"{_failures} of {_passes + _failures} checks failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static void Section(string name, Action run)
    {
        Console.WriteLine($"[{name}]");
        try
        {
            run();
        }
        catch (Exception e)
        {
            Fail($"{name} threw {e.GetType().Name}: {e.Message}");
            Console.WriteLine(e.StackTrace);
        }
    }

    // ---- visual system -------------------------------------------------

    private static void CheckPalette()
    {
        Expect("Ink", "#FF173B40", Hex(Ui.Ink));
        Expect("Cream", "#FFECEDE0", Hex(Ui.Cream));
        Expect("Muted", "#FF688178", Hex(Ui.Muted));
        Expect("Line", "#FFBCC8BB", Hex(Ui.Line));
        Expect("Phosphor", "#FFB3E5DA", Hex(Ui.Phosphor));
        Expect("Dark", "#FF102C32", Hex(Ui.Dark));
        Expect("Sidebar", "#FFDFE4D6", Hex(Ui.Sidebar));
        Expect("RowRule", "#FFD8DECF", Hex(Ui.RowRule));
        Expect("WindowBorder", "#FF9CAEA0", Hex(Ui.WindowBorder));
        Expect("TitlebarActive", "#FFE9EBDD", Hex(Ui.TitlebarActive));
        Expect("TitlebarInactive", "#FFCCD6C9", Hex(Ui.TitlebarInactive));
        Expect("Danger", "#FF99513D", Hex(Ui.Danger));
    }

    private static void CheckTypography()
    {
        var label = Ui.Label("LIBRARY");
        Expect("label size", 10.5, label.FontSize);
        Expect("label spacing", 1.2, label.LetterSpacing);
        Expect("label font", "Cascadia Mono", label.FontFamily.Name);
        Expect("label colour", Hex(Ui.Muted), Hex(label.Foreground!));

        Expect("display font", "Georgia", Ui.Display("Eminence").FontFamily.Name);
        Expect("display size", 32d, Ui.Display("Eminence").FontSize);
        Expect("body size", 13d, Ui.Text("body").FontSize);
        Expect("mono text font", "Cascadia Mono", Ui.Text("x", mono: true).FontFamily.Name);
    }

    private static void CheckFactories()
    {
        Expect("rule height", 1d, Ui.Rule().Height);
        Expect("button radius", 0d, Ui.Button("Save", () => { }).CornerRadius.TopLeft);
        Expect("hex id", "0X 4A 92", Ui.HexId(0x4A92));
        Expect("hex id pads", "0X 00 07", Ui.HexId(7));

        var columns = Ui.Columns("158,*", new Border(), new Border());
        Expect("columns count", 2, columns.ColumnDefinitions.Count);
        Expect("columns width", 158d, columns.ColumnDefinitions[0].Width.Value);

        Expect("glyph width", 15d, new Glyph("search") { Width = 15, Height = 15 }.Width);

        Expect("launch rejects script urls", false, Launch.Url("javascript:alert(1)"));
        Expect("launch rejects blanks", false, Launch.Url(""));
        Expect("launch rejects relative", false, Launch.Url("not a url"));
    }

    // ---- shell ---------------------------------------------------------

    private static void CheckShell()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        var id = library.AddEntry("eminence");
        library.UpdateEntry(library.GetEntry(id)! with { Definition = "Fame." });

        var window = new MainWindow(library, new Settings(db), new Search(db), new Stats(db), new StubLookup());
        window.Show();

        Expect("no system decorations", SystemDecorations.None, window.SystemDecorations);
        Expect("default width", 1180d, window.Width);
        Expect("default height", 760d, window.Height);
        Expect("min width", 1040d, window.MinWidth);
        Expect("min height", 680d, window.MinHeight);
        Expect("window background", "#FFECEDE0", Hex(window.Background!));

        Expect("titlebar height", 43d, window.Titlebar.Height);
        Expect("titlebar colour", "#FFE9EBDD", Hex(window.Titlebar.Background!));
        Expect("sidebar width", 170d, window.SidebarPanel.Width);
        Expect("sidebar colour", "#FFDFE4D6", Hex(window.SidebarPanel.Background!));
        Expect("footer height", 42d, window.Footer.Height);
        Expect("nav items", 11, window.Nav.Count);
        Expect("no console view", false, window.Nav.Any(i => i.Id == "console"));
        Expect("starts on library", "library", window.CurrentViewId);

        foreach (var item in window.Nav)
        {
            window.Navigate(item.Id);
            Expect($"navigate {item.Id}", item.Id, window.CurrentViewId);
            Expect($"{item.Id} has content", true, window.Host.Content is not null);

            // Exactly one sidebar entry may be highlighted, and it must be this one.
            Expect($"{item.Id} is the only highlight",
                item.Id, string.Join(",", window.HighlightedNavIds));
        }

        window.Navigate("library");
        Expect("status counts words", true, window.StatusRight.Text!.Contains("1 WORDS"));
        Expect("status shows online", true, window.StatusRight.Text!.Contains("ONLINE"));

        CheckResizeEdges(window);

        window.Close();
    }

    /// <summary>A frameless window has no platform resize border, so every edge and
    /// corner is hit-tested by hand. These assert the zones rather than trusting them.</summary>
    private static void CheckResizeEdges(MainWindow window)
    {
        var w = window.Width;
        var h = window.Height;

        Expect("resize NW", WindowEdge.NorthWest, window.ResizeEdgeAt(new Point(2, 2)));
        Expect("resize NE", WindowEdge.NorthEast, window.ResizeEdgeAt(new Point(w - 2, 2)));
        Expect("resize SW", WindowEdge.SouthWest, window.ResizeEdgeAt(new Point(2, h - 2)));
        Expect("resize SE", WindowEdge.SouthEast, window.ResizeEdgeAt(new Point(w - 2, h - 2)));

        Expect("resize W", WindowEdge.West, window.ResizeEdgeAt(new Point(2, h / 2)));
        Expect("resize E", WindowEdge.East, window.ResizeEdgeAt(new Point(w - 2, h / 2)));
        Expect("resize N", WindowEdge.North, window.ResizeEdgeAt(new Point(w / 2, 2)));
        Expect("resize S", WindowEdge.South, window.ResizeEdgeAt(new Point(w / 2, h - 2)));

        Expect("interior does not resize", null, window.ResizeEdgeAt(new Point(w / 2, h / 2)));
        Expect("just inside the edge does not resize", null, window.ResizeEdgeAt(new Point(20, h / 2)));
        Expect("outside does not resize", null, window.ResizeEdgeAt(new Point(-5, h / 2)));

        // The top edge must win over the titlebar's drag handler.
        Expect("top edge beats the titlebar", WindowEdge.North, window.ResizeEdgeAt(new Point(300, 1)));

        window.WindowState = WindowState.Maximized;
        Expect("maximized does not resize", null, window.ResizeEdgeAt(new Point(2, 2)));
        window.WindowState = WindowState.Normal;
    }

    // ---- views ---------------------------------------------------------

    private static void CheckLibraryView()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        var eminence = library.AddEntry("eminence");
        library.UpdateEntry(library.GetEntry(eminence)! with
        { PartOfSpeech = "noun", Definition = "A position of superiority." });
        library.AddRelation(eminence, RelationKind.Synonym, "renown");
        library.AddSentence("She rose to eminence.", eminence);
        library.AddEntry("laconic");

        var context = Context(db, library);

        var view = new LibraryView(context, "library");
        Expect("library lists both", 2, view.Hits.Count);

        // A headless TextBox has no template, so its TextChanged never fires; the
        // filter itself is what matters here, so drive Refresh directly.
        view.SearchBox.Text = "lacon";
        view.Refresh();
        Expect("search narrows", 1, view.Hits.Count);
        Expect("search matches", "laconic", view.Hits[0].Entry.Headword);

        view.SearchBox.Text = "";
        view.Refresh();
        view.Select(eminence);
        Expect("detail opens", true, view.Detail.Content is EntryView);

        var detail = (EntryView)view.Detail.Content!;
        Expect("detail entry", "eminence", detail.Entry.Headword);

        var starred = new LibraryView(context, "starred");
        Expect("starred preset empty", 0, starred.Hits.Count);

        library.SetStarred(eminence, true);
        Expect("starred preset updates", 1, starred.Hits.Count);

        var deckId = view.SaveFilterAsDeck("Everything");
        Expect("filter saved as smart deck", true,
            library.Decks().Single(d => d.Id == deckId).FilterJson is { Length: > 0 });

        CheckRowMenu(view, library);
        CheckSelectMode(view, library);

        view.Dispose();
        starred.Dispose();
    }

    private static void CheckRowMenu(LibraryView view, Library library)
    {
        var entry = library.FindEntry("laconic")!;
        var menu = view.RowMenu(entry);
        var headers = Headers(menu);

        Expect("row menu opens", true, headers.Contains("Open"));
        Expect("row menu quizzes", true, headers.Contains("Quiz this word"));
        Expect("row menu stars", true, headers.Contains("Star"));
        Expect("row menu archives", true, headers.Contains("Archive"));
        Expect("row menu looks up", true, headers.Contains("Look this word up"));
        Expect("row menu copies", true, headers.Contains("Copy headword"));
        Expect("row menu deletes", true, headers.Contains("Delete..."));

        // A manual deck is offerable; a smart deck is not, because its membership
        // follows a filter and cannot be added to by hand.
        var manual = library.CreateDeck("Hand-built");
        var refreshed = Headers(view.RowMenu(entry));
        Expect("add-to-deck offers manual decks", true, refreshed.Contains("Hand-built"));
        Expect("add-to-deck hides smart decks", false, refreshed.Contains("Everything"));

        library.AddToDeck(manual, entry.Id);
        var member = FindItem(view.RowMenu(entry), "Hand-built");
        Expect("a deck already holding the word is disabled", false, member?.IsEnabled);

        // The label must track state rather than always reading "Star".
        library.SetStarred(entry.Id, true);
        Expect("star label follows state", true,
            Headers(view.RowMenu(library.GetEntry(entry.Id)!)).Contains("Unstar"));
        library.SetStarred(entry.Id, false);
    }

    private static void CheckSelectMode(LibraryView view, Library library)
    {
        Expect("starts out of select mode", false, view.SelectMode);

        view.ToggleSelectMode();
        Expect("enters select mode", true, view.SelectMode);
        Expect("nothing ticked initially", 0, view.Checked.Count);

        var first = view.Hits[0].Entry.Id;
        view.ToggleChecked(first);
        Expect("ticks one", 1, view.Checked.Count);
        view.ToggleChecked(first);
        Expect("unticks the same one", 0, view.Checked.Count);

        view.SelectAll();
        Expect("selects all on screen", view.Hits.Count, view.Checked.Count);

        // A tick must not survive being filtered off screen, or a bulk action would
        // hit words the user can no longer see.
        view.SearchBox.Text = "laconic";
        view.Refresh();
        Expect("filtered rows drop their tick", 1, view.Checked.Count);
        Expect("the surviving tick is the visible row", true,
            view.Checked.Contains(view.Hits[0].Entry.Id));

        view.SearchBox.Text = "";
        view.Refresh();

        view.SelectNone();
        Expect("clears the selection", 0, view.Checked.Count);

        // Deleting through the library must really remove the words.
        var before = library.Count;
        view.SelectAll();
        var doomed = view.Checked.ToArray();
        foreach (var id in doomed) library.DeleteEntry(id);
        view.Refresh();
        Expect("bulk delete removes them", before - doomed.Length, library.Count);
        Expect("selection empties after deletion", 0, view.Checked.Count);

        view.ToggleSelectMode();
        Expect("leaves select mode", false, view.SelectMode);
    }

    private static List<string> Headers(ContextMenu menu)
    {
        var headers = new List<string>();
        foreach (var item in menu.Items)
        {
            if (item is not MenuItem entry) continue;
            if (entry.Header is string header) headers.Add(header);
            foreach (var child in entry.Items)
                if (child is MenuItem nested && nested.Header is string nestedHeader) headers.Add(nestedHeader);
        }
        return headers;
    }

    private static MenuItem? FindItem(ContextMenu menu, string header)
    {
        foreach (var item in menu.Items)
        {
            if (item is not MenuItem entry) continue;
            if (Equals(entry.Header, header)) return entry;
            foreach (var child in entry.Items)
                if (child is MenuItem nested && Equals(nested.Header, header)) return nested;
        }
        return null;
    }

    private static void CheckSentencesView()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        var obfuscate = library.AddEntry("obfuscate");
        library.AddEntry("laconic");
        library.AddSentence("A loose one.");

        var view = new SentencesView(Context(db, library));
        Expect("lists the bank", 1, view.Rows.Count);

        var suggestions = view.SuggestLinks("He obfuscates the issue entirely.");
        Expect("suggests by inflection", 1, suggestions.Count);
        Expect("suggests the right word", obfuscate, suggestions[0].Id);

        view.Add("He obfuscates the issue entirely.", "Notebook");
        Expect("adds to the bank", 2, view.Rows.Count);
        Expect("auto-linked on a single match", 1, library.SentencesFor(obfuscate).Count);

        view.SearchBox.Text = "obfuscates";
        view.Refresh();
        Expect("search narrows", 1, view.Rows.Count);

        var linked = view.Rows.Single(r => r.EntryId is not null);
        var loose = library.AllSentences(looseOnly: true).Single();

        var linkedHeaders = Headers(view.RowMenu(linked));
        Expect("sentence menu edits", true, linkedHeaders.Contains("Edit..."));
        Expect("sentence menu unlinks", true, linkedHeaders.Contains("Unlink from its word"));
        Expect("sentence menu copies", true, linkedHeaders.Contains("Copy sentence"));
        Expect("sentence menu deletes", true, linkedHeaders.Contains("Delete"));
        Expect("linked sentence can open its word", true, FindItem(view.RowMenu(linked), "Open its word")?.IsEnabled);

        Expect("loose sentence offers linking", true, Headers(view.RowMenu(loose)).Contains("Link to a word"));
        Expect("loose sentence cannot open a word", false, FindItem(view.RowMenu(loose), "Open its word")?.IsEnabled);

        view.Dispose();
    }

    private static void CheckLookupView()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        var context = Context(db, library) with { Lookup = new FakeLookup() };

        var view = new LookupView(context);
        view.RunAsync("eminence").GetAwaiter().GetResult();

        Expect("result arrives", true, view.Result is not null);
        Expect("senses listed", 1, view.Result!.Senses.Count);
        Expect("everything ticked by default", 2, view.SelectedWords.Count);

        var id = view.Commit();
        Expect("entry created", "eminence", library.GetEntry(id)!.Headword);
        Expect("definition taken", "A position of superiority.", library.GetEntry(id)!.Definition);
        Expect("relations taken", 2, library.Relations(id).Count);
        Expect("example taken", 1, library.SentencesFor(id).Count);

        var offline = new LookupView(Context(db, library));
        offline.RunAsync("eminence").GetAwaiter().GetResult();
        Expect("offline handled", false, offline.Result!.Online);
    }

    private static void CheckQuizView()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        foreach (var word in new[] { "eminence", "laconic", "ersatz", "obdurate", "pellucid" })
        {
            var id = library.AddEntry(word);
            library.UpdateEntry(library.GetEntry(id)! with
            { PartOfSpeech = "adjective", Definition = $"Meaning of {word}." });
        }

        var context = Context(db, library);

        var view = new QuizView(context);
        view.Start(new ReviewScope(ScopeKind.All), QuizMode.Flip);

        Expect("session starts", true, view.Session is not null);
        Expect("starts hidden", false, view.IsRevealed);

        var first = view.Session!.Current!.Entry.Id;
        view.GradeCard(Grade.Good);
        Expect("blind grading ignored", 0, library.ReviewsFor(first).Count);

        view.Reveal();
        Expect("reveals", true, view.IsRevealed);
        view.GradeCard(Grade.Good);
        Expect("graded once revealed", 1, library.ReviewsFor(first).Count);
        Expect("advances", false, view.IsRevealed);

        // An auto-graded card preselects its grade; it is recorded only once the
        // grade is confirmed, so seeing the answer can still change it.
        var typed = new QuizView(context);
        typed.Start(new ReviewScope(ScopeKind.All), QuizMode.Typed);
        var target = typed.Session!.Current!.Entry;
        var before = library.ReviewsFor(target.Id).Count;
        typed.AnswerBox.Text = target.Headword;
        typed.SubmitTyped();
        Expect("exact answer suggests Good", Grade.Good, typed.SuggestedGrade);
        Expect("nothing recorded before confirming", before, library.ReviewsFor(target.Id).Count);
        typed.GradeCard(typed.SuggestedGrade);
        Expect("confirming records the suggestion", Grade.Good, library.ReviewsFor(target.Id)[^1].Grade);

        var nearly = new QuizView(context);
        nearly.Start(new ReviewScope(ScopeKind.All), QuizMode.Typed);
        var second = nearly.Session!.Current!.Entry;
        nearly.AnswerBox.Text = second.Headword[..^1] + "x";
        nearly.SubmitTyped();
        Expect("near miss suggests Hard", Grade.Hard, nearly.SuggestedGrade);
        nearly.GradeCard(Grade.Again);
        Expect("the suggestion can be overridden", Grade.Again, library.ReviewsFor(second.Id)[^1].Grade);

        var wrong = new QuizView(context);
        wrong.Start(new ReviewScope(ScopeKind.All), QuizMode.Typed);
        wrong.AnswerBox.Text = "completely different";
        wrong.SubmitTyped();
        Expect("a wrong answer suggests Again", Grade.Again, wrong.SuggestedGrade);

        var single = new QuizView(context);
        single.Receive(second.Id);
        Expect("single-word scope", 1, single.Session!.Total);
        Expect("single-word entry", second.Id, single.Session!.Current!.Entry.Id);
    }

    private static void CheckDecksView()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        var eminence = library.AddEntry("eminence");
        library.UpdateEntry(library.GetEntry(eminence)! with { PartOfSpeech = "noun" });
        var laconic = library.AddEntry("laconic");
        library.UpdateEntry(library.GetEntry(laconic)! with { PartOfSpeech = "adjective" });

        var context = Context(db, library);

        var manual = library.CreateDeck("Exam");
        library.AddToDeck(manual, eminence);

        var view = new DecksView(context);
        Expect("lists decks", 1, view.Decks.Count);
        Expect("manual members", 1, view.MembersOf(view.Decks[0]).Count);

        var libraryView = new LibraryView(context, "library");
        libraryView.SearchBox.Text = "laconic";
        libraryView.Refresh();
        var smart = libraryView.SaveFilterAsDeck("Smart");
        libraryView.Dispose();

        Expect("smart deck added", 2, view.Decks.Count);

        var smartDeck = view.Decks.Single(d => d.Id == smart);
        Expect("smart deck evaluates live", 1, view.MembersOf(smartDeck).Count);
        Expect("smart deck resolves", "laconic", view.MembersOf(smartDeck)[0].Headword);

        var manualDeck = view.Decks.Single(d => d.Id == manual);
        var headers = Headers(view.RowMenu(manualDeck, smart: false));
        Expect("deck menu opens", true, headers.Contains("Open"));
        Expect("deck menu renames", true, headers.Contains("Rename..."));
        Expect("deck menu quizzes", true, headers.Contains("Quiz this deck"));
        Expect("deck menu deletes", true, headers.Contains("Delete..."));

        // Renaming was reachable in Core but had no route through the interface.
        library.RenameDeck(manual, "Final exam");
        view.Refresh();
        Expect("rename lands", "Final exam", view.Decks.Single(d => d.Id == manual).Name);

        var emptyDeck = library.CreateDeck("Nothing in here");
        view.Refresh();
        Expect("an empty deck cannot be quizzed", false,
            FindItem(view.RowMenu(view.Decks.Single(d => d.Id == emptyDeck), false), "Quiz this deck")?.IsEnabled);

        view.Dispose();
    }

    private static void CheckStatsView()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        var id = library.AddEntry("eminence");
        library.AddReview(new Review(0, id, QuizMode.Flip, Grade.Good, true, true, 900, 5, 10, DateTime.UtcNow));

        var view = new StatsView(Context(db, library));
        Expect("overview words", 1, view.Overview.TotalWords);
        Expect("overview reviews today", 1, view.Overview.ReviewsToday);
        Expect("overview streak", 1, view.Overview.StreakDays);

        var heatmap = new Heatmap(new Stats(db).Heatmap(DateTime.UtcNow, weeks: 53));
        heatmap.Measure(new Size(800, 200));
        Expect("heatmap measures", true, heatmap.DesiredSize.Width > 0);

        view.Dispose();
    }

    private static void CheckSettingsView()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        var id = library.AddEntry("eminence");
        library.SaveScheduling(library.GetScheduling(id) with
        { IntervalDays = 7, Repetitions = 3, LastReviewedUtc = DateTime.UtcNow });

        var settings = new Settings(db);
        var context = Context(db, library, settings);

        var view = new SettingsView(context);
        var dueBefore = library.GetScheduling(id).DueUtc;

        view.ApplyScheduler(SchedulerKind.Leitner);

        Expect("scheduler stored", SchedulerKind.Leitner, settings.Scheduler);
        Expect("rows converted", SchedulerKind.Leitner, library.GetScheduling(id).Scheduler);
        Expect("box seeded from interval", 4, library.GetScheduling(id).Box);
        Expect("due date preserved", dueBefore, library.GetScheduling(id).DueUtc);

        var path = Path.Combine(Path.GetTempPath(), $"swl-check-{Guid.NewGuid():N}.json");
        try
        {
            view.ExportTo(path, "json");
            Expect("export writes", true, File.Exists(path));

            var targetDb = Db.OpenMemory();
            var target = new Library(targetDb);
            var report = new SettingsView(Context(targetDb, target)).ImportFrom(path, MergePolicy.TakeTheirs);
            Expect("import reads", 1, report.Added);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void CheckCommandPalette()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        library.AddEntry("eminence");
        library.CreateDeck("Exam");

        var navigated = "";
        var context = Context(db, library) with { Navigate = (id, _) => navigated = id };

        var window = new MainWindow(library, new Settings(db), new Search(db), new Stats(db), new StubLookup());
        var entries = CommandPalette.Build(context, window.Nav);

        Expect("palette has views", true, entries.Any(e => e.Label == "All words"));
        Expect("palette has entries", true, entries.Any(e => e.Label == "eminence"));
        Expect("palette has decks", true, entries.Any(e => e.Label == "Exam"));
        Expect("palette has verbs", true, entries.Any(e => e.Label.Contains("Export")));

        var filtered = CommandPalette.Filter(entries, "emin");
        Expect("filter narrows", "eminence", filtered[0].Label);

        filtered[0].Run();
        Expect("running navigates", "library", navigated);

        Expect("nonsense matches nothing", 0, CommandPalette.Filter(entries, "zzqqxx").Count);

        window.Close();
    }

    // ---- helpers -------------------------------------------------------

    private static ShellContext Context(Db db, Library library, Settings? settings = null) => new(
        library, settings ?? new Settings(db), new Search(db), new Stats(db),
        new StubLookup(), _ => { }, (_, _) => { });

    private sealed class StubLookup : ILookup
    {
        public Task<LookupResult> LookupAsync(string word, CancellationToken ct) =>
            Task.FromResult(Lookup.Offline(word, "Checks run offline."));
    }

    private sealed class FakeLookup : ILookup
    {
        public Task<LookupResult> LookupAsync(string word, CancellationToken ct) => Task.FromResult(
            new LookupResult(word, "/ˈɛmɪnəns/", "https://example.invalid/e.mp3",
                [new LookupSense("noun", "A position of superiority.", ["She rose to eminence."])],
                new Dictionary<RelationKind, IReadOnlyList<string>>
                {
                    [RelationKind.Synonym] = ["prominence"],
                    [RelationKind.Antonym] = ["obscurity"],
                    [RelationKind.Related] = [],
                    [RelationKind.Rhyme] = [],
                },
                Online: true, Message: null));
    }

    private static string Hex(IBrush brush) => ((ISolidColorBrush)brush).Color.ToString().ToUpperInvariant();

    private static void Expect<T>(string name, T expected, T actual)
    {
        if (Equals(expected, actual)) { _passes++; return; }
        Fail($"{name}: expected {expected}, got {actual}");
    }

    private static void Fail(string message)
    {
        Console.WriteLine($"  FAIL  {message}");
        _failures++;
    }
}

internal sealed class HeadlessApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
    }
}
