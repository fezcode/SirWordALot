namespace SirWordALot.Core;

/// <summary>Finds a headword inside a sentence even when the sentence inflects it,
/// blanks it for cloze questions, and scores typed answers.</summary>
public static class Cloze
{
    public const char BlankChar = '▁';

    public static (int Start, int Length)? Find(string sentence, string headword)
    {
        foreach (var form in Forms(headword))
        {
            var index = IndexOfWord(sentence, form);
            if (index >= 0) return (index, form.Length);
        }
        return null;
    }

    public static bool ContainsWord(string sentence, string headword) => Find(sentence, headword) is not null;

    public static string Blank(string sentence, int start, int length)
    {
        if (start < 0 || length <= 0 || start >= sentence.Length) return sentence;
        length = Math.Min(length, sentence.Length - start);
        return string.Concat(sentence.AsSpan(0, start),
                             new string(BlankChar, length),
                             sentence.AsSpan(start + length));
    }

    public static string? BlankFor(string sentence, string headword) =>
        Find(sentence, headword) is { } span ? Blank(sentence, span.Start, span.Length) : null;

    /// <summary>The headword plus the inflections an English sentence is likely to use.
    /// Longest forms are yielded first so a whole-word match lands on the real token.</summary>
    public static IEnumerable<string> Forms(string headword)
    {
        var word = headword.Trim();
        if (word.Length == 0) yield break;

        var forms = new List<string> { word };

        if (word.EndsWith('e'))
        {
            var stem = word[..^1];
            forms.Add(stem + "ing");
            forms.Add(word + "d");
            forms.Add(word + "s");
        }
        else if (word.EndsWith('y') && word.Length > 1 && !IsVowel(word[^2]))
        {
            var stem = word[..^1];
            forms.Add(stem + "ies");
            forms.Add(stem + "ied");
            forms.Add(word + "ing");
        }
        else
        {
            forms.Add(word + "s");
            forms.Add(word + "es");
            forms.Add(word + "ed");
            forms.Add(word + "ing");

            if (word.Length >= 3 && !IsVowel(word[^1]) && IsVowel(word[^2]) && !IsVowel(word[^3]))
            {
                forms.Add(word + word[^1] + "ing");
                forms.Add(word + word[^1] + "ed");
            }
        }

        foreach (var form in forms.Distinct().OrderByDescending(f => f.Length))
            yield return form;
    }

    private static bool IsVowel(char c) => "aeiou".Contains(char.ToLowerInvariant(c));

    private static int IndexOfWord(string haystack, string needle)
    {
        var from = 0;
        while (from <= haystack.Length - needle.Length)
        {
            var index = haystack.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return -1;

            var beforeOk = index == 0 || !IsWordChar(haystack[index - 1]);
            var afterIndex = index + needle.Length;
            var afterOk = afterIndex >= haystack.Length || !IsWordChar(haystack[afterIndex]);

            if (beforeOk && afterOk) return index;
            from = index + 1;
        }
        return -1;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '\'' or '-';

    /// <summary>Case-insensitive Levenshtein distance, used to grade typed answers.</summary>
    public static int Distance(string a, string b)
    {
        a = a.Trim().ToLowerInvariant();
        b = b.Trim().ToLowerInvariant();

        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
