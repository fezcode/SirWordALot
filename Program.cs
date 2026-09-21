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

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
          Console     Type commands against the real library
          Settings    Palette, scheduler, daily caps, capture hotkey, backups

        Keys
          Ctrl+Space  Command palette          /   Focus search
          Ctrl+Alt+W  Capture clipboard word   N   New word
          Q           Start a quiz             Space  Flip the card
          1-4         Grade Again/Hard/Good/Easy     Esc  Back

        Data lives in %APPDATA%\fezcode\SirWordALot.
        """;
}
