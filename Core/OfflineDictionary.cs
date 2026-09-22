using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace SirWordALot.Core;

/// <summary>An offline English-to-Turkish dictionary exported from atlas.dict.
///
/// The source is a translation dictionary, so it carries headwords and their
/// Turkish meanings — not English definitions, phonetics or parts of speech.
/// That is why this supplies <see cref="Translate"/> and <see cref="Suggest"/>
/// and leaves senses to the online services.
///
/// The payload is a gzipped embedded resource, decompressed once on first use
/// and never touched again if nothing asks for a word.</summary>
public sealed class OfflineDictionary
{
    private const string ResourceName = "SirWordALot.Assets.dictionary.json.gz";

    private static readonly Lazy<OfflineDictionary> Shared = new(Load, isThreadSafe: true);

    /// <summary>The exporter writes short lowercase keys to keep the asset small.</summary>
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, string[]> _byWord;
    private readonly string[] _headwords;

    public static OfflineDictionary Instance => Shared.Value;

    public int Count => _headwords.Length;

    /// <summary>False when the asset is missing, which is the case for a build that
    /// deliberately ships without it. Callers degrade rather than fail.</summary>
    public bool IsAvailable => _headwords.Length > 0;

    private OfflineDictionary(Dictionary<string, string[]> byWord, string[] headwords)
    {
        _byWord = byWord;
        _headwords = headwords;
    }

    public IReadOnlyList<string> Translate(string word) =>
        _byWord.GetValueOrDefault(Normalize(word)) ?? [];

    public bool Contains(string word) => _byWord.ContainsKey(Normalize(word));

    /// <summary>Headwords beginning with <paramref name="prefix"/>, in order. Backed by
    /// a binary search over the sorted headword list rather than a scan.</summary>
    public IReadOnlyList<string> Suggest(string prefix, int limit = 12)
    {
        var key = Normalize(prefix);
        if (key.Length == 0 || limit <= 0 || !IsAvailable) return [];

        var index = Array.BinarySearch(_headwords, key, StringComparer.Ordinal);
        if (index < 0) index = ~index;

        var matches = new List<string>(limit);
        for (var i = index; i < _headwords.Length && matches.Count < limit; i++)
        {
            if (!_headwords[i].StartsWith(key, StringComparison.Ordinal)) break;
            matches.Add(_headwords[i]);
        }
        return matches;
    }

    private static string Normalize(string word) => word.Trim().ToLowerInvariant();

    private static OfflineDictionary Load()
    {
        try
        {
            using var stream = typeof(OfflineDictionary).GetTypeInfo().Assembly
                .GetManifestResourceStream(ResourceName);

            if (stream is null) return Empty();

            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            var document = JsonSerializer.Deserialize<Document>(gzip, Json);
            if (document?.Words is not { Count: > 0 } words) return Empty();

            var byWord = new Dictionary<string, string[]>(words.Count, StringComparer.Ordinal);
            foreach (var word in words)
            {
                if (word.W is not { Length: > 0 } headword || word.T is not { Length: > 0 }) continue;
                byWord[Normalize(headword)] = word.T;
            }

            var headwords = byWord.Keys.ToArray();
            Array.Sort(headwords, StringComparer.Ordinal);
            return new OfflineDictionary(byWord, headwords);
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or IOException
                                       or NotSupportedException or BadImageFormatException)
        {
            return Empty();
        }
    }

    private static OfflineDictionary Empty() => new([], []);

    private sealed record Word(string? W, string[]? T);

    private sealed record Document(string? Source, string? Pair, int Count, List<Word>? Words);
}
