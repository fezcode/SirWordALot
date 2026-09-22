using Microsoft.Data.Sqlite;

namespace SirWordALot.Core;

/// <summary>Owns the SQLite connection, the schema, and forward migrations.</summary>
public sealed class Db : IDisposable
{
    public const int SchemaVersion = 2;

    public SqliteConnection Connection { get; }

    private SqliteTransaction? _transaction;

    private Db(SqliteConnection connection)
    {
        Connection = connection;
        Connection.Open();
        Execute("PRAGMA foreign_keys = ON");
        Migrate();
    }

    public static Db OpenMemory() =>
        new(new SqliteConnection($"Data Source=swl-{Guid.NewGuid():N};Mode=Memory;Cache=Shared"));

    public static Db Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL";
            pragma.ExecuteScalar();
        }
        connection.Close();
        return new Db(connection);
    }

    public static string DefaultPath() => Path.Combine(DataRoot(), "library.db");

    public static string DataRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "fezcode", "SirWordALot");

    public int UserVersion => System.Convert.ToInt32(Scalar("PRAGMA user_version"));

    /// <summary>Microsoft.Data.Sqlite refuses to run a command while a transaction is
    /// open unless the command is explicitly enrolled in it, so the active transaction
    /// is tracked here and attached by <see cref="Command"/>.</summary>
    public SqliteTransaction Begin()
    {
        _transaction = Connection.BeginTransaction();
        return _transaction;
    }

    public void Commit()
    {
        _transaction?.Commit();
        _transaction?.Dispose();
        _transaction = null;
    }

    public int Execute(string sql, params (string Name, object? Value)[] args)
    {
        using var command = Command(sql, args);
        return command.ExecuteNonQuery();
    }

    public object? Scalar(string sql, params (string Name, object? Value)[] args)
    {
        using var command = Command(sql, args);
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args)
    {
        using var command = Command(sql, args);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read()) rows.Add(map(reader));
        return rows;
    }

    private SqliteCommand Command(string sql, (string Name, object? Value)[] args)
    {
        var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _transaction;
        foreach (var (name, value) in args)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private void Migrate()
    {
        var from = UserVersion;
        if (from >= SchemaVersion) return;

        Begin();
        if (from < 1) Apply(SchemaV1);
        if (from < 2) Apply(SchemaV2);
        Execute($"PRAGMA user_version = {SchemaVersion}");
        Commit();
    }

    private void Apply(string schema)
    {
        foreach (var statement in schema.Split(";--", StringSplitOptions.RemoveEmptyEntries))
            if (!string.IsNullOrWhiteSpace(statement)) Execute(statement);
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        Connection.Dispose();
    }

    private const string SchemaV1 = """
        CREATE TABLE entries (
          id INTEGER PRIMARY KEY,
          headword TEXT NOT NULL,
          normalized TEXT NOT NULL UNIQUE,
          pronunciation TEXT,
          audio_url TEXT,
          part_of_speech TEXT,
          definition TEXT,
          notes TEXT,
          starred INTEGER NOT NULL DEFAULT 0,
          archived INTEGER NOT NULL DEFAULT 0,
          source TEXT NOT NULL,
          created_utc TEXT NOT NULL,
          modified_utc TEXT NOT NULL
        );--
        CREATE TABLE relations (
          id INTEGER PRIMARY KEY,
          entry_id INTEGER NOT NULL REFERENCES entries(id) ON DELETE CASCADE,
          kind TEXT NOT NULL,
          word TEXT NOT NULL,
          source TEXT,
          UNIQUE(entry_id, kind, word)
        );--
        CREATE TABLE sentences (
          id INTEGER PRIMARY KEY,
          entry_id INTEGER REFERENCES entries(id) ON DELETE CASCADE,
          text TEXT NOT NULL,
          source TEXT,
          cloze_start INTEGER,
          cloze_length INTEGER,
          created_utc TEXT NOT NULL
        );--
        CREATE TABLE tags (
          id INTEGER PRIMARY KEY,
          name TEXT NOT NULL UNIQUE
        );--
        CREATE TABLE entry_tags (
          entry_id INTEGER NOT NULL REFERENCES entries(id) ON DELETE CASCADE,
          tag_id INTEGER NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
          PRIMARY KEY (entry_id, tag_id)
        );--
        CREATE TABLE decks (
          id INTEGER PRIMARY KEY,
          name TEXT NOT NULL UNIQUE,
          description TEXT,
          filter_json TEXT,
          created_utc TEXT NOT NULL
        );--
        CREATE TABLE deck_entries (
          deck_id INTEGER NOT NULL REFERENCES decks(id) ON DELETE CASCADE,
          entry_id INTEGER NOT NULL REFERENCES entries(id) ON DELETE CASCADE,
          position INTEGER NOT NULL DEFAULT 0,
          PRIMARY KEY (deck_id, entry_id)
        );--
        CREATE TABLE scheduling (
          entry_id INTEGER PRIMARY KEY REFERENCES entries(id) ON DELETE CASCADE,
          scheduler TEXT NOT NULL DEFAULT 'sm2',
          box INTEGER NOT NULL DEFAULT 1,
          ease REAL NOT NULL DEFAULT 2.5,
          interval_days INTEGER NOT NULL DEFAULT 0,
          repetitions INTEGER NOT NULL DEFAULT 0,
          lapses INTEGER NOT NULL DEFAULT 0,
          due_utc TEXT NOT NULL,
          last_reviewed_utc TEXT
        );--
        CREATE TABLE reviews (
          id INTEGER PRIMARY KEY,
          entry_id INTEGER NOT NULL REFERENCES entries(id) ON DELETE CASCADE,
          mode TEXT NOT NULL,
          grade INTEGER NOT NULL,
          correct INTEGER NOT NULL,
          scheduled INTEGER NOT NULL DEFAULT 1,
          elapsed_ms INTEGER NOT NULL DEFAULT 0,
          interval_before INTEGER NOT NULL DEFAULT 0,
          interval_after INTEGER NOT NULL DEFAULT 0,
          reviewed_utc TEXT NOT NULL
        );--
        CREATE INDEX ix_reviews_entry ON reviews(entry_id);--
        CREATE INDEX ix_reviews_day ON reviews(reviewed_utc);--
        CREATE INDEX ix_scheduling_due ON scheduling(due_utc);--
        CREATE TABLE settings (
          key TEXT PRIMARY KEY,
          value TEXT NOT NULL
        );--
        CREATE VIRTUAL TABLE entries_fts USING fts5(
          headword, definition, notes, content='entries', content_rowid='id'
        );--
        CREATE TRIGGER entries_ai AFTER INSERT ON entries BEGIN
          INSERT INTO entries_fts(rowid, headword, definition, notes)
          VALUES (new.id, new.headword, new.definition, new.notes);
        END;--
        CREATE TRIGGER entries_ad AFTER DELETE ON entries BEGIN
          INSERT INTO entries_fts(entries_fts, rowid, headword, definition, notes)
          VALUES ('delete', old.id, old.headword, old.definition, old.notes);
        END;--
        CREATE TRIGGER entries_au AFTER UPDATE ON entries BEGIN
          INSERT INTO entries_fts(entries_fts, rowid, headword, definition, notes)
          VALUES ('delete', old.id, old.headword, old.definition, old.notes);
          INSERT INTO entries_fts(rowid, headword, definition, notes)
          VALUES (new.id, new.headword, new.definition, new.notes);
        END;--
        CREATE VIRTUAL TABLE sentences_fts USING fts5(
          text, source, content='sentences', content_rowid='id'
        );--
        CREATE TRIGGER sentences_ai AFTER INSERT ON sentences BEGIN
          INSERT INTO sentences_fts(rowid, text, source) VALUES (new.id, new.text, new.source);
        END;--
        CREATE TRIGGER sentences_ad AFTER DELETE ON sentences BEGIN
          INSERT INTO sentences_fts(sentences_fts, rowid, text, source)
          VALUES ('delete', old.id, old.text, old.source);
        END;--
        CREATE TRIGGER sentences_au AFTER UPDATE ON sentences BEGIN
          INSERT INTO sentences_fts(sentences_fts, rowid, text, source)
          VALUES ('delete', old.id, old.text, old.source);
          INSERT INTO sentences_fts(rowid, text, source) VALUES (new.id, new.text, new.source);
        END;
        """;

    /// <summary>v2 adds the translation carried by the offline dictionary, which is a
    /// meaning in another language rather than an English definition and so deserves
    /// its own column instead of being folded into one.</summary>
    private const string SchemaV2 = """
        ALTER TABLE entries ADD COLUMN translation TEXT;
        """;
}
