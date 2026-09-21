using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class SearchTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static (Library Library, Search Search) Fixture()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);

        var eminence = library.AddEntry("eminence");
        library.UpdateEntry(library.GetEntry(eminence)! with
        { PartOfSpeech = "noun", Definition = "A position of superiority or fame." });
        library.TagEntry(eminence, "latinate");
        library.AddSentence("She rose to eminence after the war.", eminence);

        var laconic = library.AddEntry("laconic");
        library.UpdateEntry(library.GetEntry(laconic)! with
        { PartOfSpeech = "adjective", Definition = "Using very few words." });
        library.TagEntry(laconic, "greek");

        var ersatz = library.AddEntry("ersatz");
        library.UpdateEntry(library.GetEntry(ersatz)! with
        { PartOfSpeech = "adjective", Definition = "An inferior substitute.", Archived = true });

        return (library, new Search(db));
    }

    [Fact]
    public void AnEmptyFilterReturnsEveryUnarchivedEntry()
    {
        var (_, search) = Fixture();
        var hits = search.Run(new SearchFilter(), Now);

        Assert.Equal(2, hits.Count);
        Assert.DoesNotContain(hits, h => h.Entry.Headword == "ersatz");
    }

    [Fact]
    public void IncludeArchivedBringsBackArchivedEntries()
    {
        var (_, search) = Fixture();
        Assert.Equal(3, search.Run(new SearchFilter(IncludeArchived: true), Now).Count);
    }

    [Fact]
    public void TextMatchesTheHeadword()
    {
        var (_, search) = Fixture();
        var hits = search.Run(new SearchFilter(Text: "lacon"), Now);

        Assert.Single(hits);
        Assert.Equal("laconic", hits[0].Entry.Headword);
    }

    [Fact]
    public void TextMatchesTheDefinition()
    {
        var (_, search) = Fixture();
        var hits = search.Run(new SearchFilter(Text: "superiority"), Now);

        Assert.Single(hits);
        Assert.Equal("eminence", hits[0].Entry.Headword);
    }

    [Fact]
    public void TextMatchesASentenceAndReportsTheMatch()
    {
        var (_, search) = Fixture();
        var hits = search.Run(new SearchFilter(Text: "after the war"), Now);

        Assert.Single(hits);
        Assert.Equal("eminence", hits[0].Entry.Headword);
        Assert.Contains("after the war", hits[0].MatchedSentence);
    }

    [Fact]
    public void TextWithFtsPunctuationDoesNotThrow()
    {
        var (_, search) = Fixture();
        Assert.Empty(search.Run(new SearchFilter(Text: "\"OR NOT (x*"), Now));
    }

    [Fact]
    public void PartOfSpeechFilters()
    {
        var (_, search) = Fixture();
        var hits = search.Run(new SearchFilter(PartOfSpeech: "adjective"), Now);

        Assert.Single(hits);
        Assert.Equal("laconic", hits[0].Entry.Headword);
    }

    [Fact]
    public void TagFilters()
    {
        var (_, search) = Fixture();
        var hits = search.Run(new SearchFilter(Tag: "latinate"), Now);

        Assert.Single(hits);
        Assert.Equal("eminence", hits[0].Entry.Headword);
    }

    [Fact]
    public void StarredOnlyFilters()
    {
        var (library, search) = Fixture();
        library.SetStarred(library.FindEntry("laconic")!.Id, true);

        var hits = search.Run(new SearchFilter(StarredOnly: true), Now);
        Assert.Single(hits);
        Assert.Equal("laconic", hits[0].Entry.Headword);
    }

    [Fact]
    public void DueNowFindsNewWordsAndNotDueExcludesThem()
    {
        var (library, search) = Fixture();
        var eminence = library.FindEntry("eminence")!.Id;
        library.SaveScheduling(library.GetScheduling(eminence) with
        { IntervalDays = 10, Repetitions = 3, DueUtc = Now.AddDays(10) });

        Assert.Single(search.Run(new SearchFilter(Due: DueState.DueNow), Now));
        Assert.Equal("laconic", search.Run(new SearchFilter(Due: DueState.DueNow), Now)[0].Entry.Headword);
        Assert.Equal("eminence", search.Run(new SearchFilter(Due: DueState.NotDue), Now)[0].Entry.Headword);
    }

    [Fact]
    public void NeverReviewedFilters()
    {
        var (library, search) = Fixture();
        var eminence = library.FindEntry("eminence")!.Id;
        library.SaveScheduling(library.GetScheduling(eminence) with
        { Repetitions = 1, LastReviewedUtc = Now });

        var hits = search.Run(new SearchFilter(Due: DueState.Never), Now);
        Assert.Single(hits);
        Assert.Equal("laconic", hits[0].Entry.Headword);
    }

    [Fact]
    public void MasteryBucketFilters()
    {
        var (library, search) = Fixture();
        var eminence = library.FindEntry("eminence")!.Id;
        library.SaveScheduling(library.GetScheduling(eminence) with
        { IntervalDays = 100, Repetitions = 6, DueUtc = Now.AddDays(100) });
        library.AddReview(NewReview(eminence, Now));

        Assert.Single(search.Run(new SearchFilter(Mastery: MasteryBucket.Mastered), Now));
        Assert.Single(search.Run(new SearchFilter(Mastery: MasteryBucket.New), Now));
    }

    [Fact]
    public void SortOrdersAreApplied()
    {
        var (library, search) = Fixture();
        var eminence = library.FindEntry("eminence")!.Id;
        library.SaveScheduling(library.GetScheduling(eminence) with { DueUtc = Now.AddDays(5) });

        Assert.Equal(new[] { "eminence", "laconic" },
            search.Run(new SearchFilter(Sort: SortOrder.Alphabetic), Now).Select(h => h.Entry.Headword));
        Assert.Equal(new[] { "laconic", "eminence" },
            search.Run(new SearchFilter(Sort: SortOrder.Due), Now).Select(h => h.Entry.Headword));
    }

    [Fact]
    public void DeckFilterRestrictsToDeckMembers()
    {
        var (library, search) = Fixture();
        var deck = library.CreateDeck("Exam");
        library.AddToDeck(deck, library.FindEntry("laconic")!.Id);

        var hits = search.Run(new SearchFilter(DeckId: deck), Now);
        Assert.Single(hits);
        Assert.Equal("laconic", hits[0].Entry.Headword);
    }

    [Fact]
    public void HitsCarryMasteryAndDueDate()
    {
        var (library, search) = Fixture();
        var eminence = library.FindEntry("eminence")!.Id;
        library.SaveScheduling(library.GetScheduling(eminence) with
        { IntervalDays = 95, Repetitions = 4, DueUtc = Now.AddDays(95) });
        library.AddReview(NewReview(eminence, Now));

        var hit = search.Run(new SearchFilter(Text: "eminence"), Now).Single();
        Assert.Equal(100, hit.MasteryPercent);
        Assert.Equal(MasteryBucket.Mastered, hit.Bucket);
        Assert.Equal(Now.AddDays(95), hit.DueUtc);
    }

    private static Review NewReview(long entryId, DateTime at) =>
        new(0, entryId, QuizMode.Flip, Grade.Good, true, true, 1200, 0, 1, at);
}
