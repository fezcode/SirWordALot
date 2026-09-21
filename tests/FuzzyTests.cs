using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class FuzzyTests
{
    [Fact]
    public void AnEmptyQueryMatchesEverything() => Assert.True(Fuzzy.Score("Library", "") >= 0);

    [Fact]
    public void AnExactPrefixScoresHigherThanAScatteredMatch() =>
        Assert.True(Fuzzy.Score("library", "lib") > Fuzzy.Score("loose bank", "lib"));

    [Fact]
    public void MatchingIsCaseInsensitive() => Assert.True(Fuzzy.Score("Library", "LIB") >= 0);

    [Fact]
    public void CharactersMustAppearInOrder()
    {
        Assert.True(Fuzzy.Score("library", "lry") >= 0);
        Assert.Equal(-1, Fuzzy.Score("library", "yrl"));
    }

    [Fact]
    public void AMissingCharacterFails() => Assert.Equal(-1, Fuzzy.Score("library", "libz"));

    [Fact]
    public void ConsecutiveMatchesBeatGappedOnes() =>
        Assert.True(Fuzzy.Score("sentences", "sent") > Fuzzy.Score("settings", "sent"));
}
