namespace SirWordALot.Core;

/// <summary>Pure scheduling arithmetic. No I/O, no clock of its own — the caller
/// supplies <c>nowUtc</c> so every transition is reproducible in a test.</summary>
public static class Scheduler
{
    public const double StartEase = 2.5;
    public const double MinEase = 1.3;
    public const int MaxIntervalDays = 365;

    public static readonly IReadOnlyList<int> LeitnerIntervals = [1, 2, 4, 8, 16];

    /// <summary>A multiplier in [0.95, 1.05], applied to intervals above two days
    /// so that cards reviewed together do not stay clumped together forever.</summary>
    public static double RandomFuzz(Random random) => 0.95 + random.NextDouble() * 0.10;

    /// <summary>Away-from-zero rounding. The .NET default rounds halves to even,
    /// which would silently shorten every interval that lands on a .5 boundary.</summary>
    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    public static Scheduling Apply(Scheduling current, Grade grade, DateTime nowUtc, double fuzz = 1.0) =>
        current.Scheduler == SchedulerKind.Leitner
            ? ApplyLeitner(current, grade, nowUtc)
            : ApplySm2(current, grade, nowUtc, fuzz);

    public static int PreviewIntervalDays(Scheduling current, Grade grade) =>
        Apply(current, grade, current.DueUtc).IntervalDays;

    private static Scheduling ApplySm2(Scheduling c, Grade grade, DateTime nowUtc, double fuzz)
    {
        var ease = Math.Max(MinEase, c.Ease + grade switch
        {
            Grade.Again => -0.20,
            Grade.Hard => -0.15,
            Grade.Good => 0.0,
            _ => 0.15,
        });

        var interval = grade switch
        {
            Grade.Again => 0,
            Grade.Hard => Math.Max(1, Round(c.IntervalDays * 1.2)),
            Grade.Good => c.Repetitions switch { 0 => 1, 1 => 6, _ => Round(c.IntervalDays * ease) },
            _ => c.Repetitions switch { 0 => 4, _ => Round(c.IntervalDays * ease * 1.3) },
        };

        if (interval > 2) interval = Round(interval * fuzz);
        interval = Math.Clamp(interval, 0, MaxIntervalDays);

        return c with
        {
            Ease = ease,
            Repetitions = grade == Grade.Again ? 0 : c.Repetitions + 1,
            Lapses = grade == Grade.Again ? c.Lapses + 1 : c.Lapses,
            IntervalDays = interval,
            DueUtc = interval == 0 ? nowUtc : nowUtc.Date.AddDays(interval),
            LastReviewedUtc = nowUtc,
        };
    }

    private static Scheduling ApplyLeitner(Scheduling c, Grade grade, DateTime nowUtc)
    {
        var box = Math.Clamp(c.Box, 1, LeitnerIntervals.Count);
        box = grade switch
        {
            Grade.Again => 1,
            Grade.Hard => box,
            _ => Math.Min(LeitnerIntervals.Count, box + 1),
        };

        var interval = LeitnerIntervals[box - 1];

        return c with
        {
            Box = box,
            IntervalDays = interval,
            Repetitions = grade == Grade.Again ? 0 : c.Repetitions + 1,
            Lapses = grade == Grade.Again ? c.Lapses + 1 : c.Lapses,
            DueUtc = nowUtc.Date.AddDays(interval),
            LastReviewedUtc = nowUtc,
        };
    }

    /// <summary>Switches scheduler without losing progress: the due date is kept and
    /// the target scheduler's fields are seeded from the current interval.</summary>
    public static Scheduling Convert(Scheduling current, SchedulerKind target)
    {
        if (current.Scheduler == target) return current;

        if (target == SchedulerKind.Leitner)
        {
            var box = current.IntervalDays switch
            {
                <= 1 => 1,
                <= 2 => 2,
                <= 4 => 3,
                <= 8 => 4,
                _ => 5,
            };
            return current with { Scheduler = target, Box = box, IntervalDays = LeitnerIntervals[box - 1] };
        }

        return current with
        {
            Scheduler = target,
            Ease = current.Ease < MinEase ? StartEase : current.Ease,
            Repetitions = Math.Max(current.Repetitions, current.Box),
        };
    }
}
