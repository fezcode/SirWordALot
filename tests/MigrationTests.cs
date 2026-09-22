using Microsoft.Data.Sqlite;
using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class MigrationTests
{
    [Fact]
    public void AFreshDatabaseIsStampedAtTheCurrentVersion()
    {
        using var db = Db.OpenMemory();
        Assert.Equal(2, Db.SchemaVersion);
        Assert.Equal(Db.SchemaVersion, db.UserVersion);
    }

    [Fact]
    public void TheTranslationColumnExists()
    {
        using var db = Db.OpenMemory();
        var columns = db.Query("SELECT name FROM pragma_table_info('entries')", r => r.GetString(0));
        Assert.Contains("translation", columns);
    }

    [Fact]
    public void TranslationRoundTripsThroughTheLibrary()
    {
        var library = new Library(Db.OpenMemory());
        var id = library.AddEntry("eminence");

        library.UpdateEntry(library.GetEntry(id)! with { Translation = "saygınlık, yücelik" });

        Assert.Equal("saygınlık, yücelik", library.GetEntry(id)!.Translation);
        Assert.Equal("saygınlık, yücelik", library.AllEntries().Single().Translation);
    }

    [Fact]
    public void TranslationSurvivesAJsonRoundTrip()
    {
        var source = new Library(Db.OpenMemory());
        var id = source.AddEntry("eminence");
        source.UpdateEntry(source.GetEntry(id)! with { Translation = "saygınlık" });

        var target = new Library(Db.OpenMemory());
        Portability.ImportJson(target, Portability.ExportJson(source), MergePolicy.TakeTheirs);

        Assert.Equal("saygınlık", target.FindEntry("eminence")!.Translation);
    }

    [Fact]
    public void AVersionOneDatabaseIsUpgradedWithoutLosingRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"swl-v1-{Guid.NewGuid():N}.db");
        try
        {
            // Build a v1-shaped database by hand: the entries table without the
            // translation column, stamped at user_version 1.
            using (var raw = new SqliteConnection($"Data Source={path}"))
            {
                raw.Open();
                using var command = raw.CreateCommand();
                command.CommandText = """
                    CREATE TABLE entries (
                      id INTEGER PRIMARY KEY, headword TEXT NOT NULL, normalized TEXT NOT NULL UNIQUE,
                      pronunciation TEXT, audio_url TEXT, part_of_speech TEXT, definition TEXT,
                      notes TEXT, starred INTEGER NOT NULL DEFAULT 0, archived INTEGER NOT NULL DEFAULT 0,
                      source TEXT NOT NULL, created_utc TEXT NOT NULL, modified_utc TEXT NOT NULL);
                    INSERT INTO entries (headword, normalized, source, created_utc, modified_utc)
                      VALUES ('eminence','eminence','manual','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
                    PRAGMA user_version = 1;
                    """;
                command.ExecuteNonQuery();
            }

            using var db = Db.Open(path);
            Assert.Equal(Db.SchemaVersion, db.UserVersion);

            var columns = db.Query("SELECT name FROM pragma_table_info('entries')", r => r.GetString(0));
            Assert.Contains("translation", columns);
            Assert.Equal(1L, (long)db.Scalar("SELECT COUNT(*) FROM entries")!);
            Assert.Null(db.Scalar("SELECT translation FROM entries"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var stray in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*"))
                try { File.Delete(stray); } catch (IOException) { }
        }
    }
}
