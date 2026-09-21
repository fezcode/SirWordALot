using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class ClozeTests
{
    [Fact]
    public void FindLocatesTheExactWord()
    {
        var span = Cloze.Find("She rose to eminence quickly.", "eminence");
        Assert.Equal((12, 8), span);
    }

    [Fact]
    public void FindIsCaseInsensitiveButReportsTheSentencesOwnSpan()
    {
        var span = Cloze.Find("Eminence is earned.", "eminence");
        Assert.Equal((0, 8), span);
    }

    [Theory]
    [InlineData("He obfuscates the issue.", "obfuscate", "obfuscates")]
    [InlineData("She obfuscated the issue.", "obfuscate", "obfuscated")]
    [InlineData("They are obfuscating it.", "obfuscate", "obfuscating")]
    [InlineData("The watches stopped.", "watch", "watches")]
    [InlineData("He tarried there.", "tarry", "tarried")]
    [InlineData("It parries every blow.", "parry", "parries")]
    [InlineData("He stopped abruptly.", "stop", "stopped")]
    [InlineData("She is stopping now.", "stop", "stopping")]
    public void FindMatchesInflectedForms(string sentence, string headword, string expected)
    {
        var span = Cloze.Find(sentence, headword);
        Assert.NotNull(span);
        Assert.Equal(expected, sentence.Substring(span!.Value.Start, span.Value.Length));
    }

    [Fact]
    public void FindDoesNotMatchInsideALongerWord() =>
        Assert.Null(Cloze.Find("The runner was fast.", "run"));

    [Fact]
    public void FindReturnsNullWhenTheWordIsAbsent() =>
        Assert.Null(Cloze.Find("Nothing to see here.", "eminence"));

    [Fact]
    public void FindHandlesPunctuationBoundaries()
    {
        var span = Cloze.Find("Eminence, earned slowly.", "eminence");
        Assert.Equal((0, 8), span);
    }

    [Fact]
    public void BlankReplacesTheSpanWithMatchingLowBars() =>
        Assert.Equal("She rose to ▁▁▁▁▁▁▁▁ quickly.",
            Cloze.Blank("She rose to eminence quickly.", 12, 8));

    [Fact]
    public void BlankForBlanksAnInflectedForm() =>
        Assert.Equal("He ▁▁▁▁▁▁▁▁▁▁ the issue.", Cloze.BlankFor("He obfuscates the issue.", "obfuscate"));

    [Fact]
    public void BlankForReturnsNullWhenTheWordIsAbsent() =>
        Assert.Null(Cloze.BlankFor("Nothing here.", "eminence"));

    [Fact]
    public void BlankClampsAnOutOfRangeSpanRatherThanThrowing() =>
        Assert.Equal("Short", Cloze.Blank("Short", 40, 8));

    [Theory]
    [InlineData("eminence", "eminence", 0)]
    [InlineData("eminence", "eminense", 1)]
    [InlineData("eminence", "eminance", 1)]
    [InlineData("eminence", "eminnce", 1)]
    [InlineData("eminence", "emminence", 1)]
    [InlineData("eminence", "eminanse", 2)]
    [InlineData("eminence", "laconic", 7)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    public void DistanceIsStandardLevenshtein(string a, string b, int expected) =>
        Assert.Equal(expected, Cloze.Distance(a, b));

    [Fact]
    public void DistanceIsCaseInsensitive() => Assert.Equal(0, Cloze.Distance("Eminence", "eminence"));

    [Fact]
    public void ContainsWordIsInflectionAware()
    {
        Assert.True(Cloze.ContainsWord("He obfuscates it.", "obfuscate"));
        Assert.False(Cloze.ContainsWord("The runner was fast.", "run"));
    }
}
