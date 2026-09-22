using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Preview;

/// <summary>Renders every view to PNG using the production controls, so the result
/// can be measured. Nothing is shown on the real desktop.</summary>
internal static class Program
{
    private static readonly string[] Views =
        ["library", "sentences", "lookup", "quiz", "decks", "stats", "settings"];

    [STAThread]
    public static int Main(string[] args)
    {
        var folder = args.FirstOrDefault() ?? "dist/preview";
        Directory.CreateDirectory(folder);

        AppBuilder.Configure<PreviewApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .WithInterFont()
            .SetupWithoutStarting();

        // Motion animates opacity from 0 through a DispatcherTimer. Nothing pumps the
        // dispatcher here, so leaving it on renders every view fully transparent.
        Motion.Enabled = false;

        var db = Db.OpenMemory();
        var library = Seed(db);

        // The hotkey cannot register against a headless window, and its failure toast
        // would sit on top of every preview.
        var settings = new Settings(db) { CaptureEnabled = false };

        var window = new MainWindow(library, settings, new Search(db), new Stats(db), new StubLookup());
        window.Show();

        foreach (var view in Views)
        {
            window.Navigate(view);
            Render(window, Path.Combine(folder, $"{view}.png"));
            Console.WriteLine($"  wrote {view}.png");
        }

        // The detail pane is the screen the app is really about, and the plain
        // library preview never shows it.
        window.Navigate("library", library.FindEntry("eminence")!.Id);
        Render(window, Path.Combine(folder, "entry.png"));
        Console.WriteLine("  wrote entry.png");

        // Several sizes so the .ico is crisp in the taskbar rather than a
        // downscaled 256. The border thins and the mark grows as size drops,
        // because a 6px frame at 16px would swallow the glyph.
        foreach (var size in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            RenderIcon(Path.Combine(folder, $"icon-{size}.png"), size);
            Console.WriteLine($"  wrote icon-{size}.png");
        }

        RenderIcon(Path.Combine(folder, "icon.png"), 256);
        Console.WriteLine("  wrote icon.png");

        window.Close();
        Console.WriteLine($"Previews written to {Path.GetFullPath(folder)}.");
        return 0;
    }

    private static void Render(Window window, string path)
    {
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();

        var target = new RenderTargetBitmap(
            new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        target.Render(window);

        using var stream = File.Create(path);
        target.Save(stream);
    }

    private static void RenderIcon(string path, int size)
    {
        var border = size <= 32 ? 0 : Math.Max(1.0, size / 42.0);
        var mark = size * (size <= 32 ? 0.88 : 0.67);

        var panel = new Border
        {
            Width = size,
            Height = size,
            Background = Ui.Cream,
            BorderBrush = Ui.WindowBorder,
            BorderThickness = new Thickness(border),
            Child = new Glyph("crest") { Width = mark, Height = mark },
        };

        panel.Measure(new Size(size, size));
        panel.Arrange(new Rect(0, 0, size, size));
        panel.UpdateLayout();

        var target = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        target.Render(panel);

        using var stream = File.Create(path);
        target.Save(stream);
    }

    private static Library Seed(Db db)
    {
        var library = new Library(db);
        var now = DateTime.UtcNow;

        var words = new (string Word, string Pos, string Definition, string Sentence, string[] Synonyms)[]
        {
            ("eminence", "noun", "A position of superiority or fame.",
             "She rose to eminence after the war.", ["prominence", "renown", "standing"]),
            ("laconic", "adjective", "Using very few words.",
             "His laconic reply ended the meeting.", ["terse", "curt", "concise"]),
            ("ersatz", "adjective", "An inferior substitute.",
             "They served an ersatz coffee of roasted barley.", ["artificial", "imitation"]),
            ("obdurate", "adjective", "Stubbornly refusing to change.",
             "The committee remained obdurate.", ["unyielding", "inflexible"]),
            ("pellucid", "adjective", "Translucently clear.",
             "A pellucid stream ran past the mill.", ["limpid", "lucid"]),
            ("quixotic", "adjective", "Idealistic to the point of impracticality.",
             "A quixotic plan to rebuild the pier.", ["idealistic", "romantic"]),
        };

        var deck = library.CreateDeck("Examination", "For Tuesday");

        for (var i = 0; i < words.Length; i++)
        {
            var (word, pos, definition, sentence, synonyms) = words[i];
            var id = library.AddEntry(word, "lookup");

            library.UpdateEntry(library.GetEntry(id)! with
            {
                PartOfSpeech = pos,
                Definition = definition,
                Pronunciation = "/ˈ" + word[..Math.Min(4, word.Length)] + "/",
                Starred = i % 3 == 0,
            });

            foreach (var synonym in synonyms) library.AddRelation(id, RelationKind.Synonym, synonym, "lookup");
            library.AddRelation(id, RelationKind.Antonym, i % 2 == 0 ? "obscurity" : "verbose", "lookup");

            var sentenceId = library.AddSentence(sentence, id, "Reading notes");
            if (Cloze.Find(sentence, word) is { } span)
                library.UpdateSentence(library.SentencesFor(id).Single(s => s.Id == sentenceId)
                    with { ClozeStart = span.Start, ClozeLength = span.Length });

            library.TagEntry(id, i % 2 == 0 ? "latinate" : "reading");
            if (i < 3) library.AddToDeck(deck, id);

            var interval = new[] { 1, 6, 15, 40, 95, 3 }[i];
            library.SaveScheduling(library.GetScheduling(id) with
            {
                IntervalDays = interval,
                Repetitions = i + 1,
                LastReviewedUtc = now.AddDays(-1),
                DueUtc = now.AddDays(i % 2 == 0 ? -1 : interval),
            });

            for (var r = 0; r < 4 + i; r++)
                library.AddReview(new Review(0, id, QuizMode.Flip, Grade.Good, r % 5 != 0, true,
                    1200, Math.Max(1, interval - r), interval, now.AddDays(-(r % 30))));
        }

        library.AddSentence("A sentence found in the wild, not yet attached to anything.");
        return library;
    }

    private sealed class StubLookup : ILookup
    {
        public Task<LookupResult> LookupAsync(string word, CancellationToken ct) =>
            Task.FromResult(Lookup.Offline(word, "Previews are rendered offline."));
    }
}

internal sealed class PreviewApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://SirWordALot.Preview/"))
        {
            Source = new Uri("avares://SirWordALot.Preview/PreviewStyles.axaml"),
        });
    }
}
