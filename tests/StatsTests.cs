using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class StatsTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static (Library Library, Stats Stats) Fixture()
    {
        var db = Db.OpenMemory();
        return (new Library(db), new Stats(db));
    }

    private static void Log(Library library, long entryId, DateTime at, bool correct,
                            int before = 5, int after = 10)
        => library.AddReview(new Review(0, entryId, QuizMode.Flip,
            correct ? Grade.Good : Grade.Again, correct, true, 900, before, after, at));

    [Fact]
    public void SummaryCountsWordsAndDueCards()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        library.AddEntry("laconic");
        library.SaveScheduling(library.GetScheduling(a) with
        { DueUtc = Now.AddDays(-1), LastReviewedUtc = Now.AddDays(-6), IntervalDays = 5 });

        var summary = stats.Summary(Now);
        Assert.Equal(2, summary.TotalWords);
        Assert.Equal(2, summary.DueToday);
    }

    [Fact]
    public void ArchivedWordsAreExcludedFromTheTotal()
    {
        var (library, stats) = Fixture();
        library.AddEntry("eminence");
        var shelved = library.AddEntry("laconic");
        library.SetArchived(shelved, true);

        Assert.Equal(1, stats.Summary(Now).TotalWords);
    }

    [Fact]
    public void MasteredCountsWordsPastNinetyDays()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        library.SaveScheduling(library.GetScheduling(a) with
        { IntervalDays = 120, Repetitions = 8, LastReviewedUtc = Now, DueUtc = Now.AddDays(120) });
        Log(library, a, Now, correct: true);

        Assert.Equal(1, stats.Summary(Now).Mastered);
    }

    [Fact]
    public void ReviewsTodayCountsOnlyTodayAndOnlyScheduled()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        Log(library, a, Now, correct: true);
        Log(library, a, Now.AddDays(-1), correct: true);
        library.AddReview(new Review(0, a, QuizMode.Flip, Grade.Good, true,
            Scheduled: false, 900, 5, 10, Now));

        Assert.Equal(1, stats.Summary(Now).ReviewsToday);
    }

    [Fact]
    public void RetentionIgnoresFirstExposures()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        Log(library, a, Now, correct: false, before: 0);
        Log(library, a, Now, correct: true, before: 5);
        Log(library, a, Now, correct: true, before: 5);

        Assert.Equal(1.0, stats.Summary(Now).Retention, 3);
    }

    [Fact]
    public void RetentionIsZeroWithNothingToMeasure() =>
        Assert.Equal(0, Fixture().Stats.Summary(Now).Retention);

    [Fact]
    public void StreakCountsConsecutiveDaysEndingToday()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        foreach (var offset in new[] { 0, -1, -2, -4 })
            Log(library, a, Now.AddDays(offset), correct: true);

        Assert.Equal(3, stats.Summary(Now).StreakDays);
    }

    [Fact]
    public void StreakSurvivesADayNotYetStudied()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        foreach (var offset in new[] { -1, -2 })
            Log(library, a, Now.AddDays(offset), correct: true);

        Assert.Equal(2, stats.Summary(Now).StreakDays);
    }

    [Fact]
    public void StreakIsZeroAfterAMissedDay()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        Log(library, a, Now.AddDays(-3), correct: true);

        Assert.Equal(0, stats.Summary(Now).StreakDays);
    }

    [Fact]
    public void HeatmapCoversEveryDayInTheWindowAndCountsReviews()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        Log(library, a, Now, correct: true);
        Log(library, a, Now, correct: true);
        Log(library, a, Now.AddDays(-10), correct: true);

        var heatmap = stats.Heatmap(Now, weeks: 2);

        Assert.Equal(14, heatmap.Count);
        Assert.Equal(DateOnly.FromDateTime(Now), heatmap[^1].Day);
        Assert.Equal(2, heatmap[^1].Count);
        Assert.Equal(1, heatmap.Single(d => d.Day == DateOnly.FromDateTime(Now.AddDays(-10))).Count);
        Assert.All(heatmap, d => Assert.True(d.Count >= 0));
    }

    [Fact]
    public void BreakdownBucketsEveryUnarchivedWord()
    {
        var (library, stats) = Fixture();
        library.AddEntry("eminence");
        var strong = library.AddEntry("laconic");
        library.SaveScheduling(library.GetScheduling(strong) with
        { IntervalDays = 45, Repetitions = 5, LastReviewedUtc = Now, DueUtc = Now.AddDays(45) });
        Log(library, strong, Now, correct: true);

        var breakdown = stats.Breakdown();

        Assert.Equal(1, breakdown[MasteryBucket.New]);
        Assert.Equal(1, breakdown[MasteryBucket.Strong]);
        Assert.Equal(0, breakdown[MasteryBucket.Mastered]);
        Assert.Equal(5, breakdown.Count);
    }

    [Fact]
    public void RetentionCurveGroupsByIntervalBucket()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        Log(library, a, Now, correct: true, before: 1);
        Log(library, a, Now, correct: false, before: 1);
        Log(library, a, Now, correct: true, before: 40);

        var curve = stats.RetentionCurve();

        Assert.Equal(0.5, curve.Single(p => p.UpperDays == 1).Rate, 3);
        Assert.Equal(2, curve.Single(p => p.UpperDays == 1).Samples);
        Assert.Equal(1.0, curve.Single(p => p.UpperDays == 90).Rate, 3);
    }
}
