using Microsoft.Data.Sqlite;
using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class DbTests
{
    [Fact]
    public void OpenMemoryCreatesEverySchemaObject()
    {
        using var db = Db.OpenMemory();
        var names = db.Query(
            "SELECT name FROM sqlite_master WHERE type IN ('table','trigger') ORDER BY name",
            r => r.GetString(0));

        foreach (var expected in new[]
        {
            "decks", "deck_entries", "entries", "entries_fts", "entry_tags",
            "relations", "reviews", "scheduling", "sentences", "sentences_fts",
            "settings", "tags"
        })
            Assert.Contains(expected, names);
    }

    [Fact]
    public void OpenMemoryStampsSchemaVersion()
    {
        using var db = Db.OpenMemory();
        Assert.Equal(Db.SchemaVersion, db.UserVersion);
    }

    [Fact]
    public void ForeignKeysAreEnforced()
    {
        using var db = Db.OpenMemory();
        Assert.Throws<SqliteException>(() =>
        {
            db.Execute("INSERT INTO relations (entry_id, kind, word) VALUES (9999, 'synonym', 'x')");
        });
    }

    [Fact]
    public void ReopeningAnExistingDatabaseDoesNotReapplyMigrations()
    {
        var path = Path.Combine(Path.GetTempPath(), $"swl-{Guid.NewGuid():N}.db");
        try
        {
            using (var first = Db.Open(path))
                first.Execute("INSERT INTO entries (headword, normalized, source, created_utc, modified_utc) " +
                              "VALUES ('eminence','eminence','manual','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z')");
            using var second = Db.Open(path);
            Assert.Equal(1L, (long)second.Scalar("SELECT COUNT(*) FROM entries")!);
            Assert.Equal(Db.SchemaVersion, second.UserVersion);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void DefaultPathLivesUnderFezcodeAppData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "fezcode", "SirWordALot", "library.db");
        Assert.Equal(expected, Db.DefaultPath());
    }
}
