using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class StarterLibraryTests
{
    private static Library NewLibrary() => new(Db.OpenMemory());

    [Fact]
    public void AnEmptyLibraryIsSeeded()
    {
        var library = NewLibrary();
        var added = StarterLibrary.SeedIfEmpty(library);

        Assert.Equal(StarterLibrary.WordCount, added);
        Assert.Equal(StarterLibrary.WordCount, library.Count);
        Assert.True(library.Count >= 50, $"expected a usable starter set, got {library.Count}");
    }

    [Fact]
    public void ALibraryThatAlreadyHasWordsIsLeftAlone()
    {
        var library = NewLibrary();
        library.AddEntry("mine");

        Assert.Equal(0, StarterLibrary.SeedIfEmpty(library));
        Assert.Equal(1, library.Count);
    }

    [Fact]
    public void SeedingTwiceAddsNothingTheSecondTime()
    {
        var library = NewLibrary();
        StarterLibrary.Seed(library, OfflineDictionary.Instance);
        var again = StarterLibrary.Seed(library, OfflineDictionary.Instance);

        Assert.Equal(0, again);
        Assert.Equal(StarterLibrary.WordCount, library.Count);
    }

    [Fact]
    public void EverySeededWordHasADefinitionAPartOfSpeechAndTheStarterTag()
    {
        var library = NewLibrary();
        StarterLibrary.SeedIfEmpty(library);

        foreach (var entry in library.AllEntries())
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Definition), entry.Headword);
            Assert.False(string.IsNullOrWhiteSpace(entry.PartOfSpeech), entry.Headword);
            Assert.Contains(StarterLibrary.Tag, library.TagsFor(entry.Id));
        }
    }

    [Fact]
    public void MostSeededWordsPickUpATranslationFromTheOfflineDictionary()
    {
        var library = NewLibrary();
        StarterLibrary.SeedIfEmpty(library);

        var translated = library.AllEntries().Count(e => !string.IsNullOrWhiteSpace(e.Translation));

        // Not all of them: the source is a finite dictionary, and a word missing a
        // translation is still worth studying. Most should be covered though.
        Assert.True(translated > library.Count / 2,
            $"only {translated} of {library.Count} starter words had a translation");
    }

    [Fact]
    public void SeededWordsAreAllDueImmediately()
    {
        var library = NewLibrary();
        StarterLibrary.SeedIfEmpty(library);

        Assert.Equal(library.Count, library.NewEntries(int.MaxValue).Count);
    }

    [Fact]
    public void TheStarterSetIsQuizzable()
    {
        var library = NewLibrary();
        StarterLibrary.SeedIfEmpty(library);

        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(NewPerDay: 10, Seed: 1), DateTime.UtcNow);

        Assert.Equal(10, session.Total);
        Assert.NotNull(session.Current);
        Assert.Equal(QuizMode.Choice, session.Current!.Mode);
    }
}
