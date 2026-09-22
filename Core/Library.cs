using Microsoft.Data.Sqlite;

namespace SirWordALot.Core;

/// <summary>All reads and writes against the library. Raises <see cref="Changed"/>
/// after any mutation so views can re-render.</summary>
public sealed class Library : IDisposable
{
    private readonly Db _db;

    public Library(Db db) => _db = db;

    public Db Database => _db;

    /// <summary>Raised after any mutation. Views subscribe and re-render.</summary>
    public event Action? Changed;

    private void Touch() => Changed?.Invoke();

    public void Dispose() => _db.Dispose();

    // ---- entries -------------------------------------------------------

    public static string Normalize(string headword) =>
        string.Join(' ', headword.Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public int Count => System.Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM entries"));

    public long AddEntry(string headword, string source = "manual")
    {
        var normalized = Normalize(headword);
        if (normalized.Length == 0)
            throw new ArgumentException("A headword cannot be blank.", nameof(headword));

        if (FindEntry(normalized) is { } existing) return existing.Id;

        var now = Iso.Text(DateTime.UtcNow);
        _db.Execute(
            """
            INSERT INTO entries (headword, normalized, source, created_utc, modified_utc)
            VALUES ($headword, $normalized, $source, $now, $now)
            """,
            ("$headword", headword.Trim()), ("$normalized", normalized),
            ("$source", source), ("$now", now));

        var id = (long)_db.Scalar("SELECT last_insert_rowid()")!;
        _db.Execute(
            "INSERT INTO scheduling (entry_id, scheduler, due_utc) VALUES ($id, 'sm2', $now)",
            ("$id", id), ("$now", now));

        Touch();
        return id;
    }

    public Entry? GetEntry(long id) =>
        _db.Query($"{EntrySelect} WHERE id = $id", ReadEntry, ("$id", id)).FirstOrDefault();

    public Entry? FindEntry(string headword) =>
        _db.Query($"{EntrySelect} WHERE normalized = $normalized", ReadEntry,
            ("$normalized", Normalize(headword))).FirstOrDefault();

    public IReadOnlyList<Entry> AllEntries(bool includeArchived = false) =>
        _db.Query($"{EntrySelect}{(includeArchived ? "" : " WHERE archived = 0")} ORDER BY normalized",
            ReadEntry);

    public void UpdateEntry(Entry entry)
    {
        var normalized = Normalize(entry.Headword);
        if (normalized.Length == 0)
            throw new ArgumentException("A headword cannot be blank.", nameof(entry));

        _db.Execute(
            """
            UPDATE entries SET
              headword = $headword, normalized = $normalized, pronunciation = $pronunciation,
              audio_url = $audio, part_of_speech = $pos, definition = $definition,
              notes = $notes, starred = $starred, archived = $archived,
              translation = $translation, modified_utc = $now
            WHERE id = $id
            """,
            ("$headword", entry.Headword.Trim()), ("$normalized", normalized),
            ("$pronunciation", entry.Pronunciation), ("$audio", entry.AudioUrl),
            ("$pos", entry.PartOfSpeech), ("$definition", entry.Definition),
            ("$notes", entry.Notes), ("$starred", entry.Starred ? 1 : 0),
            ("$archived", entry.Archived ? 1 : 0), ("$translation", entry.Translation),
            ("$now", Iso.Text(DateTime.UtcNow)), ("$id", entry.Id));

        Touch();
    }

    public void DeleteEntry(long id)
    {
        _db.Execute("DELETE FROM entries WHERE id = $id", ("$id", id));
        Touch();
    }

    public void SetStarred(long id, bool starred) => SetFlag(id, "starred", starred);

    public void SetArchived(long id, bool archived) => SetFlag(id, "archived", archived);

    private void SetFlag(long id, string column, bool value)
    {
        _db.Execute($"UPDATE entries SET {column} = $value, modified_utc = $now WHERE id = $id",
            ("$value", value ? 1 : 0), ("$now", Iso.Text(DateTime.UtcNow)), ("$id", id));
        Touch();
    }

    private const string EntrySelect =
        """
        SELECT id, headword, normalized, pronunciation, audio_url, part_of_speech,
               definition, notes, starred, archived, source, created_utc, modified_utc,
               translation
        FROM entries
        """;

    /// <summary>Shared by every query that projects the entry columns; the column
    /// order above and in those queries must match this reader.</summary>
    internal static Entry ReadEntry(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        r.IsDBNull(7) ? null : r.GetString(7),
        r.GetInt32(8) != 0, r.GetInt32(9) != 0, r.GetString(10),
        Iso.Parse(r.GetString(11)), Iso.Parse(r.GetString(12)),
        r.IsDBNull(13) ? null : r.GetString(13));

    // ---- relations -----------------------------------------------------

    public void AddRelation(long entryId, RelationKind kind, string word, string? source = null)
    {
        var trimmed = word.Trim();
        if (trimmed.Length == 0) return;
        _db.Execute(
            """
            INSERT OR IGNORE INTO relations (entry_id, kind, word, source)
            VALUES ($entry, $kind, $word, $source)
            """,
            ("$entry", entryId), ("$kind", kind.ToColumn()), ("$word", trimmed), ("$source", source));
        Touch();
    }

    public void RemoveRelation(long entryId, RelationKind kind, string word)
    {
        _db.Execute("DELETE FROM relations WHERE entry_id = $entry AND kind = $kind AND word = $word",
            ("$entry", entryId), ("$kind", kind.ToColumn()), ("$word", word.Trim()));
        Touch();
    }

    public IReadOnlyList<Relation> Relations(long entryId) => _db.Query(
        "SELECT id, entry_id, kind, word, source FROM relations WHERE entry_id = $entry ORDER BY kind, word",
        r => new Relation(r.GetInt64(0), r.GetInt64(1), Enums.RelationKindFrom(r.GetString(2)),
            r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)),
        ("$entry", entryId));

