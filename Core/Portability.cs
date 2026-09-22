using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SirWordALot.Core;

public enum MergePolicy { KeepMine, TakeTheirs, KeepBoth }

public sealed record ImportReport(int Added, int Updated, int Skipped, IReadOnlyList<string> Conflicts);

/// <summary>JSON is the lossless format and the backup format. CSV and Anki TSV
/// are deliberately lossy exchange formats.</summary>
public static class Portability
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] CsvHeader =
    [
        "headword", "pronunciation", "part_of_speech", "definition", "notes",
        "starred", "tags", "synonyms", "antonyms", "related", "sentences"
    ];

    // ---- JSON ----------------------------------------------------------

    public sealed record SentenceDto(string Text, string? Source, int[]? Cloze);

    public sealed record SchedulingDto(
        string Scheduler, int Box, double Ease, int IntervalDays,
        int Repetitions, int Lapses, string DueUtc, string? LastReviewedUtc);

    public sealed record ReviewDto(
        string Mode, int Grade, bool Correct, bool Scheduled,
        int ElapsedMs, int IntervalBefore, int IntervalAfter, string ReviewedUtc);

    public sealed record EntryDto(
        string Headword, string? Pronunciation, string? AudioUrl, string? PartOfSpeech,
        string? Definition, string? Notes, bool Starred, bool Archived, string Source,
        string CreatedUtc, string? Translation, List<string>? Tags, Dictionary<string, List<string>>? Relations,
        List<SentenceDto>? Sentences, SchedulingDto? Scheduling, List<ReviewDto>? Reviews);

    public sealed record DeckDto(string Name, string? Description, string? FilterJson, List<string>? Entries);

    public sealed record Document(
        int Version, string ExportedUtc, List<EntryDto>? Entries,
        List<SentenceDto>? LooseSentences, List<DeckDto>? Decks);

    public static string ExportJson(Library library)
    {
        var entries = library.AllEntries(includeArchived: true).Select(entry =>
        {
            var relations = library.Relations(entry.Id)
                .GroupBy(r => r.Kind.ToColumn())
                .ToDictionary(g => g.Key, g => g.Select(r => r.Word).ToList());

            var scheduling = library.GetScheduling(entry.Id);

            return new EntryDto(
                entry.Headword, entry.Pronunciation, entry.AudioUrl, entry.PartOfSpeech,
                entry.Definition, entry.Notes, entry.Starred, entry.Archived, entry.Source,
                Iso.Text(entry.CreatedUtc), entry.Translation,
                library.TagsFor(entry.Id).ToList(),
                relations,
                library.SentencesFor(entry.Id).Select(ToDto).ToList(),
                new SchedulingDto(scheduling.Scheduler.ToColumn(), scheduling.Box, scheduling.Ease,
                    scheduling.IntervalDays, scheduling.Repetitions, scheduling.Lapses,
                    Iso.Text(scheduling.DueUtc),
                    scheduling.LastReviewedUtc is { } last ? Iso.Text(last) : null),
                library.ReviewsFor(entry.Id).Select(r => new ReviewDto(
                    r.Mode.ToColumn(), (int)r.Grade, r.Correct, r.Scheduled,
                    r.ElapsedMs, r.IntervalBefore, r.IntervalAfter, Iso.Text(r.ReviewedUtc))).ToList());
        }).ToList();

        var decks = library.Decks().Select(deck => new DeckDto(
            deck.Name, deck.Description, deck.FilterJson,
            library.DeckEntries(deck.Id).Select(e => e.Headword).ToList())).ToList();

        var document = new Document(
            FormatVersion, Iso.Text(DateTime.UtcNow), entries,
            library.AllSentences(looseOnly: true).Select(ToDto).ToList(), decks);

        return JsonSerializer.Serialize(document, Json);

        static SentenceDto ToDto(Sentence s) => new(s.Text, s.Source,
            s.ClozeStart is { } start && s.ClozeLength is { } length ? [start, length] : null);
    }

    public static ImportReport ImportJson(Library library, string json, MergePolicy policy)
    {
        Document? document;
        try { document = JsonSerializer.Deserialize<Document>(json, Json); }
        catch (JsonException e) { throw new InvalidDataException("This file is not a valid export.", e); }

        if (document is null) throw new InvalidDataException("This file is empty.");
        if (document.Version > FormatVersion)
            throw new InvalidDataException(
                $"This export was written by a newer version (format {document.Version}).");

        var added = 0; var updated = 0; var skipped = 0;
        var conflicts = new List<string>();

        foreach (var dto in document.Entries ?? [])
        {
            var existing = library.FindEntry(dto.Headword);
            var headword = dto.Headword;

            if (existing is not null)
            {
                conflicts.Add(dto.Headword);
                switch (policy)
                {
                    case MergePolicy.KeepMine:
                        skipped++;
                        continue;
                    case MergePolicy.KeepBoth:
                        headword = UniqueHeadword(library, dto.Headword);
                        existing = null;
                        break;
                }
            }

            var id = existing?.Id ?? library.AddEntry(headword, dto.Source ?? "import");
            if (existing is null) added++; else updated++;

            library.UpdateEntry(library.GetEntry(id)! with
            {
                Headword = headword,
                Pronunciation = dto.Pronunciation,
                AudioUrl = dto.AudioUrl,
                PartOfSpeech = dto.PartOfSpeech,
                Definition = dto.Definition,
                Notes = dto.Notes,
                Starred = dto.Starred,
                Archived = dto.Archived,
                Translation = dto.Translation,
            });

            foreach (var tag in dto.Tags ?? []) library.TagEntry(id, tag);

            foreach (var (kind, words) in dto.Relations ?? new Dictionary<string, List<string>>())
                foreach (var word in words)
                    library.AddRelation(id, Enums.RelationKindFrom(kind), word, "import");

            foreach (var sentence in dto.Sentences ?? [])
            {
                var sentenceId = library.AddSentence(sentence.Text, id, sentence.Source);
                if (sentence.Cloze is [var start, var length])
                    library.UpdateSentence(library.SentencesFor(id).Single(s => s.Id == sentenceId)
                        with { ClozeStart = start, ClozeLength = length });
            }

            if (dto.Scheduling is { } s)
                library.SaveScheduling(new Scheduling(id, Enums.SchedulerKindFrom(s.Scheduler),
                    s.Box, s.Ease, s.IntervalDays, s.Repetitions, s.Lapses,
                    Iso.Parse(s.DueUtc), Iso.ParseOrNull(s.LastReviewedUtc)));

            foreach (var r in dto.Reviews ?? [])
                library.AddReview(new Review(0, id, Enums.QuizModeFrom(r.Mode), (Grade)r.Grade,
                    r.Correct, r.Scheduled, r.ElapsedMs, r.IntervalBefore, r.IntervalAfter,
                    Iso.Parse(r.ReviewedUtc)));
        }

        foreach (var loose in document.LooseSentences ?? [])
            library.AddSentence(loose.Text, null, loose.Source);

        foreach (var deck in document.Decks ?? [])
        {
            if (library.Decks().Any(d => d.Name == deck.Name)) continue;
            var deckId = library.CreateDeck(deck.Name, deck.Description, deck.FilterJson);
            foreach (var headword in deck.Entries ?? [])
                if (library.FindEntry(headword) is { } entry) library.AddToDeck(deckId, entry.Id);
        }

        return new ImportReport(added, updated, skipped, conflicts);
    }

    private static string UniqueHeadword(Library library, string headword)
    {
        var candidate = $"{headword} (imported)";
        var n = 2;
        while (library.FindEntry(candidate) is not null) candidate = $"{headword} (imported {n++})";
        return candidate;
    }

    // ---- CSV -----------------------------------------------------------

    public static string ExportCsv(Library library)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', CsvHeader)).Append('\n');

        foreach (var entry in library.AllEntries(includeArchived: true))
        {
            var relations = library.Relations(entry.Id);
            string Words(RelationKind kind) =>
                string.Join(';', relations.Where(r => r.Kind == kind).Select(r => r.Word));

            builder.Append(string.Join(',', new[]
            {
                Quote(entry.Headword), Quote(entry.Pronunciation), Quote(entry.PartOfSpeech),
                Quote(entry.Definition), Quote(entry.Notes), entry.Starred ? "true" : "false",
                Quote(string.Join(';', library.TagsFor(entry.Id))),
                Quote(Words(RelationKind.Synonym)), Quote(Words(RelationKind.Antonym)),
                Quote(Words(RelationKind.Related)),
                Quote(string.Join(" | ", library.SentencesFor(entry.Id).Select(s => s.Text))),
            })).Append('\n');
        }

        return builder.ToString();
    }

    public static ImportReport ImportCsv(Library library, string csv, MergePolicy policy)
    {
        var rows = ParseCsv(csv);
        if (rows.Count > 0 && rows[0].Count > 0 &&
            string.Equals(rows[0][0].Trim(), "headword", StringComparison.OrdinalIgnoreCase))
            rows.RemoveAt(0);

        var added = 0; var updated = 0; var skipped = 0;
        var conflicts = new List<string>();

        foreach (var row in rows)
        {
            if (row.Count == 0) continue;
            var headword = Field(row, 0).Trim();
            if (headword.Length == 0) continue;

            var existing = library.FindEntry(headword);
            if (existing is not null)
            {
                conflicts.Add(headword);
                if (policy == MergePolicy.KeepMine) { skipped++; continue; }
                if (policy == MergePolicy.KeepBoth) { headword = UniqueHeadword(library, headword); existing = null; }
            }

            var id = existing?.Id ?? library.AddEntry(headword, "import");
            if (existing is null) added++; else updated++;

            library.UpdateEntry(library.GetEntry(id)! with
            {
                Headword = headword,
                Pronunciation = Blank(Field(row, 1)),
                PartOfSpeech = Blank(Field(row, 2)),
                Definition = Blank(Field(row, 3)),
                Notes = Blank(Field(row, 4)),
                Starred = bool.TryParse(Field(row, 5), out var starred) && starred,
            });

            foreach (var tag in Split(Field(row, 6))) library.TagEntry(id, tag);
            foreach (var word in Split(Field(row, 7))) library.AddRelation(id, RelationKind.Synonym, word, "import");
            foreach (var word in Split(Field(row, 8))) library.AddRelation(id, RelationKind.Antonym, word, "import");
            foreach (var word in Split(Field(row, 9))) library.AddRelation(id, RelationKind.Related, word, "import");

            foreach (var text in Field(row, 10).Split('|', StringSplitOptions.RemoveEmptyEntries))
                if (text.Trim().Length > 0) library.AddSentence(text.Trim(), id);
        }

        return new ImportReport(added, updated, skipped, conflicts);

        static string Field(List<string> row, int index) => index < row.Count ? row[index] : "";
        static string? Blank(string value) => value.Trim().Length == 0 ? null : value.Trim();
        static IEnumerable<string> Split(string value) =>
            value.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim())
                 .Where(v => v.Length > 0);
    }

    private static string Quote(string? value)
    {
        value ??= "";
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    /// <summary>RFC 4180 reader: handles quoted fields containing commas,
    /// doubled quotes and embedded newlines.</summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\n')
            {
                row.Add(field.ToString()); field.Clear();
                if (row.Any(f => f.Length > 0)) rows.Add(row);
                row = [];
            }
            else if (c != '\r') field.Append(c);
        }

        row.Add(field.ToString());
        if (row.Any(f => f.Length > 0)) rows.Add(row);
        return rows;
    }

    // ---- Anki ----------------------------------------------------------

    public static string ExportAnkiTsv(Library library)
    {
        var builder = new StringBuilder();

        foreach (var entry in library.AllEntries())
        {
            var synonyms = library.Relations(entry.Id)
                .Where(r => r.Kind == RelationKind.Synonym).Select(r => r.Word).ToList();

            var back = entry.Definition ?? "";
            if (synonyms.Count > 0) back += (back.Length > 0 ? " — " : "") + string.Join(", ", synonyms);

            builder.Append(Flatten(entry.Headword)).Append('\t')
                   .Append(Flatten(back)).Append('\t')
                   .Append(Flatten(string.Join(' ', library.TagsFor(entry.Id)))).Append('\n');
        }

        return builder.ToString();

        static string Flatten(string value) =>
            value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
