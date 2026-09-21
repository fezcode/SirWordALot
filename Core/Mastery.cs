namespace SirWordALot.Core;

/// <summary>Mastery is derived, never stored: how long the word survives between
/// reviews, discounted by how often it has actually been recalled.</summary>
public static class Mastery
{
    public const int FullIntervalDays = 60;

    public static int Percent(int intervalDays, int correct, int total)
    {
        if (total <= 0) return 0;
        var span = Math.Min(Math.Max(intervalDays, 0), FullIntervalDays) / (double)FullIntervalDays;
        var accuracy = Math.Clamp(correct / (double)total, 0, 1);
        return (int)Math.Round(100 * span * accuracy);
    }

    public static MasteryBucket Bucket(int totalReviews, int intervalDays) => totalReviews <= 0
        ? MasteryBucket.New
        : intervalDays switch
        {
            < 7 => MasteryBucket.Learning,
            < 30 => MasteryBucket.Familiar,
            < 90 => MasteryBucket.Strong,
            _ => MasteryBucket.Mastered,
        };
}
