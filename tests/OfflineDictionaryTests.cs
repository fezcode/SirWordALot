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
    public void LookupIsCaseAndWhitespaceInsensitive()
    {
        Assert.Equal(Dictionary.Translate("book"), Dictionary.Translate("  BOOK "));
        Assert.True(Dictionary.Contains("Book"));
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
