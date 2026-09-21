namespace SirWordALot.Core;

/// <summary>The Department shell. Every command acts on the real library —
/// nothing here is simulated.</summary>
public sealed class ConsoleCommands(Library library, Search search, Stats stats, Settings settings)
{
    /// <summary>Sentinel returned by <c>clear</c>; the view wipes its scrollback.</summary>
    public const string ClearSignal = "\u0000clear";

    public IReadOnlyList<string> Names =>
        ["add", "find", "show", "sentence", "tag", "untag", "star", "due", "stats",
         "export", "import", "backup", "help", "clear"];

    public IReadOnlyList<string> Run(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return [];

        var space = trimmed.IndexOf(' ');
        var command = (space < 0 ? trimmed : trimmed[..space]).ToLowerInvariant();
        var rest = space < 0 ? "" : trimmed[(space + 1)..].Trim();

        return command switch
        {
            "help" => Help(),
            "clear" => [ClearSignal],
            "add" => Add(rest),
            "find" => Find(rest),
            "show" => Show(rest),
            "sentence" => AddSentence(rest),
            "tag" => Tag(rest, attach: true),
            "untag" => Tag(rest, attach: false),
            "star" => Star(rest),
            "due" => Due(),
            "stats" => Summary(),
            "export" => Export(rest),
            "import" => Import(rest),
            "backup" => Backup(),
            _ => [$"Unknown command '{command}'. Type help to begin."],
        };
    }

    private IReadOnlyList<string> Help() =>
    [
        "SIR WORD-A-LOT  ·  DEPARTMENT SHELL",
        "",
        "  add <word>                    Add a word to the library",
        "  find <query>                  Search words, definitions and sentences",
        "  show <word>                   Print everything held about a word",
        "  sentence <word> <text>        Attach an example sentence",
        "  tag <word> <tag>              Tag a word",
        "  untag <word> <tag>            Remove a tag",
        "  star <word>                   Toggle the star on a word",
        "  due                           List what is due now",
        "  stats                         Print the overview",
        "  export <json|csv|tsv> <path>  Write the library to a file",
        "  import <path>                 Read a JSON export back in",
        "  backup                        Write a timestamped snapshot",
        "  help                          Show this text",
        "  clear                         Clear the screen",
    ];

    private IReadOnlyList<string> Add(string word)
    {
        if (word.Length == 0) return ["Usage: add <word>"];
        if (library.FindEntry(word) is not null) return [$"'{word}' is already in the library."];
        library.AddEntry(word);
        return [$"Added '{word}'. It is due immediately."];
    }

    private IReadOnlyList<string> Find(string query)
    {
        if (query.Length == 0) return ["Usage: find <query>"];

        var hits = search.Run(new SearchFilter(Text: query), DateTime.UtcNow);
        if (hits.Count == 0) return ["No matches."];

        return hits.Take(20).Select(hit =>
            $"  {hit.Entry.Headword,-24} {hit.Entry.PartOfSpeech ?? "—",-12} {hit.MasteryPercent,3}%")
            .Prepend($"{hits.Count} match(es):").ToList();
    }

    private IReadOnlyList<string> Show(string word)
    {
        if (word.Length == 0) return ["Usage: show <word>"];
        if (library.FindEntry(word) is not { } entry) return [$"'{word}' is not in the library."];

        var scheduling = library.GetScheduling(entry.Id);
        var reviews = library.ReviewsFor(entry.Id);
        var lines = new List<string>
        {
            $"{entry.Headword}  {entry.Pronunciation ?? ""}  ·  {entry.PartOfSpeech ?? "—"}",
            "",
        };

        if (entry.Definition is { Length: > 0 }) lines.Add($"  {entry.Definition}");

        foreach (var kind in new[] { RelationKind.Synonym, RelationKind.Antonym, RelationKind.Related })
        {
            var words = library.Relations(entry.Id).Where(r => r.Kind == kind).Select(r => r.Word).ToList();
            if (words.Count > 0) lines.Add($"  {kind.ToColumn(),-9} {string.Join(", ", words)}");
        }

        foreach (var sentence in library.SentencesFor(entry.Id)) lines.Add($"  \"{sentence.Text}\"");

        var tags = library.TagsFor(entry.Id);
        if (tags.Count > 0) lines.Add($"  tags      {string.Join(", ", tags)}");

        lines.Add("");
        lines.Add($"  due {scheduling.DueUtc:yyyy-MM-dd}  ·  interval {scheduling.IntervalDays} d  " +
                  $"·  reps {scheduling.Repetitions}  ·  lapses {scheduling.Lapses}  ·  {reviews.Count} review(s)");
        return lines;
    }

