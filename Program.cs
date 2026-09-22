using Avalonia;
using SirWordALot.Core;

namespace SirWordALot;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--version") || args.Contains("-v"))
        {
            Console.WriteLine($"{Product.Name} {Product.Version}");
            return;
        }

        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine(Help);
            return;
        }

        if (args.Contains("--selftest"))
        {
            Environment.ExitCode = SelfTest();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Exercises the SQLite stack against a throwaway database and exits.
    /// Proves the native provider resolves inside a single-file bundle, which
    /// <c>dotnet run</c> cannot tell you, without opening a window.</summary>
    private static int SelfTest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"swl-selftest-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = Db.Open(path))
            {
                var library = new Library(db);
                var id = library.AddEntry("eminence");
                library.AddRelation(id, RelationKind.Synonym, "renown");
                library.AddSentence("She rose to eminence.", id);

                var hits = new Search(db).Run(new SearchFilter(Text: "eminence"), DateTime.UtcNow);
                if (hits.Count != 1) { Console.WriteLine("Self-test FAILED: search returned nothing."); return 1; }

                var json = Portability.ExportJson(library);
                if (!json.Contains("eminence")) { Console.WriteLine("Self-test FAILED: export was empty."); return 1; }
            }

            // Embedded resources resolve differently inside a single-file bundle, so
            // the published binary is the only place this is really proven.
            var dictionary = OfflineDictionary.Instance;
            if (!dictionary.IsAvailable || dictionary.Translate("book").Count == 0)
            {
                Console.WriteLine("Self-test FAILED: the offline dictionary did not load.");
                return 1;
            }

            Console.WriteLine($"Self-test passed. SQLite is working and " +
                              $"{dictionary.Count:N0} offline headwords loaded.");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Self-test FAILED: {e.GetType().Name}: {e.Message}");
            return 1;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var stray in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*"))
                try { File.Delete(stray); } catch (IOException) { }
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();

    private const string Help = """
        Sir Word-a-lot - a vocabulary and thesaurus trainer

          -v, --version   Print version and exit
          -h, --help      Show this help and exit

        Views
          Library     Browse, search, filter and edit every word
          Sentences   The sentence bank; link loose sentences to words
          Lookup      Fetch synonyms, senses and examples online
          Quiz        Flip, choice, cloze and typed drills over SM-2 or Leitner
          Decks       Manual decks and smart decks saved from a filter
          Stats       Streak, retention, review heatmap and mastery breakdown
          Settings    Palette, scheduler, daily caps, capture hotkey, backups

        Keys
          Ctrl+Space  Command palette          /   Focus search
          Ctrl+Alt+W  Capture clipboard word   N   New word
          Q           Start a quiz             Space  Flip the card
          1-4         Grade Again/Hard/Good/Easy     Esc  Back

        Data lives in %APPDATA%\fezcode\SirWordALot.
        """;
}
