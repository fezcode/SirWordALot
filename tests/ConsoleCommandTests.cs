using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class ConsoleCommandTests
{
    private static ConsoleCommands NewConsole(out Library library)
    {
        var db = Db.OpenMemory();
        library = new Library(db);
        return new ConsoleCommands(library, new Search(db), new Stats(db), new Settings(db));
    }

    [Fact]
    public void HelpListsEveryCommand()
    {
        var console = NewConsole(out _);
        var output = string.Join('\n', console.Run("help"));

        foreach (var name in console.Names) Assert.Contains(name, output);
    }

    [Fact]
    public void AddCreatesAWord()
    {
        var console = NewConsole(out var library);
        var output = console.Run("add eminence");

        Assert.Equal(1, library.Count);
        Assert.Contains("eminence", string.Join(' ', output));
    }

    [Fact]
    public void AddIsIdempotent()
    {
        var console = NewConsole(out var library);
        console.Run("add eminence");
        var output = console.Run("add eminence");

        Assert.Equal(1, library.Count);
        Assert.Contains("already", string.Join(' ', output), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindSearchesTheLibrary()
    {
        var console = NewConsole(out var library);
        var id = library.AddEntry("eminence");
        library.UpdateEntry(library.GetEntry(id)! with { Definition = "A position of superiority." });

        Assert.Contains("eminence", string.Join(' ', console.Run("find superiority")));
        Assert.Contains("No matches", string.Join(' ', console.Run("find zzzz")));
    }

    [Fact]
    public void ShowPrintsTheEntry()
    {
        var console = NewConsole(out var library);
        var id = library.AddEntry("eminence");
        library.UpdateEntry(library.GetEntry(id)! with { PartOfSpeech = "noun", Definition = "Fame." });
        library.AddRelation(id, RelationKind.Synonym, "renown");

        var output = string.Join('\n', console.Run("show eminence"));

        Assert.Contains("noun", output);
        Assert.Contains("Fame.", output);
        Assert.Contains("renown", output);
    }

    [Fact]
    public void SentenceAttachesToAWord()
    {
        var console = NewConsole(out var library);
        library.AddEntry("eminence");

        console.Run("sentence eminence She rose to eminence.");

        Assert.Single(library.SentencesFor(library.FindEntry("eminence")!.Id));
    }

    [Fact]
    public void TagAndUntagWork()
    {
        var console = NewConsole(out var library);
        library.AddEntry("eminence");

        console.Run("tag eminence latinate");
        Assert.Single(library.TagsFor(library.FindEntry("eminence")!.Id));

        console.Run("untag eminence latinate");
        Assert.Empty(library.TagsFor(library.FindEntry("eminence")!.Id));
    }

    [Fact]
    public void DueAndStatsReport()
    {
        var console = NewConsole(out var library);
        library.AddEntry("eminence");

        Assert.Contains("eminence", string.Join(' ', console.Run("due")));
        Assert.Contains("1", string.Join(' ', console.Run("stats")));
    }

    [Fact]
    public void ExportWritesAFile()
    {
        var console = NewConsole(out var library);
        library.AddEntry("eminence");
        var path = Path.Combine(Path.GetTempPath(), $"swl-{Guid.NewGuid():N}.json");

        try
        {
            console.Run($"export json {path}");
            Assert.True(File.Exists(path));
            Assert.Contains("eminence", File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ImportReadsAFile()
    {
        var console = NewConsole(out var library);
        library.AddEntry("eminence");
        var path = Path.Combine(Path.GetTempPath(), $"swl-{Guid.NewGuid():N}.json");

        try
        {
            console.Run($"export json {path}");
            var other = NewConsole(out var target);
            other.Run($"import {path}");
            Assert.Equal(1, target.Count);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankInputProducesNoOutput(string line) => Assert.Empty(NewConsole(out _).Run(line));

    [Fact]
    public void AnUnknownCommandSuggestsHelp() =>
        Assert.Contains("help", string.Join(' ', NewConsole(out _).Run("frobnicate")),
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void ACommandMissingItsArgumentExplainsItself() =>
        Assert.Contains("Usage", string.Join(' ', NewConsole(out _).Run("add")));

    [Fact]
    public void ShowOnAnUnknownWordSaysSo() =>
        Assert.Contains("not in the library", string.Join(' ', NewConsole(out _).Run("show eminence")),
            StringComparison.OrdinalIgnoreCase);
}
