using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class LibraryEntryTests
{
    private static Library NewLibrary() => new(Db.OpenMemory());

    [Theory]
    [InlineData("Eminence", "eminence")]
    [InlineData("  eminence  ", "eminence")]
    [InlineData("Force  Majeure", "force majeure")]
    [InlineData("ÉLAN", "élan")]
    public void NormalizeLowercasesTrimsAndCollapsesWhitespace(string input, string expected) =>
        Assert.Equal(expected, Library.Normalize(input));

    [Fact]
    public void AddEntryStoresTheHeadwordAsTyped()
    {
        var library = NewLibrary();
        var id = library.AddEntry("Eminence");
        var entry = library.GetEntry(id);

        Assert.NotNull(entry);
        Assert.Equal("Eminence", entry!.Headword);
        Assert.Equal("eminence", entry.Normalized);
        Assert.Equal("manual", entry.Source);
        Assert.False(entry.Starred);
        Assert.False(entry.Archived);
    }

    [Fact]
    public void AddEntryCreatesASchedulingRowDueImmediately()
    {
        var library = NewLibrary();
        var id = library.AddEntry("eminence");
        var scheduling = library.GetScheduling(id);

        Assert.Equal(0, scheduling.IntervalDays);
        Assert.Equal(0, scheduling.Repetitions);
        Assert.Equal(Scheduler.StartEase, scheduling.Ease);
        Assert.True(scheduling.DueUtc <= DateTime.UtcNow);
        Assert.Null(scheduling.LastReviewedUtc);
    }

    [Fact]
    public void AddEntryReturnsTheExistingIdForADuplicate()
    {
        var library = NewLibrary();
        var first = library.AddEntry("Eminence");
        var second = library.AddEntry("  eminence ");

        Assert.Equal(first, second);
        Assert.Equal(1, library.Count);
    }

    [Fact]
    public void AddEntryRejectsBlankHeadwords()
    {
        var library = NewLibrary();
        Assert.Throws<ArgumentException>(() => library.AddEntry("   "));
    }

    [Fact]
    public void FindEntryIsCaseAndWhitespaceInsensitive()
    {
        var library = NewLibrary();
        library.AddEntry("Eminence");

        Assert.NotNull(library.FindEntry("EMINENCE"));
        Assert.NotNull(library.FindEntry(" eminence "));
        Assert.Null(library.FindEntry("laconic"));
    }

    [Fact]
    public void UpdateEntryPersistsFieldsAndRenormalizesTheHeadword()
    {
        var library = NewLibrary();
        var id = library.AddEntry("eminence");
        var entry = library.GetEntry(id)!;

        library.UpdateEntry(entry with
        {
            Headword = "Eminence Grise",
            Pronunciation = "/ˈɛmɪnəns ɡriːz/",
            PartOfSpeech = "noun",
            Definition = "A power behind the throne.",
            Notes = "From Richelieu.",
        });

        var updated = library.GetEntry(id)!;
        Assert.Equal("Eminence Grise", updated.Headword);
        Assert.Equal("eminence grise", updated.Normalized);
        Assert.Equal("noun", updated.PartOfSpeech);
        Assert.Equal("A power behind the throne.", updated.Definition);
        Assert.Equal("From Richelieu.", updated.Notes);
        Assert.True(updated.ModifiedUtc >= updated.CreatedUtc);
    }

    [Fact]
    public void AllEntriesHidesArchivedUnlessAsked()
    {
        var library = NewLibrary();
        var kept = library.AddEntry("eminence");
        var shelved = library.AddEntry("laconic");
        library.SetArchived(shelved, true);

        Assert.Equal(new[] { kept }, library.AllEntries().Select(e => e.Id));
        Assert.Equal(2, library.AllEntries(includeArchived: true).Count);
    }

    [Fact]
    public void DeleteEntryRemovesItAndItsSchedulingRow()
    {
        var library = NewLibrary();
        var id = library.AddEntry("eminence");
        library.DeleteEntry(id);

        Assert.Null(library.GetEntry(id));
        Assert.Equal(0, library.Count);
    }

    [Fact]
    public void MutationsRaiseChanged()
    {
        var library = NewLibrary();
        var raised = 0;
        library.Changed += () => raised++;

        var id = library.AddEntry("eminence");
        library.SetStarred(id, true);
        library.DeleteEntry(id);

        Assert.Equal(3, raised);
    }

    [Fact]
    public void SetStarredTogglesTheFlag()
    {
        var library = NewLibrary();
        var id = library.AddEntry("eminence");

        library.SetStarred(id, true);
        Assert.True(library.GetEntry(id)!.Starred);

        library.SetStarred(id, false);
        Assert.False(library.GetEntry(id)!.Starred);
    }
}
