namespace SirWordALot.Core;

public sealed record SearchFilter(
    string? Text = null,
    string? PartOfSpeech = null,
    string? Tag = null,
    MasteryBucket? Mastery = null,
    DueState Due = DueState.Any,
    DateTime? AddedAfter = null,
    bool IncludeArchived = false,
    bool StarredOnly = false,
    long? DeckId = null,
    SortOrder Sort = SortOrder.Recent);

public sealed record SearchHit(
    Entry Entry,
    int MasteryPercent,
    MasteryBucket Bucket,
    DateTime DueUtc,
    string? MatchedSentence);

/// <summary>Structured filtering plus FTS5 text matching over headwords,
/// definitions, notes and sentences.</summary>
public sealed class Search(Db db)
{
    public IReadOnlyList<SearchHit> Run(SearchFilter filter, DateTime nowUtc)
    {
        var where = new List<string>();
        var args = new List<(string, object?)>();

        if (!filter.IncludeArchived) where.Add("e.archived = 0");
        if (filter.StarredOnly) where.Add("e.starred = 1");

        if (!string.IsNullOrWhiteSpace(filter.PartOfSpeech))
        {
            where.Add("e.part_of_speech = $pos");
            args.Add(("$pos", filter.PartOfSpeech));
        }

        if (!string.IsNullOrWhiteSpace(filter.Tag))
        {
            where.Add("""
                e.id IN (SELECT et.entry_id FROM entry_tags et
                         JOIN tags t ON t.id = et.tag_id WHERE t.name = $tag)
                """);
            args.Add(("$tag", filter.Tag!.Trim().ToLowerInvariant()));
        }

        if (filter.DeckId is { } deckId)
        {
            where.Add("e.id IN (SELECT entry_id FROM deck_entries WHERE deck_id = $deck)");
            args.Add(("$deck", deckId));
        }

        if (filter.AddedAfter is { } after)
        {
            where.Add("e.created_utc >= $after");
            args.Add(("$after", Iso.Text(after)));
        }

        switch (filter.Due)
        {
            case DueState.DueNow:
                where.Add("s.due_utc <= $now");
                args.Add(("$now", Iso.Text(nowUtc)));
                break;
            case DueState.NotDue:
                where.Add("s.due_utc > $now");
                args.Add(("$now", Iso.Text(nowUtc)));
                break;
            case DueState.Never:
                where.Add("s.last_reviewed_utc IS NULL");
                break;
        }

        var matched = new Dictionary<long, string>();
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var fts = ToFts(filter.Text!);
            if (fts.Length == 0) return [];

            var entryIds = db.Query(
                "SELECT rowid FROM entries_fts WHERE entries_fts MATCH $q",
                r => r.GetInt64(0), ("$q", fts)).ToHashSet();

            foreach (var (sentenceEntryId, text) in db.Query(
                """
                SELECT s.entry_id, s.text FROM sentences_fts f
                JOIN sentences s ON s.id = f.rowid
                WHERE sentences_fts MATCH $q AND s.entry_id IS NOT NULL
                """,
                r => (r.GetInt64(0), r.GetString(1)), ("$q", fts)))
            {
                entryIds.Add(sentenceEntryId);
                matched.TryAdd(sentenceEntryId, text);
            }

            if (entryIds.Count == 0) return [];
            where.Add($"e.id IN ({string.Join(',', entryIds)})");
        }

        var sql = $"""
            SELECT e.id, e.headword, e.normalized, e.pronunciation, e.audio_url, e.part_of_speech,
                   e.definition, e.notes, e.starred, e.archived, e.source, e.created_utc, e.modified_utc,
                   e.translation,
                   s.interval_days, s.due_utc,
                   (SELECT COUNT(*) FROM reviews r WHERE r.entry_id = e.id),
                   (SELECT COUNT(*) FROM reviews r WHERE r.entry_id = e.id AND r.correct = 1)
            FROM entries e
            JOIN scheduling s ON s.entry_id = e.id
            {(where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where))}
            """;

        var hits = db.Query(sql, r =>
        {
            // Library.ReadEntry consumes columns 0-13; the aggregates follow it.
            var entry = Library.ReadEntry(r);
            var interval = r.GetInt32(14);
            var due = Iso.Parse(r.GetString(15));
            var total = r.GetInt32(16);
            var correct = r.GetInt32(17);
            return new SearchHit(
                entry,
                Core.Mastery.Percent(interval, correct, total),
                Core.Mastery.Bucket(total, interval),
                due,
                matched.GetValueOrDefault(entry.Id));
        }, args.ToArray());

        if (filter.Mastery is { } bucket)
            hits = hits.Where(h => h.Bucket == bucket).ToList();

        return filter.Sort switch
        {
            SortOrder.Alphabetic => hits.OrderBy(h => h.Entry.Normalized, StringComparer.Ordinal).ToList(),
            SortOrder.Due => hits.OrderBy(h => h.DueUtc).ThenBy(h => h.Entry.Normalized).ToList(),
            SortOrder.Mastery => hits.OrderByDescending(h => h.MasteryPercent)
                                     .ThenBy(h => h.Entry.Normalized).ToList(),
            _ => hits.OrderByDescending(h => h.Entry.CreatedUtc).ThenByDescending(h => h.Entry.Id).ToList(),
        };
    }

    /// <summary>Turns free text into a safe FTS5 prefix query. Every token is
    /// double-quoted so the user can type quotes, parentheses and operators
    /// without producing a syntax error.</summary>
    public static string ToFts(string text)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => new string(t.Where(char.IsLetterOrDigit).ToArray()))
            .Where(t => t.Length > 0)
            .Select(t => $"\"{t}\"*");

        return string.Join(' ', tokens);
    }
}
