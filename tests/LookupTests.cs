using System.Net;
using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class LookupTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private const string DatamuseJson = """
        [{"word":"prominence","score":900},{"word":"renown","score":800},{"word":"","score":1}]
        """;

    private const string DictionaryJson = """
        [{
          "word":"eminence",
          "phonetic":"/ˈɛmɪnəns/",
          "phonetics":[{"text":"/ɛmɪnəns/","audio":""},
                       {"text":"/ˈɛmɪnəns/","audio":"https://example.invalid/eminence.mp3"}],
          "meanings":[
            {"partOfSpeech":"noun",
             "definitions":[{"definition":"A position of superiority.","example":"She rose to eminence."},
                            {"definition":"A piece of high ground."}],
             "synonyms":["prominence"],"antonyms":["obscurity"]}
          ]
        }]
        """;

    [Fact]
    public void ParseDatamuseReturnsWordsAndDropsBlanks() =>
        Assert.Equal(new[] { "prominence", "renown" }, Lookup.ParseDatamuse(DatamuseJson));

    [Fact]
    public void ParseDatamuseToleratesRubbish()
    {
        Assert.Empty(Lookup.ParseDatamuse("not json"));
        Assert.Empty(Lookup.ParseDatamuse("{}"));
        Assert.Empty(Lookup.ParseDatamuse("[]"));
    }

    [Fact]
    public void ParseDictionaryExtractsPronunciationAudioAndSenses()
    {
        var (pronunciation, audio, senses) = Lookup.ParseDictionary(DictionaryJson);

        Assert.Equal("/ˈɛmɪnəns/", pronunciation);
        Assert.Equal("https://example.invalid/eminence.mp3", audio);
        Assert.Equal(2, senses.Count);
        Assert.Equal("noun", senses[0].PartOfSpeech);
        Assert.Equal("A position of superiority.", senses[0].Definition);
        Assert.Equal(new[] { "She rose to eminence." }, senses[0].Examples);
        Assert.Empty(senses[1].Examples);
    }

    [Fact]
    public void ParseDictionaryToleratesRubbish()
    {
        Assert.Empty(Lookup.ParseDictionary("not json").Senses);
        Assert.Empty(Lookup.ParseDictionary("""{"title":"No Definitions Found"}""").Senses);
    }

    [Fact]
    public async Task LookupCombinesBothServices()
    {
        var lookup = new Lookup(new StubHandler(request =>
            request.RequestUri!.Host.Contains("datamuse") ? Ok(DatamuseJson) : Ok(DictionaryJson)));

        var result = await lookup.LookupAsync("eminence", CancellationToken.None);

        Assert.True(result.Online);
        Assert.Equal("eminence", result.Word);
        Assert.Equal("/ˈɛmɪnəns/", result.Pronunciation);
        Assert.Equal(2, result.Senses.Count);
        Assert.Equal(new[] { "prominence", "renown" }, result.Relations[RelationKind.Synonym]);
        Assert.NotEmpty(result.Relations[RelationKind.Antonym]);
        Assert.NotEmpty(result.Relations[RelationKind.Related]);
        Assert.NotEmpty(result.Relations[RelationKind.Rhyme]);
    }

    [Fact]
    public async Task ANetworkFailureProducesAnOfflineResultRatherThanThrowing()
    {
        var lookup = new Lookup(new StubHandler(_ => throw new HttpRequestException("no route")));

        var result = await lookup.LookupAsync("eminence", CancellationToken.None);

        Assert.False(result.Online);
        Assert.Equal("eminence", result.Word);
        Assert.Empty(result.Senses);
        Assert.All(result.Relations.Values, words => Assert.Empty(words));
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task DictionaryFailureStillKeepsThesaurusResults()
    {
        var lookup = new Lookup(new StubHandler(request =>
            request.RequestUri!.Host.Contains("datamuse")
                ? Ok(DatamuseJson)
                : new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await lookup.LookupAsync("eminence", CancellationToken.None);

        Assert.True(result.Online);
        Assert.Empty(result.Senses);
        Assert.Equal(new[] { "prominence", "renown" }, result.Relations[RelationKind.Synonym]);
    }

    [Fact]
    public async Task ThesaurusFailureStillKeepsDictionaryResults()
    {
        var lookup = new Lookup(new StubHandler(request =>
            request.RequestUri!.Host.Contains("datamuse")
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Ok(DictionaryJson)));

        var result = await lookup.LookupAsync("eminence", CancellationToken.None);

        Assert.True(result.Online);
        Assert.Equal(2, result.Senses.Count);
        Assert.Empty(result.Relations[RelationKind.Synonym]);
    }

    [Fact]
    public async Task ABlankWordIsRejectedWithoutTouchingTheNetwork()
    {
        var called = false;
        var lookup = new Lookup(new StubHandler(_ => { called = true; return Ok("[]"); }));

        var result = await lookup.LookupAsync("   ", CancellationToken.None);

        Assert.False(called);
        Assert.False(result.Online);
    }

    [Fact]
    public void OfflineResultCarriesEveryRelationKindAsAnEmptyList()
    {
        var result = Lookup.Offline("eminence", "No network.");

        Assert.False(result.Online);
        Assert.Equal(4, result.Relations.Count);
        Assert.All(result.Relations.Values, words => Assert.Empty(words));
    }
}
