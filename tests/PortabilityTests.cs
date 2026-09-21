using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class PortabilityTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static Library Populated()
    {
        var library = new Library(Db.OpenMemory());
        var id = library.AddEntry("Eminence", source: "lookup");
        library.UpdateEntry(library.GetEntry(id)! with
        {
            Pronunciation = "/ˈɛmɪnəns/",
            AudioUrl = "https://example.invalid/eminence.mp3",
            PartOfSpeech = "noun",
            Definition = "A position of superiority.",
            Notes = "Compare \"grise\".",
            Starred = true,
        });
        library.AddRelation(id, RelationKind.Synonym, "prominence");
        library.AddRelation(id, RelationKind.Synonym, "renown");
        library.AddRelation(id, RelationKind.Antonym, "obscurity");
        library.TagEntry(id, "latinate");
        library.AddSentence("She rose to eminence.", id, "Gibbon");
        library.UpdateSentence(library.SentencesFor(id).Single() with { ClozeStart = 12, ClozeLength = 8 });
        library.SaveScheduling(library.GetScheduling(id) with
        { IntervalDays = 6, Repetitions = 2, Ease = 2.35, LastReviewedUtc = Now, DueUtc = Now.AddDays(6) });
        library.AddReview(new Review(0, id, QuizMode.Cloze, Grade.Good, true, true, 2400, 1, 6, Now));

        library.AddSentence("A loose sentence with no word attached.");
        var deck = library.CreateDeck("Exam", "For Tuesday");
        library.AddToDeck(deck, id);
        return library;
    }

    [Fact]
    public void JsonRoundTripReproducesTheLibraryExactly()
    {
        var source = Populated();
        var json = Portability.ExportJson(source);

        var target = new Library(Db.OpenMemory());
        var report = Portability.ImportJson(target, json, MergePolicy.TakeTheirs);

        Assert.Equal(1, report.Added);

        var entry = target.FindEntry("eminence")!;
        Assert.Equal("Eminence", entry.Headword);
        Assert.Equal("/ˈɛmɪnəns/", entry.Pronunciation);
        Assert.Equal("https://example.invalid/eminence.mp3", entry.AudioUrl);
        Assert.Equal("noun", entry.PartOfSpeech);
        Assert.Equal("A position of superiority.", entry.Definition);
        Assert.Equal("Compare \"grise\".", entry.Notes);
        Assert.True(entry.Starred);

        Assert.Equal(3, target.Relations(entry.Id).Count);
        Assert.Equal(new[] { "latinate" }, target.TagsFor(entry.Id));

        var sentence = target.SentencesFor(entry.Id).Single();
        Assert.Equal("She rose to eminence.", sentence.Text);
        Assert.Equal("Gibbon", sentence.Source);
        Assert.Equal(12, sentence.ClozeStart);
        Assert.Equal(8, sentence.ClozeLength);

        var scheduling = target.GetScheduling(entry.Id);
        Assert.Equal(6, scheduling.IntervalDays);
        Assert.Equal(2, scheduling.Repetitions);
        Assert.Equal(2.35, scheduling.Ease, 3);

        Assert.Single(target.ReviewsFor(entry.Id));
        Assert.Single(target.AllSentences(looseOnly: true));
        Assert.Equal("Exam", target.Decks().Single().Name);
        Assert.Single(target.DeckEntries(target.Decks().Single().Id));
    }

    [Fact]
    public void ExportedJsonCarriesItsFormatVersion()
    {
        using var document = System.Text.Json.JsonDocument.Parse(Portability.ExportJson(Populated()));
        Assert.Equal(Portability.FormatVersion, document.RootElement.GetProperty("Version").GetInt32());
    }

    [Fact]
    public void KeepMineLeavesTheExistingEntryUntouched()
    {
        var json = Portability.ExportJson(Populated());
        var target = new Library(Db.OpenMemory());
        var id = target.AddEntry("eminence");
        target.UpdateEntry(target.GetEntry(id)! with { Definition = "Mine." });

        var report = Portability.ImportJson(target, json, MergePolicy.KeepMine);

        Assert.Equal(0, report.Added);
        Assert.Equal(1, report.Skipped);
        Assert.Single(report.Conflicts);
        Assert.Equal("Mine.", target.GetEntry(id)!.Definition);
    }

    [Fact]
    public void TakeTheirsOverwritesTheExistingEntry()
    {
        var json = Portability.ExportJson(Populated());
        var target = new Library(Db.OpenMemory());
        var id = target.AddEntry("eminence");
        target.UpdateEntry(target.GetEntry(id)! with { Definition = "Mine." });

        var report = Portability.ImportJson(target, json, MergePolicy.TakeTheirs);

        Assert.Equal(1, report.Updated);
        Assert.Equal("A position of superiority.", target.GetEntry(id)!.Definition);
    }

    [Fact]
    public void KeepBothCreatesASuffixedEntry()
    {
        var json = Portability.ExportJson(Populated());
        var target = new Library(Db.OpenMemory());
        target.AddEntry("eminence");

        var report = Portability.ImportJson(target, json, MergePolicy.KeepBoth);

        Assert.Equal(1, report.Added);
        Assert.NotNull(target.FindEntry("eminence (imported)"));
        Assert.Equal(2, target.Count);
    }

    [Fact]
    public void ImportingMalformedJsonThrowsARecognisableError()
    {
        var target = new Library(Db.OpenMemory());
        Assert.Throws<InvalidDataException>(() =>
            Portability.ImportJson(target, "{ not json", MergePolicy.TakeTheirs));
    }

    [Fact]
    public void ImportingAFutureFormatVersionThrows()
    {
        var target = new Library(Db.OpenMemory());
        Assert.Throws<InvalidDataException>(() =>
            Portability.ImportJson(target, """{"Version":99,"ExportedUtc":"x","Entries":[]}""",
                MergePolicy.TakeTheirs));
    }

    [Fact]
    public void CsvQuotesFieldsContainingCommasQuotesAndNewlines()
    {
        var library = new Library(Db.OpenMemory());
        var id = library.AddEntry("eminence");
        library.UpdateEntry(library.GetEntry(id)! with
        { Definition = "Fame, renown", Notes = "He said \"yes\"\nthen left." });

        var csv = Portability.ExportCsv(library);

        Assert.Contains("\"Fame, renown\"", csv);
        Assert.Contains("\"He said \"\"yes\"\"\nthen left.\"", csv);
    }

    [Fact]
    public void CsvRoundTripsTheFlatFields()
    {
        var csv = Portability.ExportCsv(Populated());
        var target = new Library(Db.OpenMemory());

        var report = Portability.ImportCsv(target, csv, MergePolicy.TakeTheirs);

        Assert.Equal(1, report.Added);
        var entry = target.FindEntry("eminence")!;
        Assert.Equal("noun", entry.PartOfSpeech);
        Assert.Equal("A position of superiority.", entry.Definition);
        Assert.Equal(new[] { "prominence", "renown" },
            target.Relations(entry.Id).Where(r => r.Kind == RelationKind.Synonym)
                  .Select(r => r.Word).Order());
        Assert.Equal(new[] { "latinate" }, target.TagsFor(entry.Id));
        Assert.Single(target.SentencesFor(entry.Id));
    }

    [Fact]
    public void CsvImportSkipsTheHeaderAndBlankRows()
    {
        var target = new Library(Db.OpenMemory());
        var csv = "headword,pronunciation,part_of_speech,definition,notes,starred,tags,synonyms,antonyms,related,sentences\n" +
                  "laconic,,adjective,Terse.,,false,,,,,\n" +
                  "\n";

        var report = Portability.ImportCsv(target, csv, MergePolicy.TakeTheirs);

        Assert.Equal(1, report.Added);
        Assert.Equal(1, target.Count);
    }

    [Fact]
    public void AnkiTsvHasThreeTabSeparatedColumnsPerEntry()
    {
        var tsv = Portability.ExportAnkiTsv(Populated());
        var line = tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Single();
        var columns = line.Split('\t');

        Assert.Equal(3, columns.Length);
        Assert.Equal("Eminence", columns[0]);
        Assert.Contains("A position of superiority.", columns[1]);
        Assert.Contains("prominence", columns[1]);
        Assert.Equal("latinate", columns[2]);
    }

    [Fact]
    public void AnkiTsvStripsTabsAndNewlinesFromFields()
    {
        var library = new Library(Db.OpenMemory());
        var id = library.AddEntry("eminence");
        library.UpdateEntry(library.GetEntry(id)! with { Definition = "Fame\there\nand there" });

        var line = Portability.ExportAnkiTsv(library).TrimEnd('\n');
        Assert.Equal(3, line.Split('\t').Length);
        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void ExportingAnEmptyLibraryProducesValidOutput()
    {
        var empty = new Library(Db.OpenMemory());

        Assert.Equal(Portability.FormatVersion,
            System.Text.Json.JsonDocument.Parse(Portability.ExportJson(empty))
                .RootElement.GetProperty("Version").GetInt32());
        Assert.StartsWith("headword,", Portability.ExportCsv(empty));
        Assert.Equal("", Portability.ExportAnkiTsv(empty));
    }
}