    private IReadOnlyList<string> AddSentence(string rest)
    {
        var space = rest.IndexOf(' ');
        if (space < 0) return ["Usage: sentence <word> <text>"];

        var word = rest[..space];
        var text = rest[(space + 1)..].Trim();
        if (text.Length == 0) return ["Usage: sentence <word> <text>"];
        if (library.FindEntry(word) is not { } entry) return [$"'{word}' is not in the library."];

        library.AddSentence(text, entry.Id);
        return [$"Sentence attached to '{entry.Headword}'."];
    }

    private IReadOnlyList<string> Tag(string rest, bool attach)
    {
        var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return [$"Usage: {(attach ? "tag" : "untag")} <word> <tag>"];
        if (library.FindEntry(parts[0]) is not { } entry) return [$"'{parts[0]}' is not in the library."];

        if (attach) library.TagEntry(entry.Id, parts[1]);
        else library.UntagEntry(entry.Id, parts[1]);

        return [$"{(attach ? "Tagged" : "Untagged")} '{entry.Headword}' with '{parts[1].ToLowerInvariant()}'."];
    }

    private IReadOnlyList<string> Star(string word)
    {
        if (word.Length == 0) return ["Usage: star <word>"];
        if (library.FindEntry(word) is not { } entry) return [$"'{word}' is not in the library."];

        library.SetStarred(entry.Id, !entry.Starred);
        return [$"'{entry.Headword}' is {(entry.Starred ? "no longer starred" : "starred")}."];
    }

    private IReadOnlyList<string> Due()
    {
        var now = DateTime.UtcNow;
        var due = library.DueEntries(now).Concat(library.NewEntries(settings.NewPerDay)).ToList();
        if (due.Count == 0) return ["Nothing is due. Enjoy the quiet."];

        return due.Take(30).Select(e => $"  {e.Headword}")
            .Prepend($"{due.Count} due:").ToList();
    }

    private IReadOnlyList<string> Summary()
    {
        var overview = stats.Summary(DateTime.UtcNow);
        return
        [
            $"  words       {overview.TotalWords}",
            $"  due today   {overview.DueToday}",
            $"  mastered    {overview.Mastered}",
            $"  streak      {overview.StreakDays} day(s)",
            $"  retention   {overview.Retention:P1}",
            $"  today       {overview.ReviewsToday} / {settings.DailyGoal}",
        ];
    }

    private IReadOnlyList<string> Export(string rest)
    {
        var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return ["Usage: export <json|csv|tsv> <path>"];

        var content = parts[0].ToLowerInvariant() switch
        {
            "json" => Portability.ExportJson(library),
            "csv" => Portability.ExportCsv(library),
            "tsv" => Portability.ExportAnkiTsv(library),
            _ => null,
        };

        if (content is null) return ["Format must be json, csv or tsv."];

        try
        {
            Backups.WriteAtomic(parts[1], content);
            return [$"Wrote {content.Length} characters to {parts[1]}."];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [$"Could not write that file: {e.Message}"];
        }
    }

    private IReadOnlyList<string> Import(string path)
    {
        if (path.Length == 0) return ["Usage: import <path>"];
        if (!File.Exists(path)) return [$"No file at {path}."];

        try
        {
            var report = Portability.ImportJson(library, File.ReadAllText(path), MergePolicy.TakeTheirs);
            return [$"Added {report.Added}, updated {report.Updated}, skipped {report.Skipped}."];
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return [$"Could not import that file: {e.Message}"];
        }
    }

    private IReadOnlyList<string> Backup()
    {
        var backups = new Backups(settings.BackupFolder);
        var path = backups.Write(library, DateTime.UtcNow);
        backups.Prune();
        return [$"Snapshot written to {path}."];
    }
}
