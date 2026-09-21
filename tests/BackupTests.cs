using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class BackupTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"swl-backup-{Guid.NewGuid():N}");
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }

    private static Library Seed()
    {
        var library = new Library(Db.OpenMemory());
        var id = library.AddEntry("eminence");
        library.UpdateEntry(library.GetEntry(id)! with { Definition = "Fame." });
        return library;
    }

    [Fact]
    public void WriteCreatesATimestampedSnapshot()
    {
        var backups = new Backups(_folder);
        var path = backups.Write(Seed(), Now);

        Assert.True(File.Exists(path));
        Assert.Equal("sir-word-a-lot-20260922-120000.json", Path.GetFileName(path));
        Assert.Contains("eminence", File.ReadAllText(path));
    }

    [Fact]
    public void ListReturnsNewestFirst()
    {
        var backups = new Backups(_folder);
        var library = Seed();
        backups.Write(library, Now.AddDays(-1));
        backups.Write(library, Now);

        var listed = backups.List();
        Assert.Equal(2, listed.Count);
        Assert.Contains("20260922", Path.GetFileName(listed[0]));
    }

    [Fact]
    public void PruneKeepsOnlyTheNewestN()
    {
        var backups = new Backups(_folder);
        var library = Seed();
        for (var i = 0; i < 25; i++) backups.Write(library, Now.AddMinutes(-i));

        var removed = backups.Prune(keep: 20);

        Assert.Equal(5, removed);
        Assert.Equal(20, backups.List().Count);
        Assert.Contains("120000", Path.GetFileName(backups.List()[0]));
    }

    [Fact]
    public void PruneIgnoresUnrelatedFiles()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "keep me");
        var backups = new Backups(_folder);
        backups.Write(Seed(), Now);

        backups.Prune(keep: 1);

        Assert.True(File.Exists(Path.Combine(_folder, "notes.txt")));
    }

    [Fact]
    public void RestoreReplacesTheLibraryFromASnapshot()
    {
        var backups = new Backups(_folder);
        var path = backups.Write(Seed(), Now);

        var target = new Library(Db.OpenMemory());
        target.AddEntry("laconic");

        var report = backups.Restore(target, path);

        Assert.Equal(1, report.Added);
        Assert.NotNull(target.FindEntry("eminence"));
        Assert.Null(target.FindEntry("laconic"));
    }

    [Fact]
    public void WriteAtomicLeavesNoTemporaryFileBehind()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "data.json");

        Backups.WriteAtomic(path, "first");
        Backups.WriteAtomic(path, "second");

        Assert.Equal("second", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_folder));
    }
}
