namespace SirWordALot.Core;

public enum RelationKind { Synonym, Antonym, Related, Rhyme, Form }
public enum SchedulerKind { Sm2, Leitner }
public enum QuizMode { Flip, Choice, Cloze, Typed }
public enum Grade { Again = 0, Hard = 1, Good = 2, Easy = 3 }
public enum MasteryBucket { New, Learning, Familiar, Strong, Mastered }
public enum DueState { Any, DueNow, NotDue, Never }
public enum SortOrder { Recent, Alphabetic, Due, Mastery }

public sealed record Entry(
    long Id,
    string Headword,
    string Normalized,
    string? Pronunciation,
    string? AudioUrl,
    string? PartOfSpeech,
    string? Definition,
    string? Notes,
    bool Starred,
    bool Archived,
    string Source,
    DateTime CreatedUtc,
    DateTime ModifiedUtc);

public sealed record Relation(long Id, long EntryId, RelationKind Kind, string Word, string? Source);

public sealed record Sentence(
    long Id,
    long? EntryId,
    string Text,
    string? Source,
    int? ClozeStart,
    int? ClozeLength,
    DateTime CreatedUtc);

public sealed record Tag(long Id, string Name);

public sealed record Deck(long Id, string Name, string? Description, string? FilterJson, DateTime CreatedUtc);

public sealed record Scheduling(
    long EntryId,
    SchedulerKind Scheduler,
    int Box,
    double Ease,
    int IntervalDays,
    int Repetitions,
    int Lapses,
    DateTime DueUtc,
    DateTime? LastReviewedUtc);

public sealed record Review(
    long Id,
    long EntryId,
    QuizMode Mode,
    Grade Grade,
    bool Correct,
    bool Scheduled,
    int ElapsedMs,
    int IntervalBefore,
    int IntervalAfter,
    DateTime ReviewedUtc);

/// <summary>Column conversions. Unknown values fall back rather than throwing,
/// so a hand-edited or future database never crashes the app on read.</summary>
public static class Enums
{
    public static string ToColumn(this RelationKind kind) => kind switch
    {
        RelationKind.Synonym => "synonym",
        RelationKind.Antonym => "antonym",
        RelationKind.Rhyme => "rhyme",
        RelationKind.Form => "form",
        _ => "related",
    };

    public static RelationKind RelationKindFrom(string value) => value switch
    {
        "synonym" => RelationKind.Synonym,
        "antonym" => RelationKind.Antonym,
        "rhyme" => RelationKind.Rhyme,
        "form" => RelationKind.Form,
        _ => RelationKind.Related,
    };

    public static string ToColumn(this QuizMode mode) => mode switch
    {
        QuizMode.Choice => "choice",
        QuizMode.Cloze => "cloze",
        QuizMode.Typed => "typed",
        _ => "flip",
    };

    public static QuizMode QuizModeFrom(string value) => value switch
    {
        "choice" => QuizMode.Choice,
        "cloze" => QuizMode.Cloze,
        "typed" => QuizMode.Typed,
        _ => QuizMode.Flip,
    };

    public static string ToColumn(this SchedulerKind kind) =>
        kind == SchedulerKind.Leitner ? "leitner" : "sm2";

    public static SchedulerKind SchedulerKindFrom(string value) =>
        value == "leitner" ? SchedulerKind.Leitner : SchedulerKind.Sm2;
}

/// <summary>ISO 8601 round-tripping for the TEXT timestamp columns.</summary>
public static class Iso
{
    public static string Text(DateTime utc) => utc.ToUniversalTime().ToString("O");

    public static DateTime Parse(string text) =>
        DateTime.Parse(text, null, System.Globalization.DateTimeStyles.AdjustToUniversal |
                                    System.Globalization.DateTimeStyles.AssumeUniversal);

    public static DateTime? ParseOrNull(string? text) => text is null ? null : Parse(text);
}
