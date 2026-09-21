using System.Text.Json;

namespace SirWordALot.Core;

public sealed record LookupSense(string PartOfSpeech, string Definition, IReadOnlyList<string> Examples);

public sealed record LookupResult(
    string Word,
    string? Pronunciation,
    string? AudioUrl,
    IReadOnlyList<LookupSense> Senses,
    IReadOnlyDictionary<RelationKind, IReadOnlyList<string>> Relations,
    bool Online,
    string? Message);

public interface ILookup
{
    Task<LookupResult> LookupAsync(string word, CancellationToken ct);
}

/// <summary>Datamuse for relations, dictionaryapi.dev for senses. Never throws:
/// any failure becomes an offline result carrying a message.</summary>
public sealed class Lookup : ILookup
{
    private const string DatamuseBase = "https://api.datamuse.com/words";
    private const string DictionaryBase = "https://api.dictionaryapi.dev/api/v2/entries/en";

    private static readonly RelationKind[] Kinds =
        [RelationKind.Synonym, RelationKind.Antonym, RelationKind.Related, RelationKind.Rhyme];

    private readonly HttpClient _http;

    public Lookup(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(6);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SirWordALot/0.1");
    }

    public static LookupResult Offline(string word, string message) => new(
        word, null, null, [],
        Kinds.ToDictionary(k => k, _ => (IReadOnlyList<string>)[]),
        Online: false, message);

    public async Task<LookupResult> LookupAsync(string word, CancellationToken ct)
    {
        var term = word.Trim();
        if (term.Length == 0) return Offline(word, "Enter a word to look up.");

        var relations = new Dictionary<RelationKind, IReadOnlyList<string>>();
        var anySucceeded = false;

        foreach (var (kind, parameter) in new[]
        {
            (RelationKind.Synonym, "rel_syn"),
            (RelationKind.Antonym, "rel_ant"),
            (RelationKind.Related, "ml"),
            (RelationKind.Rhyme, "rel_rhy"),
        })
        {
            var body = await GetAsync($"{DatamuseBase}?{parameter}={Uri.EscapeDataString(term)}&max=24", ct);
            relations[kind] = body is null ? [] : ParseDatamuse(body);
            anySucceeded |= body is not null;
        }

        var dictionaryBody = await GetAsync($"{DictionaryBase}/{Uri.EscapeDataString(term)}", ct);
        anySucceeded |= dictionaryBody is not null;

        var (pronunciation, audio, senses) = dictionaryBody is null
            ? (null, null, (IReadOnlyList<LookupSense>)[])
            : ParseDictionary(dictionaryBody);

        return anySucceeded
            ? new LookupResult(term, pronunciation, audio, senses, relations, Online: true, Message: null)
            : Offline(term, "Lookup unavailable. Check your connection or add the word by hand.");
    }

    private async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException
                                       or UriFormatException or IOException)
        {
            return null;
        }
    }

    public static IReadOnlyList<string> ParseDatamuse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

            return document.RootElement.EnumerateArray()
                .Select(element => element.TryGetProperty("word", out var word) ? word.GetString() : null)
                .Where(word => !string.IsNullOrWhiteSpace(word))
                .Select(word => word!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException) { return []; }
    }

    public static (string? Pronunciation, string? AudioUrl, IReadOnlyList<LookupSense> Senses)
        ParseDictionary(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return (null, null, []);

            string? pronunciation = null;
            string? audio = null;
            var senses = new List<LookupSense>();

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (pronunciation is null && entry.TryGetProperty("phonetic", out var phonetic))
                    pronunciation = Blank(phonetic.GetString());

                if (entry.TryGetProperty("phonetics", out var phonetics) &&
                    phonetics.ValueKind == JsonValueKind.Array)
                    foreach (var item in phonetics.EnumerateArray())
                    {
                        if (audio is null && item.TryGetProperty("audio", out var a))
                            audio = Blank(a.GetString());
                        if (pronunciation is null && item.TryGetProperty("text", out var t))
                            pronunciation = Blank(t.GetString());
                    }

                if (!entry.TryGetProperty("meanings", out var meanings) ||
                    meanings.ValueKind != JsonValueKind.Array) continue;

                foreach (var meaning in meanings.EnumerateArray())
                {
                    var partOfSpeech = meaning.TryGetProperty("partOfSpeech", out var pos)
                        ? pos.GetString() ?? "" : "";

                    if (!meaning.TryGetProperty("definitions", out var definitions) ||
                        definitions.ValueKind != JsonValueKind.Array) continue;

                    foreach (var definition in definitions.EnumerateArray())
                    {
                        var text = definition.TryGetProperty("definition", out var d) ? Blank(d.GetString()) : null;
                        if (text is null) continue;

                        var examples = definition.TryGetProperty("example", out var e) && Blank(e.GetString()) is { } x
                            ? new[] { x }
                            : [];

                        senses.Add(new LookupSense(partOfSpeech, text, examples));
                    }
                }
            }

            return (pronunciation, audio, senses);
        }
        catch (JsonException) { return (null, null, []); }

        static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
