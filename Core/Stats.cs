namespace SirWordALot.Core;

public sealed record Overview(
    int TotalWords, int DueToday, int Mastered, int StreakDays, double Retention, int ReviewsToday);

public sealed record RetentionPoint(int UpperDays, double Rate, int Samples);

public sealed record HeatmapDay(DateOnly Day, int Count);

/// <summary>Aggregate queries over the append-only review log.</summary>
public sealed class Stats(Db db)
{
    private static readonly int[] IntervalBuckets = [1, 3, 7, 14, 30, 90, 365];

    public Overview Summary(DateTime nowUtc)
    {
        var total = Count("SELECT COUNT(*) FROM entries WHERE archived = 0");

        var due = Count(
            """
            SELECT COUNT(*) FROM entries e JOIN scheduling s ON s.entry_id = e.id
            WHERE e.archived = 0 AND s.due_utc <= $now
            """,
            ("$now", Iso.Text(nowUtc)));

        var mastered = Count(
            """
            SELECT COUNT(*) FROM entries e JOIN scheduling s ON s.entry_id = e.id
            WHERE e.archived = 0 AND s.interval_days >= 90 AND s.repetitions > 0
            """);

        var reviewsToday = Count(
            "SELECT COUNT(*) FROM reviews WHERE scheduled = 1 AND reviewed_utc >= $start AND reviewed_utc < $end",
            ("$start", Iso.Text(nowUtc.Date)), ("$end", Iso.Text(nowUtc.Date.AddDays(1))));

        return new Overview(total, due, mastered, Streak(nowUtc), Retention(), reviewsToday);
    }

    private double Retention()
    {
        var attempts = Count("SELECT COUNT(*) FROM reviews WHERE scheduled = 1 AND interval_before > 0");
        if (attempts == 0) return 0;
        var correct = Count(
            "SELECT COUNT(*) FROM reviews WHERE scheduled = 1 AND interval_before > 0 AND correct = 1");
        return correct / (double)attempts;
    }

    private int Streak(DateTime nowUtc)
    {
        var days = db.Query(
            """
            SELECT DISTINCT substr(reviewed_utc, 1, 10) FROM reviews
            WHERE scheduled = 1 ORDER BY 1 DESC
            """,
            r => DateOnly.Parse(r.GetString(0))).ToHashSet();

        if (days.Count == 0) return 0;

        var cursor = DateOnly.FromDateTime(nowUtc.Date);
        if (!days.Contains(cursor)) cursor = cursor.AddDays(-1);

        var streak = 0;
        while (days.Contains(cursor)) { streak++; cursor = cursor.AddDays(-1); }
        return streak;
    }

    public IReadOnlyList<HeatmapDay> Heatmap(DateTime nowUtc, int weeks = 53)
    {
        var last = DateOnly.FromDateTime(nowUtc.Date);
        var first = last.AddDays(-(weeks * 7 - 1));

        var counts = db.Query(
            """
            SELECT substr(reviewed_utc, 1, 10), COUNT(*) FROM reviews
            WHERE scheduled = 1 AND reviewed_utc >= $start
            GROUP BY 1
            """,
            r => (Day: DateOnly.Parse(r.GetString(0)), Count: r.GetInt32(1)),
            ("$start", Iso.Text(first.ToDateTime(TimeOnly.MinValue))))
            .ToDictionary(x => x.Day, x => x.Count);

        var days = new List<HeatmapDay>(weeks * 7);
        for (var day = first; day <= last; day = day.AddDays(1))
            days.Add(new HeatmapDay(day, counts.GetValueOrDefault(day)));
        return days;
    }

    public IReadOnlyDictionary<MasteryBucket, int> Breakdown()
    {
        var rows = db.Query(
            """
            SELECT s.interval_days, s.repetitions FROM entries e
            JOIN scheduling s ON s.entry_id = e.id
            WHERE e.archived = 0
            """,
            r => (Interval: r.GetInt32(0), Repetitions: r.GetInt32(1)));

        var breakdown = Enum.GetValues<MasteryBucket>().ToDictionary(b => b, _ => 0);
        foreach (var row in rows)
            breakdown[Mastery.Bucket(row.Repetitions, row.Interval)]++;
        return breakdown;
    }

    public IReadOnlyList<RetentionPoint> RetentionCurve()
    {
        var rows = db.Query(
            "SELECT interval_before, correct FROM reviews WHERE scheduled = 1 AND interval_before > 0",
            r => (Interval: r.GetInt32(0), Correct: r.GetInt32(1) != 0));

        return IntervalBuckets.Select((upper, position) =>
        {
            var lower = position == 0 ? 1 : IntervalBuckets[position - 1] + 1;
            var inBucket = rows.Where(x => x.Interval >= lower && x.Interval <= upper).ToList();
            var rate = inBucket.Count == 0 ? 0 : inBucket.Count(x => x.Correct) / (double)inBucket.Count;
            return new RetentionPoint(upper, rate, inBucket.Count);
        }).ToList();
    }

    private int Count(string sql, params (string, object?)[] args) =>
        System.Convert.ToInt32(db.Scalar(sql, args));
}
