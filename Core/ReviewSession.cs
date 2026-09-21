namespace SirWordALot.Core;

public enum ScopeKind { Due, Deck, Tag, Starred, All, FreePractice, Single }

public sealed record ReviewScope(
    ScopeKind Kind, long? DeckId = null, string? Tag = null, long? EntryId = null);

public sealed record SessionOptions(int NewPerDay = 20, QuizMode? Mode = null, int? Seed = null);

public sealed record Card(
    Entry Entry,
    QuizMode Mode,
    Sentence? Sentence,
    string Prompt,
    IReadOnlyList<string> Choices,
    int CorrectChoice);

public sealed record SessionSummary(int Reviewed, int Correct, int Lapsed, TimeSpan Elapsed);

/// <summary>Builds the queue for one sitting and records each answer as it happens.
/// Nothing is buffered: ending early or crashing loses no progress.</summary>
public sealed class ReviewSession
{
    private readonly Library _library;
    private readonly ReviewScope _scope;
    private readonly SessionOptions _options;
    private readonly Random _random;
    private readonly List<Entry> _queue;
    private readonly List<Entry> _pool;
    private readonly DateTime _startedUtc;

    private int _index;
    private int _reviewed, _correct, _lapsed;
    private Card? _current;

    public ReviewSession(Library library, ReviewScope scope, SessionOptions options, DateTime nowUtc)
    {
        _library = library;
        _scope = scope;
        _options = options;
        _random = options.Seed is { } seed ? new Random(seed) : new Random();
        _startedUtc = nowUtc;

        _pool = library.AllEntries().ToList();
        _queue = BuildQueue(nowUtc);
        _current = _queue.Count > 0 ? BuildCard(_queue[0]) : null;
    }

    public int Total => _queue.Count;
    public int Position => _index + 1;
    public int Answered => _reviewed;
    public Card? Current => _current;
    public bool IsFinished => _current is null;

    public SessionSummary Summary => new(_reviewed, _correct, _lapsed, DateTime.UtcNow - _startedUtc);

    public int PreviewIntervalDays(Grade grade)
    {
        if (_current is null) return 0;
        return Scheduler.PreviewIntervalDays(_library.GetScheduling(_current.Entry.Id), grade);
    }

    public void Answer(Grade grade, int elapsedMs, DateTime nowUtc)
    {
        if (_current is null) throw new InvalidOperationException("The session is finished.");

        var entry = _current.Entry;
        var before = _library.GetScheduling(entry.Id);
        var scheduled = _scope.Kind != ScopeKind.FreePractice;
        var after = before;

        if (scheduled)
        {
            after = Scheduler.Apply(before, grade, nowUtc, Scheduler.RandomFuzz(_random));
            _library.SaveScheduling(after);
        }

        _library.AddReview(new Review(0, entry.Id, _current.Mode, grade,
            Correct: grade != Grade.Again, Scheduled: scheduled, elapsedMs,
            before.IntervalDays, after.IntervalDays, nowUtc));

        _reviewed++;
        if (grade == Grade.Again) { _lapsed++; _queue.Add(entry); } else _correct++;

        _index++;
        _current = _index < _queue.Count ? BuildCard(_queue[_index]) : null;
    }

    private List<Entry> BuildQueue(DateTime nowUtc)
    {
        switch (_scope.Kind)
        {
            case ScopeKind.Deck:
                return Shuffle(_library.DeckEntries(_scope.DeckId ?? 0).Where(e => !e.Archived).ToList());
            case ScopeKind.Tag:
                return Shuffle(_library.EntriesWithTag(_scope.Tag ?? "").ToList());
            case ScopeKind.Starred:
                return Shuffle(_library.StarredEntries().ToList());
            case ScopeKind.Single:
                return _scope.EntryId is { } single && _library.GetEntry(single) is { } entry
                    ? [entry] : [];
            case ScopeKind.All:
            case ScopeKind.FreePractice:
                return Shuffle(_library.AllEntries().ToList());
            default:
                var due = _library.DueEntries(nowUtc).ToList();
                var remaining = Math.Max(0, _options.NewPerDay - _library.NewReviewedToday(nowUtc));
                var fresh = _library.NewEntries(remaining).ToList();
                return [.. due, .. Shuffle(fresh)];
        }
    }

    private List<Entry> Shuffle(List<Entry> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        return items;
    }

    private Card BuildCard(Entry entry)
    {
        var sentence = _library.SentencesFor(entry.Id)
            .FirstOrDefault(s => Cloze.ContainsWord(s.Text, entry.Headword));

        var mode = ResolveMode(entry, sentence);

        return mode switch
        {
            QuizMode.Cloze => new Card(entry, mode, sentence, BlankedText(sentence!, entry), [], -1),
            QuizMode.Choice => BuildChoiceCard(entry, sentence),
            QuizMode.Typed => new Card(entry, mode, sentence,
                entry.Definition ?? "Type the word.", [], -1),
            _ => new Card(entry, QuizMode.Flip, sentence, entry.Headword, [], -1),
        };
    }

    private QuizMode ResolveMode(Entry entry, Sentence? sentence)
    {
        var clozeAvailable = sentence is not null;
        var choiceAvailable = _pool.Count(e => e.Id != entry.Id && !string.IsNullOrWhiteSpace(e.Headword)) >= 3
                              && !string.IsNullOrWhiteSpace(entry.Definition);

        if (_options.Mode is { } requested)
            return requested switch
            {
                QuizMode.Cloze when !clozeAvailable => QuizMode.Flip,
                QuizMode.Choice when !choiceAvailable => QuizMode.Flip,
                QuizMode.Typed when string.IsNullOrWhiteSpace(entry.Definition) => QuizMode.Flip,
                _ => requested,
            };

        if (clozeAvailable) return QuizMode.Cloze;
        if (choiceAvailable) return QuizMode.Choice;
        return QuizMode.Flip;
    }

    private static string BlankedText(Sentence sentence, Entry entry)
    {
        if (sentence.ClozeStart is { } start && sentence.ClozeLength is { } length)
            return Cloze.Blank(sentence.Text, start, length);
        return Cloze.BlankFor(sentence.Text, entry.Headword) ?? sentence.Text;
    }

    private Card BuildChoiceCard(Entry entry, Sentence? sentence)
    {
        var samePos = _pool.Where(e => e.Id != entry.Id &&
                                       e.PartOfSpeech == entry.PartOfSpeech).ToList();
        var others = _pool.Where(e => e.Id != entry.Id).ToList();

        var distractors = Shuffle(samePos).Select(e => e.Headword)
            .Concat(Shuffle(others).Select(e => e.Headword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(w => !string.Equals(w, entry.Headword, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        var choices = new List<string>(distractors) { entry.Headword };
        for (var i = choices.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (choices[i], choices[j]) = (choices[j], choices[i]);
        }

        return new Card(entry, QuizMode.Choice, sentence,
            entry.Definition ?? "Which word fits?", choices,
            choices.FindIndex(c => c == entry.Headword));
    }
}
