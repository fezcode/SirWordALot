using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class LibraryCollectionTests
{
    private static Library NewLibrary() => new(Db.OpenMemory());

    [Fact]
    public void RelationsAreGroupedByKindAndDeduplicated()
    {
        var library = NewLibrary();
        var id = library.AddEntry("eminence");

        library.AddRelation(id, RelationKind.Synonym, "prominence");
        library.AddRelation(id, RelationKind.Synonym, "prominence");
        library.AddRelation(id, RelationKind.Synonym, "renown");
        library.AddRelation(id, RelationKind.Antonym, "obscurity");

        var relations = library.Relations(id);
        Assert.Equal(3, relations.Count);
        Assert.Equal(new[] { "prominence", "renown" },
            relations.Where(r => r.Kind == RelationKind.Synonym).Select(r => r.Word).Order());
        Assert.Single(relations, r => r.Kind == RelationKind.Antonym);
    }

    [Fact]
    public void RemoveRelationDropsOnlyTheMatchingKindAndWord()
    {
        var library = NewLibrary();
        var id = library.AddEntry("eminence");
        library.AddRelation(id, RelationKind.Synonym, "renown");
        library.AddRelation(id, RelationKind.Related, "renown");

        library.RemoveRelation(id, RelationKind.Synonym, "renown");

        Assert.Single(library.Relations(id));
        Assert.Equal(RelationKind.Related, library.Relations(id)[0].Kind);
    }

    [Fact]
    public void DeletingAnEntryCascadesToItsRelationsAndSentences()
    {
        var library = NewLibrary();
        var id = library.AddEntry("eminence");
        library.AddRelation(id, RelationKind.Synonym, "renown");
        library.AddSentence("She rose to eminence.", id);

        library.DeleteEntry(id);

        Assert.Empty(library.AllSentences());
    }

    [Fact]
    public void ASentenceCanBeStoredLooseAndLinkedLater()
    {
        var library = NewLibrary();
        var sentenceId = library.AddSentence("She rose to eminence.");

        Assert.Single(library.AllSentences(looseOnly: true));

        var entryId = library.AddEntry("eminence");
        library.LinkSentence(sentenceId, entryId);

        Assert.Empty(library.AllSentences(looseOnly: true));
        Assert.Single(library.SentencesFor(entryId));
    }

    [Fact]
    public void UnlinkingASentenceReturnsItToTheBank()
    {
        var library = NewLibrary();
        var entryId = library.AddEntry("eminence");
        var sentenceId = library.AddSentence("She rose to eminence.", entryId);

        library.LinkSentence(sentenceId, null);

        Assert.Empty(library.SentencesFor(entryId));
        Assert.Single(library.AllSentences(looseOnly: true));
    }

    [Fact]
    public void UpdateSentencePersistsTextAndClozeSpan()
    {
        var library = NewLibrary();
        var entryId = library.AddEntry("eminence");
        var sentenceId = library.AddSentence("She rose to eminence.", entryId, "Gibbon");
        var sentence = library.SentencesFor(entryId).Single();

        library.UpdateSentence(sentence with { Text = "He rose to eminence.", ClozeStart = 12, ClozeLength = 8 });

        var updated = library.SentencesFor(entryId).Single();
        Assert.Equal(sentenceId, updated.Id);
        Assert.Equal("He rose to eminence.", updated.Text);
        Assert.Equal(12, updated.ClozeStart);
        Assert.Equal(8, updated.ClozeLength);
        Assert.Equal("Gibbon", updated.Source);
    }

    [Fact]
    public void TagsAreCreatedOnDemandAndSharedBetweenEntries()
    {
        var library = NewLibrary();
        var first = library.AddEntry("eminence");
        var second = library.AddEntry("laconic");

        library.TagEntry(first, "latinate");
        library.TagEntry(second, "latinate");
        library.TagEntry(second, "greek");

        Assert.Equal(2, library.Tags().Count);
        Assert.Equal(new[] { "latinate" }, library.TagsFor(first));
        Assert.Equal(new[] { "greek", "latinate" }, library.TagsFor(second).Order());
    }

    [Fact]
    public void TaggingTwiceIsIdempotent()
    {
        var library = NewLibrary();
        var id = library.AddEntry("eminence");

        library.TagEntry(id, "latinate");
        library.TagEntry(id, "Latinate");

        Assert.Single(library.TagsFor(id));
    }

    [Fact]
    public void UntagEntryLeavesTheTagAvailableToOthers()
    {
        var library = NewLibrary();
        var first = library.AddEntry("eminence");
        var second = library.AddEntry("laconic");
        library.TagEntry(first, "latinate");
        library.TagEntry(second, "latinate");

        library.UntagEntry(first, "latinate");

        Assert.Empty(library.TagsFor(first));
        Assert.Single(library.TagsFor(second));
        Assert.Single(library.Tags());
    }

    [Fact]
    public void DecksHoldEntriesAndSurviveEntryRemoval()
    {
        var library = NewLibrary();
        var deck = library.CreateDeck("Exam", "For Tuesday");
        var first = library.AddEntry("eminence");
        var second = library.AddEntry("laconic");

        library.AddToDeck(deck, first);
        library.AddToDeck(deck, second);
        Assert.Equal(2, library.DeckEntries(deck).Count);

        library.RemoveFromDeck(deck, first);
        Assert.Single(library.DeckEntries(deck));

        library.DeleteEntry(second);
        Assert.Empty(library.DeckEntries(deck));
        Assert.Single(library.Decks());
    }

    [Fact]
    public void ASmartDeckStoresItsFilter()
    {
        var library = NewLibrary();
        var deck = library.CreateDeck("Due nouns", filterJson: """{"PartOfSpeech":"noun"}""");

        Assert.Equal("""{"PartOfSpeech":"noun"}""", library.Decks().Single(d => d.Id == deck).FilterJson);
    }

    [Fact]
    public void RenameDeckPersists()
    {
        var library = NewLibrary();
        var deck = library.CreateDeck("Exam");

        library.RenameDeck(deck, "Final exam");

        Assert.Equal("Final exam", library.Decks().Single().Name);
    }
}