    // ---- sentences -----------------------------------------------------

    public long AddSentence(string text, long? entryId = null, string? source = null)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("A sentence cannot be blank.", nameof(text));

        _db.Execute(
            """
            INSERT INTO sentences (entry_id, text, source, created_utc)
            VALUES ($entry, $text, $source, $now)
            """,
            ("$entry", entryId), ("$text", trimmed), ("$source", source),
            ("$now", Iso.Text(DateTime.UtcNow)));

        Touch();
        return (long)_db.Scalar("SELECT last_insert_rowid()")!;
    }

    public void UpdateSentence(Sentence sentence)
    {
        _db.Execute(
            """
            UPDATE sentences SET text = $text, source = $source,
              cloze_start = $start, cloze_length = $length, entry_id = $entry
            WHERE id = $id
            """,
            ("$text", sentence.Text.Trim()), ("$source", sentence.Source),
            ("$start", sentence.ClozeStart), ("$length", sentence.ClozeLength),
            ("$entry", sentence.EntryId), ("$id", sentence.Id));
        Touch();
    }

    public void DeleteSentence(long id)
    {
        _db.Execute("DELETE FROM sentences WHERE id = $id", ("$id", id));
        Touch();
    }

    public void LinkSentence(long sentenceId, long? entryId)
    {
        _db.Execute("UPDATE sentences SET entry_id = $entry WHERE id = $id",
            ("$entry", entryId), ("$id", sentenceId));
        Touch();
    }

    public IReadOnlyList<Sentence> SentencesFor(long entryId) =>
        _db.Query($"{SentenceSelect} WHERE entry_id = $entry ORDER BY id", ReadSentence, ("$entry", entryId));

    public IReadOnlyList<Sentence> AllSentences(bool looseOnly = false) =>
        _db.Query($"{SentenceSelect}{(looseOnly ? " WHERE entry_id IS NULL" : "")} ORDER BY id DESC", ReadSentence);

    private const string SentenceSelect =
        "SELECT id, entry_id, text, source, cloze_start, cloze_length, created_utc FROM sentences";

    internal static Sentence ReadSentence(SqliteDataReader r) => new(
        r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetInt32(4),
        r.IsDBNull(5) ? null : r.GetInt32(5),
        Iso.Parse(r.GetString(6)));

    // ---- tags ----------------------------------------------------------

    public long AddTag(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
            throw new ArgumentException("A tag cannot be blank.", nameof(name));

        _db.Execute("INSERT OR IGNORE INTO tags (name) VALUES ($name)", ("$name", normalized));
        return (long)_db.Scalar("SELECT id FROM tags WHERE name = $name", ("$name", normalized))!;
    }

    public void TagEntry(long entryId, string tag)
    {
        var tagId = AddTag(tag);
        _db.Execute("INSERT OR IGNORE INTO entry_tags (entry_id, tag_id) VALUES ($entry, $tag)",
            ("$entry", entryId), ("$tag", tagId));
        Touch();
    }

    public void UntagEntry(long entryId, string tag)
    {
        _db.Execute(
            """
            DELETE FROM entry_tags
            WHERE entry_id = $entry
              AND tag_id = (SELECT id FROM tags WHERE name = $tag)
            """,
            ("$entry", entryId), ("$tag", tag.Trim().ToLowerInvariant()));
        Touch();
    }

    public IReadOnlyList<Tag> Tags() => _db.Query(
        "SELECT id, name FROM tags ORDER BY name",
        r => new Tag(r.GetInt64(0), r.GetString(1)));

    public IReadOnlyList<string> TagsFor(long entryId) => _db.Query(
        """
        SELECT t.name FROM tags t
        JOIN entry_tags et ON et.tag_id = t.id
        WHERE et.entry_id = $entry ORDER BY t.name
        """,
        r => r.GetString(0), ("$entry", entryId));

    // ---- decks ---------------------------------------------------------

    public long CreateDeck(string name, string? description = null, string? filterJson = null)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("A deck cannot be blank.", nameof(name));

        _db.Execute(
            """
            INSERT INTO decks (name, description, filter_json, created_utc)
            VALUES ($name, $description, $filter, $now)
            """,
            ("$name", trimmed), ("$description", description), ("$filter", filterJson),
            ("$now", Iso.Text(DateTime.UtcNow)));

        Touch();
        return (long)_db.Scalar("SELECT last_insert_rowid()")!;
    }

    public void RenameDeck(long id, string name)
    {
        _db.Execute("UPDATE decks SET name = $name WHERE id = $id", ("$name", name.Trim()), ("$id", id));
        Touch();
    }

    public void DeleteDeck(long id)
    {
        _db.Execute("DELETE FROM decks WHERE id = $id", ("$id", id));
        Touch();
    }

    public void AddToDeck(long deckId, long entryId)
    {
        _db.Execute(
            """
            INSERT OR IGNORE INTO deck_entries (deck_id, entry_id, position)
            VALUES ($deck, $entry, (SELECT COALESCE(MAX(position), 0) + 1 FROM deck_entries WHERE deck_id = $deck))
            """,
            ("$deck", deckId), ("$entry", entryId));
        Touch();
    }

    public void RemoveFromDeck(long deckId, long entryId)
    {
        _db.Execute("DELETE FROM deck_entries WHERE deck_id = $deck AND entry_id = $entry",
            ("$deck", deckId), ("$entry", entryId));
        Touch();
    }

    public IReadOnlyList<Deck> Decks() => _db.Query(
        "SELECT id, name, description, filter_json, created_utc FROM decks ORDER BY name",
        r => new Deck(r.GetInt64(0), r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            Iso.Parse(r.GetString(4))));

    public IReadOnlyList<Entry> DeckEntries(long deckId) => _db.Query(
        """
        SELECT e.id, e.headword, e.normalized, e.pronunciation, e.audio_url, e.part_of_speech,
               e.definition, e.notes, e.starred, e.archived, e.source, e.created_utc, e.modified_utc,
               e.translation
        FROM entries e
        JOIN deck_entries de ON de.entry_id = e.id
        WHERE de.deck_id = $deck
        ORDER BY de.position
        """,
        ReadEntry, ("$deck", deckId));

    // ---- scheduling and reviews ----------------------------------------

    public Scheduling GetScheduling(long entryId) => _db.Query(
        """
        SELECT entry_id, scheduler, box, ease, interval_days, repetitions, lapses,
               due_utc, last_reviewed_utc
        FROM scheduling WHERE entry_id = $id
        """,
        r => new Scheduling(
            r.GetInt64(0), Enums.SchedulerKindFrom(r.GetString(1)), r.GetInt32(2),
            r.GetDouble(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6),
            Iso.Parse(r.GetString(7)), r.IsDBNull(8) ? null : Iso.Parse(r.GetString(8))),
        ("$id", entryId)).Single();

    public void SaveScheduling(Scheduling s)
    {
        _db.Execute(
            """
            UPDATE scheduling SET scheduler = $scheduler, box = $box, ease = $ease,
              interval_days = $interval, repetitions = $reps, lapses = $lapses,
              due_utc = $due, last_reviewed_utc = $last
            WHERE entry_id = $id
            """,
            ("$scheduler", s.Scheduler.ToColumn()), ("$box", s.Box), ("$ease", s.Ease),
            ("$interval", s.IntervalDays), ("$reps", s.Repetitions), ("$lapses", s.Lapses),
            ("$due", Iso.Text(s.DueUtc)),
            ("$last", s.LastReviewedUtc is { } last ? Iso.Text(last) : null),
            ("$id", s.EntryId));
        Touch();
    }

    public long AddReview(Review review)
    {
        _db.Execute(
            """
            INSERT INTO reviews (entry_id, mode, grade, correct, scheduled, elapsed_ms,
                                 interval_before, interval_after, reviewed_utc)
            VALUES ($entry, $mode, $grade, $correct, $scheduled, $elapsed, $before, $after, $at)
            """,
            ("$entry", review.EntryId), ("$mode", review.Mode.ToColumn()),
            ("$grade", (int)review.Grade), ("$correct", review.Correct ? 1 : 0),
            ("$scheduled", review.Scheduled ? 1 : 0), ("$elapsed", review.ElapsedMs),
            ("$before", review.IntervalBefore), ("$after", review.IntervalAfter),
            ("$at", Iso.Text(review.ReviewedUtc)));
        Touch();
        return (long)_db.Scalar("SELECT last_insert_rowid()")!;
    }

    // ---- queue reads ---------------------------------------------------

    public IReadOnlyList<Entry> DueEntries(DateTime nowUtc, int limit = int.MaxValue) => _db.Query(
        """
        SELECT e.id, e.headword, e.normalized, e.pronunciation, e.audio_url, e.part_of_speech,
               e.definition, e.notes, e.starred, e.archived, e.source, e.created_utc, e.modified_utc,
               e.translation
        FROM entries e JOIN scheduling s ON s.entry_id = e.id
        WHERE e.archived = 0 AND s.last_reviewed_utc IS NOT NULL AND s.due_utc <= $now
        ORDER BY s.due_utc LIMIT $limit
        """,
        ReadEntry, ("$now", Iso.Text(nowUtc)), ("$limit", limit));

    public IReadOnlyList<Entry> NewEntries(int limit) => _db.Query(
        """
        SELECT e.id, e.headword, e.normalized, e.pronunciation, e.audio_url, e.part_of_speech,
               e.definition, e.notes, e.starred, e.archived, e.source, e.created_utc, e.modified_utc,
               e.translation
        FROM entries e JOIN scheduling s ON s.entry_id = e.id
        WHERE e.archived = 0 AND s.last_reviewed_utc IS NULL
        ORDER BY e.created_utc, e.id LIMIT $limit
        """,
        ReadEntry, ("$limit", Math.Max(0, limit)));

    public IReadOnlyList<Entry> StarredEntries() =>
        _db.Query($"{EntrySelect} WHERE archived = 0 AND starred = 1 ORDER BY normalized", ReadEntry);

    public IReadOnlyList<Entry> EntriesWithTag(string tag) => _db.Query(
        """
        SELECT e.id, e.headword, e.normalized, e.pronunciation, e.audio_url, e.part_of_speech,
               e.definition, e.notes, e.starred, e.archived, e.source, e.created_utc, e.modified_utc,
               e.translation
        FROM entries e
        JOIN entry_tags et ON et.entry_id = e.id
        JOIN tags t ON t.id = et.tag_id
        WHERE e.archived = 0 AND t.name = $tag ORDER BY e.normalized
        """,
        ReadEntry, ("$tag", tag.Trim().ToLowerInvariant()));

    public IReadOnlyList<Review> ReviewsFor(long entryId) => _db.Query(
        """
        SELECT id, entry_id, mode, grade, correct, scheduled, elapsed_ms,
               interval_before, interval_after, reviewed_utc
        FROM reviews WHERE entry_id = $entry ORDER BY id
        """,
        r => new Review(r.GetInt64(0), r.GetInt64(1), Enums.QuizModeFrom(r.GetString(2)),
            (Grade)r.GetInt32(3), r.GetInt32(4) != 0, r.GetInt32(5) != 0, r.GetInt32(6),
            r.GetInt32(7), r.GetInt32(8), Iso.Parse(r.GetString(9))),
        ("$entry", entryId));

    public int ReviewsToday(DateTime nowUtc) => System.Convert.ToInt32(_db.Scalar(
        "SELECT COUNT(*) FROM reviews WHERE scheduled = 1 AND reviewed_utc >= $start AND reviewed_utc < $end",
        ("$start", Iso.Text(nowUtc.Date)), ("$end", Iso.Text(nowUtc.Date.AddDays(1)))));

    public int NewReviewedToday(DateTime nowUtc) => System.Convert.ToInt32(_db.Scalar(
        """
        SELECT COUNT(DISTINCT entry_id) FROM reviews
        WHERE scheduled = 1 AND interval_before = 0
          AND reviewed_utc >= $start AND reviewed_utc < $end
        """,
        ("$start", Iso.Text(nowUtc.Date)), ("$end", Iso.Text(nowUtc.Date.AddDays(1)))));
}
