namespace SirWordALot.Core;

/// <summary>Subsequence matching with a bonus for consecutive characters and for
/// matches at the start of the candidate. Returns -1 when the query does not match.</summary>
public static class Fuzzy
{
    public static int Score(string candidate, string query)
    {
        if (query.Length == 0) return 0;

        var haystack = candidate.ToLowerInvariant();
        var needle = query.Trim().ToLowerInvariant();

        var score = 0;
        var cursor = 0;
        var previous = -2;

        foreach (var c in needle)
        {
            var index = haystack.IndexOf(c, cursor);
            if (index < 0) return -1;

            score += 10;
            if (index == previous + 1) score += 8;
            if (index == 0) score += 12;
            score -= Math.Min(6, index - cursor);

            previous = index;
            cursor = index + 1;
        }

        return score;
    }
}
