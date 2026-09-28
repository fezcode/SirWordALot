using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class OfflineDictionaryTests
{
    private static readonly OfflineDictionary Dictionary = OfflineDictionary.Instance;

    [Fact]
    public void TheBundledDictionaryLoads()
    {
        Assert.True(Dictionary.IsAvailable);
        Assert.True(Dictionary.Count > 30000, $"expected 30k+ headwords, got {Dictionary.Count}");
    }

    [Theory]
    [InlineData("eminence")]
    [InlineData("book")]
    [InlineData("stubborn")]
    [InlineData("abandon")]
    public void CommonWordsHaveTranslations(string word)
    {
        var translations = Dictionary.Translate(word);
        Assert.NotEmpty(translations);
        Assert.All(translations, t => Assert.False(string.IsNullOrWhiteSpace(t)));
    }

    [Fact]
    public void TranslationForJoinsMeaningsTheWayAnEntryStoresThem()
    {
        Assert.Equal(string.Join(", ", Dictionary.Translate("book")), Dictionary.TranslationFor("book"));
        Assert.Null(Dictionary.TranslationFor("zzqqxx"));
    }

    [Fact]
    public void LookupIsCaseAndWhitespaceInsensitive()
    {
        Assert.Equal(Dictionary.Translate("book"), Dictionary.Translate("  BOOK "));
        Assert.True(Dictionary.Contains("Book"));
    }

    // Each British spelling below is absent from the source while its American
    // form is present, so these genuinely exercise the fallback rather than
    // resolving directly.
    [Theory]
    [InlineData("colour", "color")]
    [InlineData("honour", "honor")]
    [InlineData("favour", "favor")]
    [InlineData("labour", "labor")]
    [InlineData("analyse", "analyze")]
    [InlineData("organise", "organize")]
    [InlineData("theatre", "theater")]
    public void BritishSpellingsFallBackToTheAmericanEntry(string british, string american)
    {
        var viaBritish = Dictionary.Translate(british);
        Assert.NotEmpty(viaBritish);
        Assert.Equal(Dictionary.Translate(american), viaBritish);
    }

    [Fact]
    public void TheFallbackDoesNotInventEntriesForWordsTheSourceLacks()
    {
        // The source is a finite dictionary and misses some ordinary words; the
        // fallback must not paper over that with a wrong match.
        Assert.Empty(Dictionary.Translate("candour"));
        Assert.Empty(Dictionary.Translate("candor"));
    }

    [Fact]
    public void AKnownWordIsNotDisturbedByTheSpellingFallback()
    {
        // "core" ends in -re but is itself a headword, so it must not resolve to "coer".
        Assert.NotEmpty(Dictionary.Translate("core"));
        Assert.Equal(Dictionary.Translate("core"), Dictionary.Translate("CORE"));
    }

    [Fact]
    public void AnUnknownWordReturnsNothingRatherThanThrowing()
    {
        Assert.Empty(Dictionary.Translate("zzzzqqqx"));
        Assert.False(Dictionary.Contains("zzzzqqqx"));
        Assert.Empty(Dictionary.Translate(""));
    }

    [Fact]
    public void SuggestReturnsHeadwordsWithThePrefixInOrder()
    {
        var suggestions = Dictionary.Suggest("emin", 5);

        Assert.NotEmpty(suggestions);
        Assert.All(suggestions, s => Assert.StartsWith("emin", s, StringComparison.Ordinal));
        Assert.Equal(suggestions.OrderBy(s => s, StringComparer.Ordinal), suggestions);
    }

    [Fact]
    public void SuggestRespectsItsLimit() => Assert.True(Dictionary.Suggest("a", 7).Count <= 7);

    [Fact]
    public void SuggestOnRubbishReturnsNothing()
    {
        Assert.Empty(Dictionary.Suggest("zzzzqqqx"));
        Assert.Empty(Dictionary.Suggest(""));
        Assert.Empty(Dictionary.Suggest("a", 0));
    }

    [Fact]
    public void HeadwordsAreOrdinaryWordsNotAbbreviationStubs()
    {
        foreach (var word in Dictionary.Suggest("ab", 20))
        {
            Assert.DoesNotContain('(', word);
            Assert.DoesNotContain('.', word);
            Assert.True(word.Length >= 2);
        }
    }
}
