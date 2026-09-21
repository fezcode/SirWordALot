# Sir Word-a-lot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a windowed Avalonia vocabulary and thesaurus trainer in the HammerOS visual idiom, backed by a local SQLite library, with CRUD, a sentence bank, offline-tolerant online lookup, four quiz modes over two schedulers, tags, decks, full-text search, three export formats, backups, a command palette, a global capture hotkey, an in-app console and a stats dashboard.

**Architecture:** `Core/` is pure C# over SQLite with no Avalonia reference — every scheduling, grading, cloze, merge and search rule is a pure function or a SQL query, tested headlessly with xunit against in-memory SQLite. `Shell/` owns a frameless `Window` with hand-drawn HammerOS chrome. `Views/` compose control factories from `Shell/Ui.cs` in C# code rather than XAML, exactly the way `hammeros/Apps/FilesView.cs` does. `Library` raises a `Changed` event; views subscribe and re-render, and implement `IDisposable` to unsubscribe.

**Tech Stack:** .NET 10 (`net10.0`), Avalonia 11.3.0, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `Microsoft.Data.Sqlite` 10.0.0, xunit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-09-22-sir-word-a-lot-design.md`

## Global Constraints

- Target framework is `net10.0`. SDK present on this machine is 10.0.101.
- `Core/` MUST NOT reference Avalonia. Enforced by Task 2's test project referencing only `Core` sources.
- Data root is `%APPDATA%\fezcode\SirWordALot` — via `Environment.SpecialFolder.ApplicationData`. Never `%LOCALAPPDATA%`.
- Database file `library.db`, WAL mode, `PRAGMA foreign_keys = ON`, schema version in `PRAGMA user_version`.
- Palette, verbatim: Ink `#173B40`, Cream `#ECEDE0`, Muted `#688178`, Line `#BCC8BB`, Phosphor `#B3E5DA`, Dark `#102C32`, TitlebarActive `#E9EBDD`, TitlebarInactive `#CCD6C9`, Sidebar `#DFE4D6`, WindowBorder `#9CAEA0`, RowRule `#D8DECF`, PrimaryBg `#214B4D`, PrimaryFg `#F0F0DF`, Danger `#99513D`.
- Fonts: Inter (interface), `Cascadia Mono, Consolas` (mono), `Georgia, Times New Roman` (display).
- Zero corner radius everywhere. Uppercase mono labels at 10.5px with 1.2 letter-spacing. 43px titlebar. `◢` resize grip.
- Window: `SystemDecorations=None`, default 1180x760, minimum 1040x680.
- `-h`/`--help` and `-v`/`--version` print to console and exit.
- No network calls other than `api.datamuse.com` and `api.dictionaryapi.dev`. 6 second timeout, no automatic retry, never on the UI thread.
- Every answer in a quiz session writes its review row and scheduling update immediately.
- Commit after every task. No `Co-Authored-By` lines in commit messages.
- Verify UI headlessly only — never drive the live desktop.

## File Structure

| File | Responsibility |
| --- | --- |
| `Core/Db.cs` | Connection, schema DDL, forward migrations, in-memory factory |
| `Core/Models.cs` | Records and enums shared by all of Core |
| `Core/Library.cs` | CRUD over entries, relations, sentences, tags, decks, scheduling, reviews |
| `Core/Search.cs` | FTS5 queries plus structured filters and sort orders |
| `Core/Mastery.cs` | Derived mastery percentage and buckets |
| `Core/Scheduler.cs` | SM-2, Leitner, interval preview, scheduler conversion |
| `Core/Cloze.cs` | Inflection-aware blank finding, blanking, Levenshtein |
| `Core/ReviewSession.cs` | Queue building, card construction, answer recording |
| `Core/Stats.cs` | Overview tiles, heatmap, mastery breakdown, retention curve |
| `Core/Settings.cs` | Typed key/value settings over the `settings` table |
| `Core/Portability.cs` | JSON/CSV/TSV export and import, merge policy |
| `Core/Backups.cs` | Timestamped snapshots, retention, restore |
| `Core/Lookup.cs` | Datamuse + dictionaryapi.dev clients, offline tolerance |
| `Shell/Ui.cs` | Palette, typography, control factories, `Glyph`, `Motion` |
| `Shell/MainWindow.cs` | Frameless chrome, sidebar nav, status footer, view host |
| `Shell/CommandPalette.cs` | Ctrl+Space fuzzy palette |
| `Shell/Toast.cs` | Transient notifications |
| `Shell/Capture.cs` | Global hotkey registration and clipboard capture |
| `Views/*.cs` | One file per view, listed in the spec |
| `tools/Checks/Program.cs` | Headless Avalonia assertions |
| `tools/Preview/Program.cs` | Renders each view to PNG |
| `tests/*.cs` | xunit over Core |

---

### Task 1: Solution scaffold and test harness

**Files:**
- Create: `SirWordALot.csproj`, `NuGet.Config`, `app.manifest`, `Program.cs`, `Directory.Build.props`
- Create: `tests/SirWordALot.Tests.csproj`, `tests/SmokeTest.cs`
- Modify: `.gitignore` (already present from spec commit)

**Interfaces:**
- Consumes: nothing.
- Produces: an `SirWordALot` assembly with root namespace `SirWordALot`, and a `tests` project that compiles `Core/**/*.cs` by link so `Core` stays Avalonia-free.

- [ ] **Step 1: Write the failing test**

Create `tests/SmokeTest.cs`:

```csharp
using Xunit;

namespace SirWordALot.Tests;

public class SmokeTest
{
    [Fact]
    public void CoreAssemblyDoesNotReferenceAvalonia()
    {
        var referenced = typeof(SirWordALot.Core.Marker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? "");
        Assert.DoesNotContain(referenced, name => name.StartsWith("Avalonia"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SirWordALot.Tests.csproj`
Expected: FAIL — the projects do not exist yet.

- [ ] **Step 3: Create the projects**

`NuGet.Config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
```

`app.manifest` — copy `D:\Workhammer\hammeros\app.manifest` verbatim.

`SirWordALot.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>SirWordALot</RootNamespace>
    <Version>0.1.0</Version>
    <Company>Fezcode</Company>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <Compile Remove="tools/**;tests/**" />
    <AvaloniaResource Include="Assets/**" />
    <PackageReference Include="Avalonia" Version="11.3.0" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.0" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.0" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.3.0" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.0" />
  </ItemGroup>
</Project>
```

`tests/SirWordALot.Tests.csproj` — links `Core` sources rather than referencing the app project, which is what keeps `Core` provably Avalonia-free:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>SirWordALot.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../Core/**/*.cs" LinkBase="Core" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

Create `Core/Marker.cs`:

```csharp
namespace SirWordALot.Core;

/// <summary>Anchor type used by tests to locate the Core assembly.</summary>
public static class Marker { public const string Name = "SirWordALot.Core"; }
```

Create a placeholder `Program.cs` so the app project compiles:

```csharp
namespace SirWordALot;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--version") || args.Contains("-v")) { Console.WriteLine("Sir Word-a-lot 0.1.0"); return; }
        if (args.Contains("--help") || args.Contains("-h")) { Console.WriteLine(Help); return; }
    }

    private const string Help = """
        Sir Word-a-lot - a vocabulary and thesaurus trainer

          -v, --version   Print version and exit
          -h, --help      Show this help and exit
        """;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet build SirWordALot.csproj; dotnet test tests/SirWordALot.Tests.csproj`
Expected: build succeeds, 1 test passes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Scaffold projects and test harness"
```

---

### Task 2: Database schema and migrations

**Files:**
- Create: `Core/Db.cs`
- Test: `tests/DbTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `Db.OpenMemory() -> Db`
  - `Db.Open(string path) -> Db`
  - `Db.Connection -> SqliteConnection`
  - `Db.SchemaVersion -> const int` (currently `1`)
  - `Db.UserVersion -> int`
  - `Db.DefaultPath() -> string` (`%APPDATA%\fezcode\SirWordALot\library.db`)
  - `Db.Execute(string sql, params (string, object?)[] args) -> int`
  - `Db.Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args) -> List<T>`
  - `Db.Scalar(string sql, params (string, object?)[] args) -> object?`

- [ ] **Step 1: Write the failing test**

Create `tests/DbTests.cs`:

```csharp
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
            db.Execute("INSERT INTO relations (entry_id, kind, word) VALUES (9999, 'synonym', 'x')"));
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter DbTests`
Expected: FAIL — `Db` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Core/Db.cs`:

```csharp
using Microsoft.Data.Sqlite;

namespace SirWordALot.Core;

/// <summary>Owns the SQLite connection, the schema, and forward migrations.</summary>
public sealed class Db : IDisposable
{
    public const int SchemaVersion = 1;

    public SqliteConnection Connection { get; }

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
        var db = new Db(new SqliteConnection($"Data Source={path}"));
        db.Execute("PRAGMA journal_mode = WAL");
        return db;
    }

    public static string DefaultPath() => Path.Combine(DataRoot(), "library.db");

    public static string DataRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "fezcode", "SirWordALot");

    public int UserVersion => System.Convert.ToInt32(Scalar("PRAGMA user_version"));

    private SqliteTransaction? _transaction;

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
        if (UserVersion >= SchemaVersion) return;
        Begin();
        if (UserVersion < 1) ApplyV1();
        Execute($"PRAGMA user_version = {SchemaVersion}");
        Commit();
    }

    private void ApplyV1()
    {
        foreach (var statement in SchemaV1.Split(";--", StringSplitOptions.RemoveEmptyEntries))
            if (!string.IsNullOrWhiteSpace(statement)) Execute(statement);
    }

    public void Dispose() => Connection.Dispose();

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
}
```

Note on `OpenMemory`: `Mode=Memory;Cache=Shared` keeps the database alive as long as the connection is open, and the unique name isolates parallel test classes from one another.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter DbTests`
Expected: 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Core/Db.cs tests/DbTests.cs
git commit -m "Add SQLite schema, migrations and query helpers"
```

---

### Task 3: Domain models

**Files:**
- Create: `Core/Models.cs`
- Test: `tests/ModelTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: every record and enum used by the rest of Core, plus the string
  conversions used when reading and writing SQLite columns.

- [ ] **Step 1: Write the failing test**

Create `tests/ModelTests.cs`:

```csharp
using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class ModelTests
{
    [Theory]
    [InlineData(RelationKind.Synonym, "synonym")]
    [InlineData(RelationKind.Antonym, "antonym")]
    [InlineData(RelationKind.Related, "related")]
    [InlineData(RelationKind.Rhyme, "rhyme")]
    [InlineData(RelationKind.Form, "form")]
    public void RelationKindRoundTripsThroughItsColumnValue(RelationKind kind, string column)
    {
        Assert.Equal(column, kind.ToColumn());
        Assert.Equal(kind, Enums.RelationKindFrom(column));
    }

    [Theory]
    [InlineData(QuizMode.Flip, "flip")]
    [InlineData(QuizMode.Choice, "choice")]
    [InlineData(QuizMode.Cloze, "cloze")]
    [InlineData(QuizMode.Typed, "typed")]
    public void QuizModeRoundTripsThroughItsColumnValue(QuizMode mode, string column)
    {
        Assert.Equal(column, mode.ToColumn());
        Assert.Equal(mode, Enums.QuizModeFrom(column));
    }

    [Theory]
    [InlineData(SchedulerKind.Sm2, "sm2")]
    [InlineData(SchedulerKind.Leitner, "leitner")]
    public void SchedulerKindRoundTripsThroughItsColumnValue(SchedulerKind kind, string column)
    {
        Assert.Equal(column, kind.ToColumn());
        Assert.Equal(kind, Enums.SchedulerKindFrom(column));
    }

    [Fact]
    public void UnknownColumnValueFallsBackRatherThanThrowing()
    {
        Assert.Equal(RelationKind.Related, Enums.RelationKindFrom("nonsense"));
        Assert.Equal(QuizMode.Flip, Enums.QuizModeFrom("nonsense"));
        Assert.Equal(SchedulerKind.Sm2, Enums.SchedulerKindFrom("nonsense"));
    }

    [Fact]
    public void GradeValuesMatchTheStoredIntegers()
    {
        Assert.Equal(0, (int)Grade.Again);
        Assert.Equal(1, (int)Grade.Hard);
        Assert.Equal(2, (int)Grade.Good);
        Assert.Equal(3, (int)Grade.Easy);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter ModelTests`
Expected: FAIL — `RelationKind` and friends do not exist.

- [ ] **Step 3: Write the implementation**

Create `Core/Models.cs`:

```csharp
namespace SirWordALot.Core;

public enum RelationKind { Synonym, Antonym, Related, Rhyme, Form }
public enum SchedulerKind { Sm2, Leitner }
public enum QuizMode { Flip, Choice, Cloze, Typed }
public enum Grade { Again = 0, Hard = 1, Good = 2, Easy = 3 }
public enum MasteryBucket { New, Learning, Familiar, Strong, Mastered }
public enum DueState { Any, DueNow, NotDue, Never }
public enum SortOrder { Recent, Alphabetic, Due, Mastery }

public sealed record Entry(
    long Id,
    string Headword,
    string Normalized,
    string? Pronunciation,
    string? AudioUrl,
    string? PartOfSpeech,
    string? Definition,
    string? Notes,
    bool Starred,
    bool Archived,
    string Source,
    DateTime CreatedUtc,
    DateTime ModifiedUtc);

public sealed record Relation(long Id, long EntryId, RelationKind Kind, string Word, string? Source);

public sealed record Sentence(
    long Id,
    long? EntryId,
    string Text,
    string? Source,
    int? ClozeStart,
    int? ClozeLength,
    DateTime CreatedUtc);

public sealed record Tag(long Id, string Name);

public sealed record Deck(long Id, string Name, string? Description, string? FilterJson, DateTime CreatedUtc);

public sealed record Scheduling(
    long EntryId,
    SchedulerKind Scheduler,
    int Box,
    double Ease,
    int IntervalDays,
    int Repetitions,
    int Lapses,
    DateTime DueUtc,
    DateTime? LastReviewedUtc);

public sealed record Review(
    long Id,
    long EntryId,
    QuizMode Mode,
    Grade Grade,
    bool Correct,
    bool Scheduled,
    int ElapsedMs,
    int IntervalBefore,
    int IntervalAfter,
    DateTime ReviewedUtc);

/// <summary>Column conversions. Unknown values fall back rather than throwing,
/// so a hand-edited or future database never crashes the app on read.</summary>
public static class Enums
{
    public static string ToColumn(this RelationKind kind) => kind switch
    {
        RelationKind.Synonym => "synonym",
        RelationKind.Antonym => "antonym",
        RelationKind.Rhyme => "rhyme",
        RelationKind.Form => "form",
        _ => "related",
    };

    public static RelationKind RelationKindFrom(string value) => value switch
    {
        "synonym" => RelationKind.Synonym,
        "antonym" => RelationKind.Antonym,
        "rhyme" => RelationKind.Rhyme,
        "form" => RelationKind.Form,
        _ => RelationKind.Related,
    };

    public static string ToColumn(this QuizMode mode) => mode switch
    {
        QuizMode.Choice => "choice",
        QuizMode.Cloze => "cloze",
        QuizMode.Typed => "typed",
        _ => "flip",
    };

    public static QuizMode QuizModeFrom(string value) => value switch
    {
        "choice" => QuizMode.Choice,
        "cloze" => QuizMode.Cloze,
        "typed" => QuizMode.Typed,
        _ => QuizMode.Flip,
    };

    public static string ToColumn(this SchedulerKind kind) =>
        kind == SchedulerKind.Leitner ? "leitner" : "sm2";

    public static SchedulerKind SchedulerKindFrom(string value) =>
        value == "leitner" ? SchedulerKind.Leitner : SchedulerKind.Sm2;
}

/// <summary>ISO 8601 round-tripping for the TEXT timestamp columns.</summary>
public static class Iso
{
    public static string Text(DateTime utc) => utc.ToUniversalTime().ToString("O");

    public static DateTime Parse(string text) =>
        DateTime.Parse(text, null, System.Globalization.DateTimeStyles.AdjustToUniversal |
                                    System.Globalization.DateTimeStyles.AssumeUniversal);

    public static DateTime? ParseOrNull(string? text) => text is null ? null : Parse(text);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter ModelTests`
Expected: 14 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Core/Models.cs tests/ModelTests.cs
git commit -m "Add domain models and column conversions"
```

---

### Task 4: Entry CRUD and normalization

**Files:**
- Create: `Core/Library.cs`
- Test: `tests/LibraryEntryTests.cs`

**Interfaces:**
- Consumes: `Db`, `Entry`, `Scheduling`, `Iso` from Tasks 2 and 3.
- Produces:
  - `Library(Db db)`
  - `Library.Changed -> event Action?`
  - `Library.Normalize(string) -> string` (static)
  - `Library.AddEntry(string headword, string source = "manual") -> long`
  - `Library.GetEntry(long id) -> Entry?`
  - `Library.FindEntry(string headword) -> Entry?`
  - `Library.AllEntries(bool includeArchived = false) -> IReadOnlyList<Entry>`
  - `Library.UpdateEntry(Entry entry) -> void`
  - `Library.DeleteEntry(long id) -> void`
  - `Library.SetStarred(long id, bool starred) -> void`
  - `Library.SetArchived(long id, bool archived) -> void`
  - `Library.Count -> int`

Creating an entry MUST also create its `scheduling` row, due immediately, so a
new word is quizzable the moment it exists.

- [ ] **Step 1: Write the failing test**

Create `tests/LibraryEntryTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter LibraryEntryTests`
Expected: FAIL — `Library` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Core/Library.cs`. This file grows across Tasks 4, 5, 6 and 9; keep the
regions in the order the tasks add them.

```csharp
using Microsoft.Data.Sqlite;

namespace SirWordALot.Core;

/// <summary>All reads and writes against the library. Raises <see cref="Changed"/>
/// after any mutation so views can re-render.</summary>
public sealed class Library : IDisposable
{
    private readonly Db _db;

    public Library(Db db) => _db = db;

    public Db Database => _db;

    /// <summary>Raised after any mutation. Views subscribe and re-render.</summary>
    public event Action? Changed;

    private void Touch() => Changed?.Invoke();

    public void Dispose() => _db.Dispose();

    // ---- entries -------------------------------------------------------

    public static string Normalize(string headword) =>
        string.Join(' ', headword.Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public int Count => System.Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM entries"));

    public long AddEntry(string headword, string source = "manual")
    {
        var normalized = Normalize(headword);
        if (normalized.Length == 0)
            throw new ArgumentException("A headword cannot be blank.", nameof(headword));

        if (FindEntry(normalized) is { } existing) return existing.Id;

        var now = Iso.Text(DateTime.UtcNow);
        _db.Execute(
            """
            INSERT INTO entries (headword, normalized, source, created_utc, modified_utc)
            VALUES ($headword, $normalized, $source, $now, $now)
            """,
            ("$headword", headword.Trim()), ("$normalized", normalized),
            ("$source", source), ("$now", now));

        var id = (long)_db.Scalar("SELECT last_insert_rowid()")!;
        _db.Execute(
            "INSERT INTO scheduling (entry_id, scheduler, due_utc) VALUES ($id, 'sm2', $now)",
            ("$id", id), ("$now", now));

        Touch();
        return id;
    }

    public Entry? GetEntry(long id) =>
        _db.Query($"{EntrySelect} WHERE id = $id", ReadEntry, ("$id", id)).FirstOrDefault();

    public Entry? FindEntry(string headword) =>
        _db.Query($"{EntrySelect} WHERE normalized = $normalized", ReadEntry,
            ("$normalized", Normalize(headword))).FirstOrDefault();

    public IReadOnlyList<Entry> AllEntries(bool includeArchived = false) =>
        _db.Query($"{EntrySelect}{(includeArchived ? "" : " WHERE archived = 0")} ORDER BY normalized",
            ReadEntry);

    public void UpdateEntry(Entry entry)
    {
        var normalized = Normalize(entry.Headword);
        if (normalized.Length == 0)
            throw new ArgumentException("A headword cannot be blank.", nameof(entry));

        _db.Execute(
            """
            UPDATE entries SET
              headword = $headword, normalized = $normalized, pronunciation = $pronunciation,
              audio_url = $audio, part_of_speech = $pos, definition = $definition,
              notes = $notes, starred = $starred, archived = $archived,
              modified_utc = $now
            WHERE id = $id
            """,
            ("$headword", entry.Headword.Trim()), ("$normalized", normalized),
            ("$pronunciation", entry.Pronunciation), ("$audio", entry.AudioUrl),
            ("$pos", entry.PartOfSpeech), ("$definition", entry.Definition),
            ("$notes", entry.Notes), ("$starred", entry.Starred ? 1 : 0),
            ("$archived", entry.Archived ? 1 : 0), ("$now", Iso.Text(DateTime.UtcNow)),
            ("$id", entry.Id));

        Touch();
    }

    public void DeleteEntry(long id)
    {
        _db.Execute("DELETE FROM entries WHERE id = $id", ("$id", id));
        Touch();
    }

    public void SetStarred(long id, bool starred) => SetFlag(id, "starred", starred);

    public void SetArchived(long id, bool archived) => SetFlag(id, "archived", archived);

    private void SetFlag(long id, string column, bool value)
    {
        _db.Execute($"UPDATE entries SET {column} = $value, modified_utc = $now WHERE id = $id",
            ("$value", value ? 1 : 0), ("$now", Iso.Text(DateTime.UtcNow)), ("$id", id));
        Touch();
    }

    private const string EntrySelect =
        """
        SELECT id, headword, normalized, pronunciation, audio_url, part_of_speech,
               definition, notes, starred, archived, source, created_utc, modified_utc
        FROM entries
        """;

    internal static Entry ReadEntry(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        r.IsDBNull(7) ? null : r.GetString(7),
        r.GetInt32(8) != 0, r.GetInt32(9) != 0, r.GetString(10),
        Iso.Parse(r.GetString(11)), Iso.Parse(r.GetString(12)));

    // ---- scheduling (fleshed out in Task 9) ----------------------------

    public Scheduling GetScheduling(long entryId) => _db.Query(
        """
        SELECT entry_id, scheduler, box, ease, interval_days, repetitions, lapses,
               due_utc, last_reviewed_utc
        FROM scheduling WHERE entry_id = $id
        """,
        r => new Scheduling(
            r.GetInt64(0), Enums.SchedulerKindFrom(r.GetString(1)), r.GetInt32(2),
            r.GetDouble(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6),
            Iso.Parse(r.GetString(7)), r.IsDBNull(8) ? null : Iso.Parse(r.GetString(8))),
        ("$id", entryId)).Single();
}
```

Note: `AddEntry` returning the existing id on a duplicate is deliberate — the
lookup and capture flows call it speculatively and expect to land on the
existing word rather than fail.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter LibraryEntryTests`
Expected: 14 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Core/Library.cs tests/LibraryEntryTests.cs
git commit -m "Add entry CRUD with normalization and duplicate folding"
```

---

### Task 5: Relations, sentences, tags and decks

**Files:**
- Modify: `Core/Library.cs` (append the regions below)
- Test: `tests/LibraryCollectionTests.cs`

**Interfaces:**
- Consumes: `Library` from Task 4.
- Produces:
  - `AddRelation(long entryId, RelationKind kind, string word, string? source = null) -> void`
  - `RemoveRelation(long entryId, RelationKind kind, string word) -> void`
  - `Relations(long entryId) -> IReadOnlyList<Relation>`
  - `AddSentence(string text, long? entryId = null, string? source = null) -> long`
  - `UpdateSentence(Sentence sentence) -> void`
  - `DeleteSentence(long id) -> void`
  - `SentencesFor(long entryId) -> IReadOnlyList<Sentence>`
  - `AllSentences(bool looseOnly = false) -> IReadOnlyList<Sentence>`
  - `LinkSentence(long sentenceId, long? entryId) -> void`
  - `AddTag(string name) -> long`
  - `TagEntry(long entryId, string tag) -> void`
  - `UntagEntry(long entryId, string tag) -> void`
  - `Tags() -> IReadOnlyList<Tag>`
  - `TagsFor(long entryId) -> IReadOnlyList<string>`
  - `CreateDeck(string name, string? description = null, string? filterJson = null) -> long`
  - `RenameDeck(long id, string name) -> void`
  - `DeleteDeck(long id) -> void`
  - `AddToDeck(long deckId, long entryId) -> void`
  - `RemoveFromDeck(long deckId, long entryId) -> void`
  - `Decks() -> IReadOnlyList<Deck>`
  - `DeckEntries(long deckId) -> IReadOnlyList<Entry>`

- [ ] **Step 1: Write the failing test**

Create `tests/LibraryCollectionTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter LibraryCollectionTests`
Expected: FAIL — `AddRelation` and friends do not exist.

- [ ] **Step 3: Write the implementation**

Append to `Core/Library.cs`, inside the class, after the entry region:

```csharp
    // ---- relations -----------------------------------------------------

    public void AddRelation(long entryId, RelationKind kind, string word, string? source = null)
    {
        var trimmed = word.Trim();
        if (trimmed.Length == 0) return;
        _db.Execute(
            """
            INSERT OR IGNORE INTO relations (entry_id, kind, word, source)
            VALUES ($entry, $kind, $word, $source)
            """,
            ("$entry", entryId), ("$kind", kind.ToColumn()), ("$word", trimmed), ("$source", source));
        Touch();
    }

    public void RemoveRelation(long entryId, RelationKind kind, string word)
    {
        _db.Execute("DELETE FROM relations WHERE entry_id = $entry AND kind = $kind AND word = $word",
            ("$entry", entryId), ("$kind", kind.ToColumn()), ("$word", word.Trim()));
        Touch();
    }

    public IReadOnlyList<Relation> Relations(long entryId) => _db.Query(
        "SELECT id, entry_id, kind, word, source FROM relations WHERE entry_id = $entry ORDER BY kind, word",
        r => new Relation(r.GetInt64(0), r.GetInt64(1), Enums.RelationKindFrom(r.GetString(2)),
            r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)),
        ("$entry", entryId));

    // ---- sentences -----------------------------------------------------

    public long AddSentence(string text, long? entryId = null, string? source = null)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("A sentence cannot be blank.", nameof(text));

        _db.Execute(
            """
            INSERT INTO sentences (entry_id, text, source, created_utc)
            VALUES ($entry, $text, $source, $now)
            """,
            ("$entry", entryId), ("$text", trimmed), ("$source", source),
            ("$now", Iso.Text(DateTime.UtcNow)));

        Touch();
        return (long)_db.Scalar("SELECT last_insert_rowid()")!;
    }

    public void UpdateSentence(Sentence sentence)
    {
        _db.Execute(
            """
            UPDATE sentences SET text = $text, source = $source,
              cloze_start = $start, cloze_length = $length, entry_id = $entry
            WHERE id = $id
            """,
            ("$text", sentence.Text.Trim()), ("$source", sentence.Source),
            ("$start", sentence.ClozeStart), ("$length", sentence.ClozeLength),
            ("$entry", sentence.EntryId), ("$id", sentence.Id));
        Touch();
    }

    public void DeleteSentence(long id)
    {
        _db.Execute("DELETE FROM sentences WHERE id = $id", ("$id", id));
        Touch();
    }

    public void LinkSentence(long sentenceId, long? entryId)
    {
        _db.Execute("UPDATE sentences SET entry_id = $entry WHERE id = $id",
            ("$entry", entryId), ("$id", sentenceId));
        Touch();
    }

    public IReadOnlyList<Sentence> SentencesFor(long entryId) =>
        _db.Query($"{SentenceSelect} WHERE entry_id = $entry ORDER BY id", ReadSentence, ("$entry", entryId));

    public IReadOnlyList<Sentence> AllSentences(bool looseOnly = false) =>
        _db.Query($"{SentenceSelect}{(looseOnly ? " WHERE entry_id IS NULL" : "")} ORDER BY id DESC", ReadSentence);

    private const string SentenceSelect =
        "SELECT id, entry_id, text, source, cloze_start, cloze_length, created_utc FROM sentences";

    internal static Sentence ReadSentence(Microsoft.Data.Sqlite.SqliteDataReader r) => new(
        r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetInt32(4),
        r.IsDBNull(5) ? null : r.GetInt32(5),
        Iso.Parse(r.GetString(6)));

    // ---- tags ----------------------------------------------------------

    public long AddTag(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
            throw new ArgumentException("A tag cannot be blank.", nameof(name));

        _db.Execute("INSERT OR IGNORE INTO tags (name) VALUES ($name)", ("$name", normalized));
        return (long)_db.Scalar("SELECT id FROM tags WHERE name = $name", ("$name", normalized))!;
    }

    public void TagEntry(long entryId, string tag)
    {
        var tagId = AddTag(tag);
        _db.Execute("INSERT OR IGNORE INTO entry_tags (entry_id, tag_id) VALUES ($entry, $tag)",
            ("$entry", entryId), ("$tag", tagId));
        Touch();
    }

    public void UntagEntry(long entryId, string tag)
    {
        _db.Execute(
            """
            DELETE FROM entry_tags
            WHERE entry_id = $entry
              AND tag_id = (SELECT id FROM tags WHERE name = $tag)
            """,
            ("$entry", entryId), ("$tag", tag.Trim().ToLowerInvariant()));
        Touch();
    }

    public IReadOnlyList<Tag> Tags() => _db.Query(
        "SELECT id, name FROM tags ORDER BY name",
        r => new Tag(r.GetInt64(0), r.GetString(1)));

    public IReadOnlyList<string> TagsFor(long entryId) => _db.Query(
        """
        SELECT t.name FROM tags t
        JOIN entry_tags et ON et.tag_id = t.id
        WHERE et.entry_id = $entry ORDER BY t.name
        """,
        r => r.GetString(0), ("$entry", entryId));

    // ---- decks ---------------------------------------------------------

    public long CreateDeck(string name, string? description = null, string? filterJson = null)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("A deck cannot be blank.", nameof(name));

        _db.Execute(
            """
            INSERT INTO decks (name, description, filter_json, created_utc)
            VALUES ($name, $description, $filter, $now)
            """,
            ("$name", trimmed), ("$description", description), ("$filter", filterJson),
            ("$now", Iso.Text(DateTime.UtcNow)));

        Touch();
        return (long)_db.Scalar("SELECT last_insert_rowid()")!;
    }

    public void RenameDeck(long id, string name)
    {
        _db.Execute("UPDATE decks SET name = $name WHERE id = $id", ("$name", name.Trim()), ("$id", id));
        Touch();
    }

    public void DeleteDeck(long id)
    {
        _db.Execute("DELETE FROM decks WHERE id = $id", ("$id", id));
        Touch();
    }

    public void AddToDeck(long deckId, long entryId)
    {
        _db.Execute(
            """
            INSERT OR IGNORE INTO deck_entries (deck_id, entry_id, position)
            VALUES ($deck, $entry, (SELECT COALESCE(MAX(position), 0) + 1 FROM deck_entries WHERE deck_id = $deck))
            """,
            ("$deck", deckId), ("$entry", entryId));
        Touch();
    }

    public void RemoveFromDeck(long deckId, long entryId)
    {
        _db.Execute("DELETE FROM deck_entries WHERE deck_id = $deck AND entry_id = $entry",
            ("$deck", deckId), ("$entry", entryId));
        Touch();
    }

    public IReadOnlyList<Deck> Decks() => _db.Query(
        "SELECT id, name, description, filter_json, created_utc FROM decks ORDER BY name",
        r => new Deck(r.GetInt64(0), r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            Iso.Parse(r.GetString(4))));

    public IReadOnlyList<Entry> DeckEntries(long deckId) => _db.Query(
        $"""
        {EntrySelect.Replace("FROM entries", "FROM entries e JOIN deck_entries de ON de.entry_id = e.id")
                    .Replace("SELECT id,", "SELECT e.id,")}
        WHERE de.deck_id = $deck ORDER BY de.position
        """,
        ReadEntry, ("$deck", deckId));
```

The `DeckEntries` string surgery is fragile; replace it with an explicit query
rather than patching `EntrySelect`:

```csharp
    public IReadOnlyList<Entry> DeckEntries(long deckId) => _db.Query(
        """
        SELECT e.id, e.headword, e.normalized, e.pronunciation, e.audio_url, e.part_of_speech,
               e.definition, e.notes, e.starred, e.archived, e.source, e.created_utc, e.modified_utc
        FROM entries e
        JOIN deck_entries de ON de.entry_id = e.id
        WHERE de.deck_id = $deck
        ORDER BY de.position
        """,
        ReadEntry, ("$deck", deckId));
```

Use the second form. It is the one the tests exercise.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter LibraryCollectionTests`
Expected: 12 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Core/Library.cs tests/LibraryCollectionTests.cs
git commit -m "Add relations, sentences, tags and decks"
```

---

### Task 6: Mastery and the scheduler

**Files:**
- Create: `Core/Mastery.cs`, `Core/Scheduler.cs`
- Test: `tests/SchedulerTests.cs`, `tests/MasteryTests.cs`

**Interfaces:**
- Consumes: `Scheduling`, `Grade`, `SchedulerKind`, `MasteryBucket` from Task 3.
- Produces:
  - `Scheduler.StartEase -> const double` (2.5)
  - `Scheduler.MinEase -> const double` (1.3)
  - `Scheduler.MaxIntervalDays -> const int` (365)
  - `Scheduler.LeitnerIntervals -> IReadOnlyList<int>` (`[1, 2, 4, 8, 16]`)
  - `Scheduler.Apply(Scheduling current, Grade grade, DateTime nowUtc, double fuzz = 1.0) -> Scheduling`
  - `Scheduler.PreviewIntervalDays(Scheduling current, Grade grade) -> int`
  - `Scheduler.Convert(Scheduling current, SchedulerKind target) -> Scheduling`
  - `Scheduler.RandomFuzz(Random random) -> double`
  - `Mastery.Percent(int intervalDays, int correct, int total) -> int`
  - `Mastery.Bucket(int totalReviews, int intervalDays) -> MasteryBucket`

Ease is updated **before** it is applied to the interval, so `Easy` compounds
its own bonus. `fuzz` is a multiplier applied only to intervals above 2 days;
tests pass `1.0` for determinism and production passes `RandomFuzz`.

- [ ] **Step 1: Write the failing test**

Create `tests/SchedulerTests.cs`:

```csharp
using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class SchedulerTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static Scheduling NewCard(SchedulerKind kind = SchedulerKind.Sm2) =>
        new(1, kind, 1, Scheduler.StartEase, 0, 0, 0, Now, null);

    [Fact]
    public void FirstGoodGivesOneDay()
    {
        var next = Scheduler.Apply(NewCard(), Grade.Good, Now);
        Assert.Equal(1, next.IntervalDays);
        Assert.Equal(1, next.Repetitions);
        Assert.Equal(Scheduler.StartEase, next.Ease);
        Assert.Equal(Now.Date.AddDays(1), next.DueUtc);
    }

    [Fact]
    public void SecondGoodGivesSixDays()
    {
        var card = NewCard() with { Repetitions = 1, IntervalDays = 1 };
        Assert.Equal(6, Scheduler.Apply(card, Grade.Good, Now).IntervalDays);
    }

    [Fact]
    public void ThirdGoodMultipliesByEase()
    {
        var card = NewCard() with { Repetitions = 2, IntervalDays = 6 };
        Assert.Equal(15, Scheduler.Apply(card, Grade.Good, Now).IntervalDays);
    }

    [Fact]
    public void FirstEasyGivesFourDaysAndRaisesEase()
    {
        var next = Scheduler.Apply(NewCard(), Grade.Easy, Now);
        Assert.Equal(4, next.IntervalDays);
        Assert.Equal(2.65, next.Ease, 3);
    }

    [Fact]
    public void EasyCompoundsTheUpdatedEase()
    {
        var card = NewCard() with { Repetitions = 2, IntervalDays = 6 };
        var next = Scheduler.Apply(card, Grade.Easy, Now);
        Assert.Equal(2.65, next.Ease, 3);
        Assert.Equal(21, next.IntervalDays);
    }

    [Fact]
    public void HardStretchesTheIntervalAndLowersEase()
    {
        var card = NewCard() with { Repetitions = 2, IntervalDays = 10 };
        var next = Scheduler.Apply(card, Grade.Hard, Now);
        Assert.Equal(12, next.IntervalDays);
        Assert.Equal(2.35, next.Ease, 3);
        Assert.Equal(0, next.Lapses);
    }

    [Fact]
    public void HardOnANewCardStillYieldsAtLeastOneDay() =>
        Assert.Equal(1, Scheduler.Apply(NewCard(), Grade.Hard, Now).IntervalDays);

    [Fact]
    public void AgainResetsRepetitionsRecordsALapseAndIsDueNow()
    {
        var card = NewCard() with { Repetitions = 4, IntervalDays = 30, Lapses = 1 };
        var next = Scheduler.Apply(card, Grade.Again, Now);

        Assert.Equal(0, next.IntervalDays);
        Assert.Equal(0, next.Repetitions);
        Assert.Equal(2, next.Lapses);
        Assert.Equal(2.3, next.Ease, 3);
        Assert.Equal(Now, next.DueUtc);
    }

    [Fact]
    public void EaseNeverFallsBelowTheFloor()
    {
        var card = NewCard() with { Ease = 1.35, Repetitions = 3, IntervalDays = 10 };
        Assert.Equal(Scheduler.MinEase, Scheduler.Apply(card, Grade.Again, Now).Ease, 3);
    }

    [Fact]
    public void IntervalsAreCappedAtOneYear()
    {
        var card = NewCard() with { Repetitions = 9, IntervalDays = 300 };
        Assert.Equal(Scheduler.MaxIntervalDays, Scheduler.Apply(card, Grade.Good, Now).IntervalDays);
    }

    [Fact]
    public void FuzzOnlyAppliesAboveTwoDays()
    {
        var newCard = NewCard();
        Assert.Equal(1, Scheduler.Apply(newCard, Grade.Good, Now, fuzz: 1.05).IntervalDays);

        var mature = NewCard() with { Repetitions = 2, IntervalDays = 20 };
        Assert.Equal(53, Scheduler.Apply(mature, Grade.Good, Now, fuzz: 1.05).IntervalDays);
    }

    [Fact]
    public void RandomFuzzStaysWithinFivePercent()
    {
        var random = new Random(1);
        for (var i = 0; i < 500; i++)
        {
            var fuzz = Scheduler.RandomFuzz(random);
            Assert.InRange(fuzz, 0.95, 1.05);
        }
    }

    [Fact]
    public void PreviewMatchesApplyWithoutFuzz()
    {
        var card = NewCard() with { Repetitions = 2, IntervalDays = 6 };
        foreach (var grade in new[] { Grade.Again, Grade.Hard, Grade.Good, Grade.Easy })
            Assert.Equal(Scheduler.Apply(card, grade, Now).IntervalDays,
                         Scheduler.PreviewIntervalDays(card, grade));
    }

    [Fact]
    public void LeitnerPromotesOnGoodHoldsOnHardAndResetsOnAgain()
    {
        var card = NewCard(SchedulerKind.Leitner) with { Box = 3, IntervalDays = 4 };

        Assert.Equal(4, Scheduler.Apply(card, Grade.Good, Now).Box);
        Assert.Equal(8, Scheduler.Apply(card, Grade.Good, Now).IntervalDays);
        Assert.Equal(3, Scheduler.Apply(card, Grade.Hard, Now).Box);
        Assert.Equal(1, Scheduler.Apply(card, Grade.Again, Now).Box);
        Assert.Equal(1, Scheduler.Apply(card, Grade.Again, Now).IntervalDays);
    }

    [Fact]
    public void LeitnerStopsAtBoxFive()
    {
        var card = NewCard(SchedulerKind.Leitner) with { Box = 5, IntervalDays = 16 };
        Assert.Equal(5, Scheduler.Apply(card, Grade.Easy, Now).Box);
        Assert.Equal(16, Scheduler.Apply(card, Grade.Easy, Now).IntervalDays);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 3)]
    [InlineData(7, 4)]
    [InlineData(40, 5)]
    public void ConvertingToLeitnerSeedsTheBoxFromTheInterval(int interval, int expectedBox)
    {
        var card = NewCard() with { IntervalDays = interval };
        var converted = Scheduler.Convert(card, SchedulerKind.Leitner);

        Assert.Equal(SchedulerKind.Leitner, converted.Scheduler);
        Assert.Equal(expectedBox, converted.Box);
        Assert.Equal(card.DueUtc, converted.DueUtc);
    }

    [Fact]
    public void ConvertingToSm2KeepsProgressAndDueDate()
    {
        var card = NewCard(SchedulerKind.Leitner) with { Box = 4, IntervalDays = 8, Repetitions = 1 };
        var converted = Scheduler.Convert(card, SchedulerKind.Sm2);

        Assert.Equal(SchedulerKind.Sm2, converted.Scheduler);
        Assert.Equal(8, converted.IntervalDays);
        Assert.Equal(4, converted.Repetitions);
        Assert.Equal(card.DueUtc, converted.DueUtc);
    }

    [Fact]
    public void ConvertingToTheSameSchedulerIsANoop()
    {
        var card = NewCard();
        Assert.Same(card, Scheduler.Convert(card, SchedulerKind.Sm2));
    }
}
```

Create `tests/MasteryTests.cs`:

```csharp
using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class MasteryTests
{
    [Fact]
    public void AnUnreviewedWordHasNoMastery() => Assert.Equal(0, Mastery.Percent(0, 0, 0));

    [Fact]
    public void PerfectRecallAtSixtyDaysIsFullMastery() => Assert.Equal(100, Mastery.Percent(60, 10, 10));

    [Fact]
    public void MasteryIsCappedAtSixtyDays() => Assert.Equal(100, Mastery.Percent(400, 10, 10));

    [Fact]
    public void MasteryScalesWithAccuracy() => Assert.Equal(50, Mastery.Percent(60, 5, 10));

    [Fact]
    public void MasteryScalesWithInterval() => Assert.Equal(50, Mastery.Percent(30, 10, 10));

    [Theory]
    [InlineData(0, 0, MasteryBucket.New)]
    [InlineData(1, 3, MasteryBucket.Learning)]
    [InlineData(2, 7, MasteryBucket.Familiar)]
    [InlineData(3, 29, MasteryBucket.Familiar)]
    [InlineData(4, 30, MasteryBucket.Strong)]
    [InlineData(5, 89, MasteryBucket.Strong)]
    [InlineData(6, 90, MasteryBucket.Mastered)]
    public void BucketsFollowTheIntervalThresholds(int reviews, int interval, MasteryBucket expected) =>
        Assert.Equal(expected, Mastery.Bucket(reviews, interval));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter "SchedulerTests|MasteryTests"`
Expected: FAIL — `Scheduler` and `Mastery` do not exist.

- [ ] **Step 3: Write the implementation**

Create `Core/Scheduler.cs`:

```csharp
namespace SirWordALot.Core;

/// <summary>Pure scheduling arithmetic. No I/O, no clock of its own — the caller
/// supplies <c>nowUtc</c> so every transition is reproducible in a test.</summary>
public static class Scheduler
{
    public const double StartEase = 2.5;
    public const double MinEase = 1.3;
    public const int MaxIntervalDays = 365;

    public static readonly IReadOnlyList<int> LeitnerIntervals = [1, 2, 4, 8, 16];

    /// <summary>A multiplier in [0.95, 1.05], applied to intervals above two days
    /// so that cards reviewed together do not stay clumped together forever.</summary>
    public static double RandomFuzz(Random random) => 0.95 + random.NextDouble() * 0.10;

    /// <summary>Away-from-zero rounding. The .NET default rounds halves to even,
    /// which would silently shorten every interval that lands on a .5 boundary.</summary>
    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    public static Scheduling Apply(Scheduling current, Grade grade, DateTime nowUtc, double fuzz = 1.0) =>
        current.Scheduler == SchedulerKind.Leitner
            ? ApplyLeitner(current, grade, nowUtc)
            : ApplySm2(current, grade, nowUtc, fuzz);

    public static int PreviewIntervalDays(Scheduling current, Grade grade) =>
        Apply(current, grade, current.DueUtc).IntervalDays;

    private static Scheduling ApplySm2(Scheduling c, Grade grade, DateTime nowUtc, double fuzz)
    {
        var ease = Math.Max(MinEase, c.Ease + grade switch
        {
            Grade.Again => -0.20,
            Grade.Hard => -0.15,
            Grade.Good => 0.0,
            _ => 0.15,
        });

        var interval = grade switch
        {
            Grade.Again => 0,
            Grade.Hard => Math.Max(1, Round(c.IntervalDays * 1.2)),
            Grade.Good => c.Repetitions switch { 0 => 1, 1 => 6, _ => Round(c.IntervalDays * ease) },
            _ => c.Repetitions switch { 0 => 4, _ => Round(c.IntervalDays * ease * 1.3) },
        };

        if (interval > 2) interval = Round(interval * fuzz);
        interval = Math.Clamp(interval, 0, MaxIntervalDays);

        return c with
        {
            Ease = ease,
            Repetitions = grade == Grade.Again ? 0 : c.Repetitions + 1,
            Lapses = grade == Grade.Again ? c.Lapses + 1 : c.Lapses,
            IntervalDays = interval,
            DueUtc = interval == 0 ? nowUtc : nowUtc.Date.AddDays(interval),
            LastReviewedUtc = nowUtc,
        };
    }

    private static Scheduling ApplyLeitner(Scheduling c, Grade grade, DateTime nowUtc)
    {
        var box = Math.Clamp(c.Box, 1, LeitnerIntervals.Count);
        box = grade switch
        {
            Grade.Again => 1,
            Grade.Hard => box,
            _ => Math.Min(LeitnerIntervals.Count, box + 1),
        };

        var interval = LeitnerIntervals[box - 1];

        return c with
        {
            Box = box,
            IntervalDays = interval,
            Repetitions = grade == Grade.Again ? 0 : c.Repetitions + 1,
            Lapses = grade == Grade.Again ? c.Lapses + 1 : c.Lapses,
            DueUtc = nowUtc.Date.AddDays(interval),
            LastReviewedUtc = nowUtc,
        };
    }

    /// <summary>Switches scheduler without losing progress: the due date is kept and
    /// the target scheduler's fields are seeded from the current interval.</summary>
    public static Scheduling Convert(Scheduling current, SchedulerKind target)
    {
        if (current.Scheduler == target) return current;

        if (target == SchedulerKind.Leitner)
        {
            var box = current.IntervalDays switch
            {
                <= 1 => 1,
                <= 2 => 2,
                <= 4 => 3,
                <= 8 => 4,
                _ => 5,
            };
            return current with { Scheduler = target, Box = box, IntervalDays = LeitnerIntervals[box - 1] };
        }

        return current with
        {
            Scheduler = target,
            Ease = current.Ease < MinEase ? StartEase : current.Ease,
            Repetitions = Math.Max(current.Repetitions, current.Box),
        };
    }
}
```

Create `Core/Mastery.cs`:

```csharp
namespace SirWordALot.Core;

/// <summary>Mastery is derived, never stored: how long the word survives between
/// reviews, discounted by how often it has actually been recalled.</summary>
public static class Mastery
{
    public const int FullIntervalDays = 60;

    public static int Percent(int intervalDays, int correct, int total)
    {
        if (total <= 0) return 0;
        var span = Math.Min(Math.Max(intervalDays, 0), FullIntervalDays) / (double)FullIntervalDays;
        var accuracy = Math.Clamp(correct / (double)total, 0, 1);
        return (int)Math.Round(100 * span * accuracy);
    }

    public static MasteryBucket Bucket(int totalReviews, int intervalDays) => totalReviews <= 0
        ? MasteryBucket.New
        : intervalDays switch
        {
            < 7 => MasteryBucket.Learning,
            < 30 => MasteryBucket.Familiar,
            < 90 => MasteryBucket.Strong,
            _ => MasteryBucket.Mastered,
        };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter "SchedulerTests|MasteryTests"`
Expected: 31 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Core/Scheduler.cs Core/Mastery.cs tests/SchedulerTests.cs tests/MasteryTests.cs
git commit -m "Add SM-2 and Leitner schedulers with derived mastery"
```

---

### Task 7: Cloze blanking and edit distance

**Files:**
- Create: `Core/Cloze.cs`
- Test: `tests/ClozeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `Cloze.Find(string sentence, string headword) -> (int Start, int Length)?`
  - `Cloze.Blank(string sentence, int start, int length) -> string`
  - `Cloze.BlankFor(string sentence, string headword) -> string?`
  - `Cloze.Distance(string a, string b) -> int`
  - `Cloze.Forms(string headword) -> IEnumerable<string>`
  - `Cloze.ContainsWord(string sentence, string headword) -> bool`

`Find` matches on whole words only, so `run` never matches inside `running`;
the inflected forms are generated and matched explicitly instead.

- [ ] **Step 1: Write the failing test**

Create `tests/ClozeTests.cs`:

```csharp
using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class ClozeTests
{
    [Fact]
    public void FindLocatesTheExactWord()
    {
        var span = Cloze.Find("She rose to eminence quickly.", "eminence");
        Assert.Equal((12, 8), span);
    }

    [Fact]
    public void FindIsCaseInsensitiveButReportsTheSentencesOwnSpan()
    {
        var span = Cloze.Find("Eminence is earned.", "eminence");
        Assert.Equal((0, 8), span);
    }

    [Theory]
    [InlineData("He obfuscates the issue.", "obfuscate", "obfuscates")]
    [InlineData("She obfuscated the issue.", "obfuscate", "obfuscated")]
    [InlineData("They are obfuscating it.", "obfuscate", "obfuscating")]
    [InlineData("The watches stopped.", "watch", "watches")]
    [InlineData("He tarried there.", "tarry", "tarried")]
    [InlineData("It parries every blow.", "parry", "parries")]
    [InlineData("He stopped abruptly.", "stop", "stopped")]
    [InlineData("She is stopping now.", "stop", "stopping")]
    public void FindMatchesInflectedForms(string sentence, string headword, string expected)
    {
        var span = Cloze.Find(sentence, headword);
        Assert.NotNull(span);
        Assert.Equal(expected, sentence.Substring(span!.Value.Start, span.Value.Length));
    }

    [Fact]
    public void FindDoesNotMatchInsideALongerWord() =>
        Assert.Null(Cloze.Find("The runner was fast.", "run"));

    [Fact]
    public void FindReturnsNullWhenTheWordIsAbsent() =>
        Assert.Null(Cloze.Find("Nothing to see here.", "eminence"));

    [Fact]
    public void FindHandlesPunctuationBoundaries()
    {
        var span = Cloze.Find("Eminence, earned slowly.", "eminence");
        Assert.Equal((0, 8), span);
    }

    [Fact]
    public void BlankReplacesTheSpanWithMatchingLowBars() =>
        Assert.Equal("She rose to ▁▁▁▁▁▁▁▁ quickly.",
            Cloze.Blank("She rose to eminence quickly.", 12, 8));

    [Fact]
    public void BlankForBlanksAnInflectedForm() =>
        Assert.Equal("He ▁▁▁▁▁▁▁▁▁▁ the issue.", Cloze.BlankFor("He obfuscates the issue.", "obfuscate"));

    [Fact]
    public void BlankForReturnsNullWhenTheWordIsAbsent() =>
        Assert.Null(Cloze.BlankFor("Nothing here.", "eminence"));

    [Fact]
    public void BlankClampsAnOutOfRangeSpanRatherThanThrowing() =>
        Assert.Equal("Short", Cloze.Blank("Short", 40, 8));

    [Theory]
    [InlineData("eminence", "eminence", 0)]
    [InlineData("eminence", "eminense", 1)]
    [InlineData("eminence", "eminance", 1)]
    [InlineData("eminence", "eminnce", 1)]
    [InlineData("eminence", "emminence", 1)]
    [InlineData("eminence", "eminanse", 2)]
    [InlineData("eminence", "laconic", 8)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    public void DistanceIsStandardLevenshtein(string a, string b, int expected) =>
        Assert.Equal(expected, Cloze.Distance(a, b));

    [Fact]
    public void DistanceIsCaseInsensitive() => Assert.Equal(0, Cloze.Distance("Eminence", "eminence"));

    [Fact]
    public void ContainsWordIsInflectionAware()
    {
        Assert.True(Cloze.ContainsWord("He obfuscates it.", "obfuscate"));
        Assert.False(Cloze.ContainsWord("The runner was fast.", "run"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter ClozeTests`
Expected: FAIL — `Cloze` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Core/Cloze.cs`:

```csharp
using System.Text;

namespace SirWordALot.Core;

/// <summary>Finds a headword inside a sentence even when the sentence inflects it,
/// blanks it for cloze questions, and scores typed answers.</summary>
public static class Cloze
{
    public const char BlankChar = '▁';

    public static (int Start, int Length)? Find(string sentence, string headword)
    {
        foreach (var form in Forms(headword))
        {
            var index = IndexOfWord(sentence, form);
            if (index >= 0) return (index, form.Length);
        }
        return null;
    }

    public static bool ContainsWord(string sentence, string headword) => Find(sentence, headword) is not null;

    public static string Blank(string sentence, int start, int length)
    {
        if (start < 0 || length <= 0 || start >= sentence.Length) return sentence;
        length = Math.Min(length, sentence.Length - start);
        return string.Concat(sentence.AsSpan(0, start),
                             new string(BlankChar, length),
                             sentence.AsSpan(start + length));
    }

    public static string? BlankFor(string sentence, string headword) =>
        Find(sentence, headword) is { } span ? Blank(sentence, span.Start, span.Length) : null;

    /// <summary>The headword plus the inflections an English sentence is likely to use.
    /// Longest forms are yielded first so a whole-word match lands on the real token.</summary>
    public static IEnumerable<string> Forms(string headword)
    {
        var word = headword.Trim();
        if (word.Length == 0) yield break;

        var forms = new List<string> { word };

        if (word.EndsWith('e'))
        {
            var stem = word[..^1];
            forms.Add(stem + "ing");
            forms.Add(word + "d");
            forms.Add(word + "s");
        }
        else if (word.EndsWith('y') && word.Length > 1 && !IsVowel(word[^2]))
        {
            var stem = word[..^1];
            forms.Add(stem + "ies");
            forms.Add(stem + "ied");
            forms.Add(word + "ing");
        }
        else
        {
            forms.Add(word + "s");
            forms.Add(word + "es");
            forms.Add(word + "ed");
            forms.Add(word + "ing");

            if (word.Length >= 3 && !IsVowel(word[^1]) && IsVowel(word[^2]) && !IsVowel(word[^3]))
            {
                forms.Add(word + word[^1] + "ing");
                forms.Add(word + word[^1] + "ed");
            }
        }

        foreach (var form in forms.Distinct().OrderByDescending(f => f.Length))
            yield return form;
    }

    private static bool IsVowel(char c) => "aeiou".Contains(char.ToLowerInvariant(c));

    private static int IndexOfWord(string haystack, string needle)
    {
        var from = 0;
        while (from <= haystack.Length - needle.Length)
        {
            var index = haystack.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return -1;

            var beforeOk = index == 0 || !IsWordChar(haystack[index - 1]);
            var afterIndex = index + needle.Length;
            var afterOk = afterIndex >= haystack.Length || !IsWordChar(haystack[afterIndex]);

            if (beforeOk && afterOk) return index;
            from = index + 1;
        }
        return -1;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '\'' or '-';

    /// <summary>Case-insensitive Levenshtein distance, used to grade typed answers.</summary>
    public static int Distance(string a, string b)
    {
        a = a.Trim().ToLowerInvariant();
        b = b.Trim().ToLowerInvariant();

        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
```

Note on `Forms` ordering: forms are yielded longest first so that for the
headword `stop` and the sentence `She is stopping now.`, `stopping` is tried
before `stops`. Whole-word matching makes the ordering defensive rather than
load-bearing, but it keeps the matched span correct when two forms could both
appear.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter ClozeTests`
Expected: 28 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Core/Cloze.cs tests/ClozeTests.cs
git commit -m "Add inflection-aware cloze blanking and edit distance"
```

---

### Task 8: Search and filtering

**Files:**
- Create: `Core/Search.cs`
- Test: `tests/SearchTests.cs`

**Interfaces:**
- Consumes: `Db`, `Library`, `Entry`, `Mastery`, `MasteryBucket`, `DueState`, `SortOrder`.
- Produces:
  - `SearchFilter` record with `Text`, `PartOfSpeech`, `Tag`, `Mastery`, `Due`, `AddedAfter`, `IncludeArchived`, `StarredOnly`, `DeckId`, `Sort`
  - `SearchHit(Entry Entry, int MasteryPercent, MasteryBucket Bucket, DateTime DueUtc, string? MatchedSentence)`
  - `Search(Db db)` with `Run(SearchFilter filter, DateTime nowUtc) -> IReadOnlyList<SearchHit>`
  - `Search.ToFts(string text) -> string` (static, exposed for testing the escaping)

- [ ] **Step 1: Write the failing test**

Create `tests/SearchTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter SearchTests`
Expected: FAIL — `Search`, `SearchFilter`, `Library.SaveScheduling` and
`Library.AddReview` do not exist. `SaveScheduling` and `AddReview` arrive in
Task 9; implement them here as part of Step 3 so this task's tests can run.

- [ ] **Step 3: Write the implementation**

First append the two scheduling writers to `Core/Library.cs`, next to
`GetScheduling`:

```csharp
    public void SaveScheduling(Scheduling s)
    {
        _db.Execute(
            """
            UPDATE scheduling SET scheduler = $scheduler, box = $box, ease = $ease,
              interval_days = $interval, repetitions = $reps, lapses = $lapses,
              due_utc = $due, last_reviewed_utc = $last
            WHERE entry_id = $id
            """,
            ("$scheduler", s.Scheduler.ToColumn()), ("$box", s.Box), ("$ease", s.Ease),
            ("$interval", s.IntervalDays), ("$reps", s.Repetitions), ("$lapses", s.Lapses),
            ("$due", Iso.Text(s.DueUtc)),
            ("$last", s.LastReviewedUtc is { } last ? Iso.Text(last) : null),
            ("$id", s.EntryId));
        Touch();
    }

    public long AddReview(Review review)
    {
        _db.Execute(
            """
            INSERT INTO reviews (entry_id, mode, grade, correct, scheduled, elapsed_ms,
                                 interval_before, interval_after, reviewed_utc)
            VALUES ($entry, $mode, $grade, $correct, $scheduled, $elapsed, $before, $after, $at)
            """,
            ("$entry", review.EntryId), ("$mode", review.Mode.ToColumn()),
            ("$grade", (int)review.Grade), ("$correct", review.Correct ? 1 : 0),
            ("$scheduled", review.Scheduled ? 1 : 0), ("$elapsed", review.ElapsedMs),
            ("$before", review.IntervalBefore), ("$after", review.IntervalAfter),
            ("$at", Iso.Text(review.ReviewedUtc)));
        Touch();
        return (long)_db.Scalar("SELECT last_insert_rowid()")!;
    }
```

Then create `Core/Search.cs`:

```csharp
namespace SirWordALot.Core;

public sealed record SearchFilter(
    string? Text = null,
    string? PartOfSpeech = null,
    string? Tag = null,
    MasteryBucket? Mastery = null,
    DueState Due = DueState.Any,
    DateTime? AddedAfter = null,
    bool IncludeArchived = false,
    bool StarredOnly = false,
    long? DeckId = null,
    SortOrder Sort = SortOrder.Recent);

public sealed record SearchHit(
    Entry Entry,
    int MasteryPercent,
    MasteryBucket Bucket,
    DateTime DueUtc,
    string? MatchedSentence);

/// <summary>Structured filtering plus FTS5 text matching over headwords,
/// definitions, notes and sentences.</summary>
public sealed class Search(Db db)
{
    public IReadOnlyList<SearchHit> Run(SearchFilter filter, DateTime nowUtc)
    {
        var where = new List<string>();
        var args = new List<(string, object?)>();

        if (!filter.IncludeArchived) where.Add("e.archived = 0");
        if (filter.StarredOnly) where.Add("e.starred = 1");

        if (!string.IsNullOrWhiteSpace(filter.PartOfSpeech))
        {
            where.Add("e.part_of_speech = $pos");
            args.Add(("$pos", filter.PartOfSpeech));
        }

        if (!string.IsNullOrWhiteSpace(filter.Tag))
        {
            where.Add("""
                e.id IN (SELECT et.entry_id FROM entry_tags et
                         JOIN tags t ON t.id = et.tag_id WHERE t.name = $tag)
                """);
            args.Add(("$tag", filter.Tag!.Trim().ToLowerInvariant()));
        }

        if (filter.DeckId is { } deckId)
        {
            where.Add("e.id IN (SELECT entry_id FROM deck_entries WHERE deck_id = $deck)");
            args.Add(("$deck", deckId));
        }

        if (filter.AddedAfter is { } after)
        {
            where.Add("e.created_utc >= $after");
            args.Add(("$after", Iso.Text(after)));
        }

        switch (filter.Due)
        {
            case DueState.DueNow:
                where.Add("s.due_utc <= $now");
                args.Add(("$now", Iso.Text(nowUtc)));
                break;
            case DueState.NotDue:
                where.Add("s.due_utc > $now");
                args.Add(("$now", Iso.Text(nowUtc)));
                break;
            case DueState.Never:
                where.Add("s.last_reviewed_utc IS NULL");
                break;
        }

        var matched = new Dictionary<long, string>();
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var fts = ToFts(filter.Text!);
            if (fts.Length == 0) return [];

            var entryIds = db.Query(
                "SELECT rowid FROM entries_fts WHERE entries_fts MATCH $q",
                r => r.GetInt64(0), ("$q", fts)).ToHashSet();

            foreach (var (sentenceEntryId, text) in db.Query(
                """
                SELECT s.entry_id, s.text FROM sentences_fts f
                JOIN sentences s ON s.id = f.rowid
                WHERE sentences_fts MATCH $q AND s.entry_id IS NOT NULL
                """,
                r => (r.GetInt64(0), r.GetString(1)), ("$q", fts)))
            {
                entryIds.Add(sentenceEntryId);
                matched.TryAdd(sentenceEntryId, text);
            }

            if (entryIds.Count == 0) return [];
            where.Add($"e.id IN ({string.Join(',', entryIds)})");
        }

        var sql = $"""
            SELECT e.id, e.headword, e.normalized, e.pronunciation, e.audio_url, e.part_of_speech,
                   e.definition, e.notes, e.starred, e.archived, e.source, e.created_utc, e.modified_utc,
                   s.interval_days, s.due_utc,
                   (SELECT COUNT(*) FROM reviews r WHERE r.entry_id = e.id),
                   (SELECT COUNT(*) FROM reviews r WHERE r.entry_id = e.id AND r.correct = 1)
            FROM entries e
            JOIN scheduling s ON s.entry_id = e.id
            {(where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where))}
            """;

        var hits = db.Query(sql, r =>
        {
            var entry = Library.ReadEntry(r);
            var interval = r.GetInt32(13);
            var due = Iso.Parse(r.GetString(14));
            var total = r.GetInt32(15);
            var correct = r.GetInt32(16);
            return new SearchHit(
                entry,
                Core.Mastery.Percent(interval, correct, total),
                Core.Mastery.Bucket(total, interval),
                due,
                matched.GetValueOrDefault(entry.Id));
        }, args.ToArray());

        if (filter.Mastery is { } bucket)
            hits = hits.Where(h => h.Bucket == bucket).ToList();

        return filter.Sort switch
        {
            SortOrder.Alphabetic => hits.OrderBy(h => h.Entry.Normalized, StringComparer.Ordinal).ToList(),
            SortOrder.Due => hits.OrderBy(h => h.DueUtc).ThenBy(h => h.Entry.Normalized).ToList(),
            SortOrder.Mastery => hits.OrderByDescending(h => h.MasteryPercent)
                                     .ThenBy(h => h.Entry.Normalized).ToList(),
            _ => hits.OrderByDescending(h => h.Entry.CreatedUtc).ThenByDescending(h => h.Entry.Id).ToList(),
        };
    }

    /// <summary>Turns free text into a safe FTS5 prefix query. Every token is
    /// double-quoted so the user can type quotes, parentheses and operators
    /// without producing a syntax error.</summary>
    public static string ToFts(string text)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => new string(t.Where(char.IsLetterOrDigit).ToArray()))
            .Where(t => t.Length > 0)
            .Select(t => $"\"{t}\"*");

        return string.Join(' ', tokens);
    }
}
```

`Library.ReadEntry` is reused because the projection keeps the entry columns in
the same order and position as `EntrySelect`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter SearchTests`
Expected: 15 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Core/Search.cs Core/Library.cs tests/SearchTests.cs
git commit -m "Add full-text search with structured filters and sorting"
```

---

### Task 9: Review sessions

**Files:**
- Modify: `Core/Library.cs` (queue reads)
- Create: `Core/ReviewSession.cs`
- Test: `tests/ReviewSessionTests.cs`

**Interfaces:**
- Consumes: `Library`, `Scheduler`, `Cloze`, `Entry`, `Sentence`, `Grade`, `QuizMode`.
- Produces on `Library`:
  - `DueEntries(DateTime nowUtc, int limit = int.MaxValue) -> IReadOnlyList<Entry>` — reviewed before and due now
  - `NewEntries(int limit) -> IReadOnlyList<Entry>` — never reviewed
  - `StarredEntries() -> IReadOnlyList<Entry>`
  - `EntriesWithTag(string tag) -> IReadOnlyList<Entry>`
  - `ReviewsFor(long entryId) -> IReadOnlyList<Review>`
  - `ReviewsToday(DateTime nowUtc) -> int`
  - `NewReviewedToday(DateTime nowUtc) -> int`
- Produces in `Core/ReviewSession.cs`:
  - `ScopeKind` enum: `Due`, `Deck`, `Tag`, `Starred`, `All`, `FreePractice`, `Single`
  - `ReviewScope(ScopeKind Kind, long? DeckId = null, string? Tag = null, long? EntryId = null)`
  - `SessionOptions(int NewPerDay = 20, QuizMode? Mode = null, int? Seed = null)`
  - `Card(Entry Entry, QuizMode Mode, Sentence? Sentence, string Prompt, IReadOnlyList<string> Choices, int CorrectChoice)`
  - `SessionSummary(int Reviewed, int Correct, int Lapsed, TimeSpan Elapsed)`
  - `ReviewSession(Library library, ReviewScope scope, SessionOptions options, DateTime nowUtc)`
  - `.Current -> Card?`, `.Total -> int`, `.Position -> int`, `.Answered -> int`,
    `.IsFinished -> bool`, `.Summary -> SessionSummary`
  - `.PreviewIntervalDays(Grade grade) -> int`
  - `.Answer(Grade grade, int elapsedMs, DateTime nowUtc) -> void`

An answer counts as correct when the grade is anything but `Again`. Cards
graded `Again` return at the end of the queue.

- [ ] **Step 1: Write the failing test**

Create `tests/ReviewSessionTests.cs`:

```csharp
using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class ReviewSessionTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static Library Seed(int count, bool withSentences = false)
    {
        var library = new Library(Db.OpenMemory());
        var words = new[] { "eminence", "laconic", "ersatz", "obdurate", "pellucid", "quixotic" };
        for (var i = 0; i < count; i++)
        {
            var id = library.AddEntry(words[i % words.Length] + (i >= words.Length ? i.ToString() : ""));
            library.UpdateEntry(library.GetEntry(id)! with
            { PartOfSpeech = "adjective", Definition = $"Definition {i}." });
            if (withSentences)
                library.AddSentence($"The {library.GetEntry(id)!.Headword} thing happened.", id);
        }
        return library;
    }

    [Fact]
    public void ANewLibraryQueuesEveryNewWordUpToTheDailyCap()
    {
        var library = Seed(6);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(NewPerDay: 4, Mode: QuizMode.Flip, Seed: 1), Now);

        Assert.Equal(4, session.Total);
        Assert.Equal(1, session.Position);
        Assert.NotNull(session.Current);
    }

    [Fact]
    public void AnsweringAdvancesThroughTheQueue()
    {
        var library = Seed(3);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);

        session.Answer(Grade.Good, 900, Now);
        Assert.Equal(2, session.Position);
        Assert.Equal(1, session.Answered);

        session.Answer(Grade.Good, 900, Now);
        session.Answer(Grade.Good, 900, Now);

        Assert.True(session.IsFinished);
        Assert.Null(session.Current);
    }

    [Fact]
    public void EveryAnswerWritesAReviewRowImmediately()
    {
        var library = Seed(2);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);
        var first = session.Current!.Entry.Id;

        session.Answer(Grade.Good, 1500, Now);

        var review = Assert.Single(library.ReviewsFor(first));
        Assert.Equal(Grade.Good, review.Grade);
        Assert.True(review.Correct);
        Assert.True(review.Scheduled);
        Assert.Equal(1500, review.ElapsedMs);
        Assert.Equal(0, review.IntervalBefore);
        Assert.Equal(1, review.IntervalAfter);
    }

    [Fact]
    public void AnsweringUpdatesTheSchedulingRow()
    {
        var library = Seed(1);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);
        var id = session.Current!.Entry.Id;

        session.Answer(Grade.Good, 900, Now);

        var scheduling = library.GetScheduling(id);
        Assert.Equal(1, scheduling.IntervalDays);
        Assert.Equal(1, scheduling.Repetitions);
        Assert.Equal(Now, scheduling.LastReviewedUtc);
    }

    [Fact]
    public void AgainRequeuesTheCardAtTheEnd()
    {
        var library = Seed(2);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);
        var first = session.Current!.Entry.Id;

        session.Answer(Grade.Again, 900, Now);
        Assert.NotEqual(first, session.Current!.Entry.Id);

        session.Answer(Grade.Good, 900, Now);
        Assert.Equal(first, session.Current!.Entry.Id);

        session.Answer(Grade.Good, 900, Now);
        Assert.True(session.IsFinished);
    }

    [Fact]
    public void AgainCountsAsIncorrectButHardDoesNot()
    {
        var library = Seed(2);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.All),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);

        session.Answer(Grade.Hard, 900, Now);
        session.Answer(Grade.Again, 900, Now);
        session.Answer(Grade.Good, 900, Now);

        Assert.Equal(3, session.Summary.Reviewed);
        Assert.Equal(2, session.Summary.Correct);
        Assert.Equal(1, session.Summary.Lapsed);
    }

    [Fact]
    public void FreePracticeLogsReviewsButLeavesSchedulingAlone()
    {
        var library = Seed(1);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.FreePractice),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);
        var id = session.Current!.Entry.Id;
        var before = library.GetScheduling(id);

        session.Answer(Grade.Easy, 900, Now);

        Assert.Equal(before, library.GetScheduling(id));
        var review = Assert.Single(library.ReviewsFor(id));
        Assert.False(review.Scheduled);
    }

    [Fact]
    public void DeckScopeQueuesOnlyDeckMembers()
    {
        var library = Seed(4);
        var deck = library.CreateDeck("Exam");
        var chosen = library.AllEntries()[0].Id;
        library.AddToDeck(deck, chosen);

        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Deck, DeckId: deck),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);

        Assert.Equal(1, session.Total);
        Assert.Equal(chosen, session.Current!.Entry.Id);
    }

    [Fact]
    public void TagScopeQueuesOnlyTaggedEntries()
    {
        var library = Seed(4);
        var chosen = library.AllEntries()[1].Id;
        library.TagEntry(chosen, "exam");

        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Tag, Tag: "exam"),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);

        Assert.Equal(1, session.Total);
        Assert.Equal(chosen, session.Current!.Entry.Id);
    }

    [Fact]
    public void SingleScopeQueuesExactlyOneWord()
    {
        var library = Seed(4);
        var chosen = library.AllEntries()[3].Id;

        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Single, EntryId: chosen),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);

        Assert.Equal(1, session.Total);
        Assert.Equal(chosen, session.Current!.Entry.Id);

        session.Answer(Grade.Good, 900, Now);
        Assert.True(session.IsFinished);
        Assert.Single(library.ReviewsFor(chosen));
    }

    [Fact]
    public void SingleScopeWithAMissingEntryIsFinishedImmediately()
    {
        var library = Seed(1);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Single, EntryId: 9999),
            new SessionOptions(Seed: 1), Now);

        Assert.True(session.IsFinished);
    }

    [Fact]
    public void StarredScopeQueuesOnlyStarredEntries()
    {
        var library = Seed(4);
        var chosen = library.AllEntries()[2].Id;
        library.SetStarred(chosen, true);

        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Starred),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);

        Assert.Equal(1, session.Total);
    }

    [Fact]
    public void ChoiceCardsCarryFourDistinctOptionsIncludingTheAnswer()
    {
        var library = Seed(6);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.All),
            new SessionOptions(Mode: QuizMode.Choice, Seed: 7), Now);
        var card = session.Current!;

        Assert.Equal(4, card.Choices.Count);
        Assert.Equal(4, card.Choices.Distinct().Count());
        Assert.Equal(card.Entry.Headword, card.Choices[card.CorrectChoice]);
        Assert.Equal(card.Entry.Definition, card.Prompt);
    }

    [Fact]
    public void ChoiceFallsBackToFlipWhenThereAreTooFewEntries()
    {
        var library = Seed(3);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.All),
            new SessionOptions(Mode: QuizMode.Choice, Seed: 1), Now);

        Assert.Equal(QuizMode.Flip, session.Current!.Mode);
    }

    [Fact]
    public void ClozeCardsCarryABlankedSentence()
    {
        var library = Seed(2, withSentences: true);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.All),
            new SessionOptions(Mode: QuizMode.Cloze, Seed: 1), Now);
        var card = session.Current!;

        Assert.Equal(QuizMode.Cloze, card.Mode);
        Assert.NotNull(card.Sentence);
        Assert.Contains(Cloze.BlankChar, card.Prompt);
        Assert.DoesNotContain(card.Entry.Headword, card.Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClozeFallsBackToFlipWithoutAUsableSentence()
    {
        var library = Seed(2);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.All),
            new SessionOptions(Mode: QuizMode.Cloze, Seed: 1), Now);

        Assert.Equal(QuizMode.Flip, session.Current!.Mode);
    }

    [Fact]
    public void MixedModePrefersClozeThenChoiceThenFlip()
    {
        var withSentences = Seed(6, withSentences: true);
        Assert.Equal(QuizMode.Cloze,
            new ReviewSession(withSentences, new ReviewScope(ScopeKind.All),
                new SessionOptions(Seed: 1), Now).Current!.Mode);

        var withoutSentences = Seed(6);
        Assert.Equal(QuizMode.Choice,
            new ReviewSession(withoutSentences, new ReviewScope(ScopeKind.All),
                new SessionOptions(Seed: 1), Now).Current!.Mode);

        var tiny = Seed(2);
        Assert.Equal(QuizMode.Flip,
            new ReviewSession(tiny, new ReviewScope(ScopeKind.All),
                new SessionOptions(Seed: 1), Now).Current!.Mode);
    }

    [Fact]
    public void PreviewIntervalDaysMatchesTheScheduler()
    {
        var library = Seed(1);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);

        Assert.Equal(1, session.PreviewIntervalDays(Grade.Good));
        Assert.Equal(4, session.PreviewIntervalDays(Grade.Easy));
        Assert.Equal(0, session.PreviewIntervalDays(Grade.Again));
    }

    [Fact]
    public void AnEmptyScopeProducesAFinishedSession()
    {
        var library = new Library(Db.OpenMemory());
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(Seed: 1), Now);

        Assert.True(session.IsFinished);
        Assert.Equal(0, session.Total);
        Assert.Equal(0, session.Summary.Reviewed);
    }

    [Fact]
    public void AnsweringAFinishedSessionThrows()
    {
        var library = Seed(1);
        var session = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(Mode: QuizMode.Flip, Seed: 1), Now);
        session.Answer(Grade.Good, 900, Now);

        Assert.Throws<InvalidOperationException>(() => session.Answer(Grade.Good, 900, Now));
    }

    [Fact]
    public void TheDailyCapCountsNewCardsAlreadyStudiedToday()
    {
        var library = Seed(6);
        var first = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(NewPerDay: 3, Mode: QuizMode.Flip, Seed: 1), Now);
        first.Answer(Grade.Good, 900, Now);
        first.Answer(Grade.Good, 900, Now);

        var second = new ReviewSession(library, new ReviewScope(ScopeKind.Due),
            new SessionOptions(NewPerDay: 3, Mode: QuizMode.Flip, Seed: 1), Now);

        Assert.Equal(1, second.Total);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter ReviewSessionTests`
Expected: FAIL — `ReviewSession` does not exist.

- [ ] **Step 3: Write the implementation**

Append the queue reads to `Core/Library.cs`:

```csharp
    public IReadOnlyList<Entry> DueEntries(DateTime nowUtc, int limit = int.MaxValue) => _db.Query(
        """
        SELECT e.id, e.headword, e.normalized, e.pronunciation, e.audio_url, e.part_of_speech,
               e.definition, e.notes, e.starred, e.archived, e.source, e.created_utc, e.modified_utc
        FROM entries e JOIN scheduling s ON s.entry_id = e.id
        WHERE e.archived = 0 AND s.last_reviewed_utc IS NOT NULL AND s.due_utc <= $now
        ORDER BY s.due_utc LIMIT $limit
        """,
        ReadEntry, ("$now", Iso.Text(nowUtc)), ("$limit", limit));

    public IReadOnlyList<Entry> NewEntries(int limit) => _db.Query(
        """
        SELECT e.id, e.headword, e.normalized, e.pronunciation, e.audio_url, e.part_of_speech,
               e.definition, e.notes, e.starred, e.archived, e.source, e.created_utc, e.modified_utc
        FROM entries e JOIN scheduling s ON s.entry_id = e.id
        WHERE e.archived = 0 AND s.last_reviewed_utc IS NULL
        ORDER BY e.created_utc, e.id LIMIT $limit
        """,
        ReadEntry, ("$limit", Math.Max(0, limit)));

    public IReadOnlyList<Entry> StarredEntries() =>
        _db.Query($"{EntrySelect} WHERE archived = 0 AND starred = 1 ORDER BY normalized", ReadEntry);

    public IReadOnlyList<Entry> EntriesWithTag(string tag) => _db.Query(
        """
        SELECT e.id, e.headword, e.normalized, e.pronunciation, e.audio_url, e.part_of_speech,
               e.definition, e.notes, e.starred, e.archived, e.source, e.created_utc, e.modified_utc
        FROM entries e
        JOIN entry_tags et ON et.entry_id = e.id
        JOIN tags t ON t.id = et.tag_id
        WHERE e.archived = 0 AND t.name = $tag ORDER BY e.normalized
        """,
        ReadEntry, ("$tag", tag.Trim().ToLowerInvariant()));

    public IReadOnlyList<Review> ReviewsFor(long entryId) => _db.Query(
        """
        SELECT id, entry_id, mode, grade, correct, scheduled, elapsed_ms,
               interval_before, interval_after, reviewed_utc
        FROM reviews WHERE entry_id = $entry ORDER BY id
        """,
        r => new Review(r.GetInt64(0), r.GetInt64(1), Enums.QuizModeFrom(r.GetString(2)),
            (Grade)r.GetInt32(3), r.GetInt32(4) != 0, r.GetInt32(5) != 0, r.GetInt32(6),
            r.GetInt32(7), r.GetInt32(8), Iso.Parse(r.GetString(9))),
        ("$entry", entryId));

    public int ReviewsToday(DateTime nowUtc) => System.Convert.ToInt32(_db.Scalar(
        "SELECT COUNT(*) FROM reviews WHERE scheduled = 1 AND reviewed_utc >= $start AND reviewed_utc < $end",
        ("$start", Iso.Text(nowUtc.Date)), ("$end", Iso.Text(nowUtc.Date.AddDays(1)))));

    public int NewReviewedToday(DateTime nowUtc) => System.Convert.ToInt32(_db.Scalar(
        """
        SELECT COUNT(DISTINCT entry_id) FROM reviews
        WHERE scheduled = 1 AND interval_before = 0
          AND reviewed_utc >= $start AND reviewed_utc < $end
        """,
        ("$start", Iso.Text(nowUtc.Date)), ("$end", Iso.Text(nowUtc.Date.AddDays(1)))));
```

Create `Core/ReviewSession.cs`:

```csharp
namespace SirWordALot.Core;

public enum ScopeKind { Due, Deck, Tag, Starred, All, FreePractice, Single }

public sealed record ReviewScope(
    ScopeKind Kind, long? DeckId = null, string? Tag = null, long? EntryId = null);

public sealed record SessionOptions(int NewPerDay = 20, QuizMode? Mode = null, int? Seed = null);

public sealed record Card(
    Entry Entry,
    QuizMode Mode,
    Sentence? Sentence,
    string Prompt,
    IReadOnlyList<string> Choices,
    int CorrectChoice);

public sealed record SessionSummary(int Reviewed, int Correct, int Lapsed, TimeSpan Elapsed);

/// <summary>Builds the queue for one sitting and records each answer as it happens.
/// Nothing is buffered: ending early or crashing loses no progress.</summary>
public sealed class ReviewSession
{
    private readonly Library _library;
    private readonly ReviewScope _scope;
    private readonly SessionOptions _options;
    private readonly Random _random;
    private readonly List<Entry> _queue;
    private readonly List<Entry> _pool;
    private readonly DateTime _startedUtc;

    private int _index;
    private int _reviewed, _correct, _lapsed;
    private Card? _current;

    public ReviewSession(Library library, ReviewScope scope, SessionOptions options, DateTime nowUtc)
    {
        _library = library;
        _scope = scope;
        _options = options;
        _random = options.Seed is { } seed ? new Random(seed) : new Random();
        _startedUtc = nowUtc;

        _pool = library.AllEntries().ToList();
        _queue = BuildQueue(nowUtc);
        _current = _queue.Count > 0 ? BuildCard(_queue[0]) : null;
    }

    public int Total => _queue.Count;
    public int Position => _index + 1;
    public int Answered => _reviewed;
    public Card? Current => _current;
    public bool IsFinished => _current is null;

    public SessionSummary Summary => new(_reviewed, _correct, _lapsed, DateTime.UtcNow - _startedUtc);

    public int PreviewIntervalDays(Grade grade)
    {
        if (_current is null) return 0;
        return Scheduler.PreviewIntervalDays(_library.GetScheduling(_current.Entry.Id), grade);
    }

    public void Answer(Grade grade, int elapsedMs, DateTime nowUtc)
    {
        if (_current is null) throw new InvalidOperationException("The session is finished.");

        var entry = _current.Entry;
        var before = _library.GetScheduling(entry.Id);
        var scheduled = _scope.Kind != ScopeKind.FreePractice;
        var after = before;

        if (scheduled)
        {
            after = Scheduler.Apply(before, grade, nowUtc, Scheduler.RandomFuzz(_random));
            _library.SaveScheduling(after);
        }

        _library.AddReview(new Review(0, entry.Id, _current.Mode, grade,
            Correct: grade != Grade.Again, Scheduled: scheduled, elapsedMs,
            before.IntervalDays, after.IntervalDays, nowUtc));

        _reviewed++;
        if (grade == Grade.Again) { _lapsed++; _queue.Add(entry); } else _correct++;

        _index++;
        _current = _index < _queue.Count ? BuildCard(_queue[_index]) : null;
    }

    private List<Entry> BuildQueue(DateTime nowUtc)
    {
        switch (_scope.Kind)
        {
            case ScopeKind.Deck:
                return Shuffle(_library.DeckEntries(_scope.DeckId ?? 0).Where(e => !e.Archived).ToList());
            case ScopeKind.Tag:
                return Shuffle(_library.EntriesWithTag(_scope.Tag ?? "").ToList());
            case ScopeKind.Starred:
                return Shuffle(_library.StarredEntries().ToList());
            case ScopeKind.Single:
                return _scope.EntryId is { } single && _library.GetEntry(single) is { } entry
                    ? [entry] : [];
            case ScopeKind.All:
            case ScopeKind.FreePractice:
                return Shuffle(_library.AllEntries().ToList());
            default:
                var due = _library.DueEntries(nowUtc).ToList();
                var remaining = Math.Max(0, _options.NewPerDay - _library.NewReviewedToday(nowUtc));
                var fresh = _library.NewEntries(remaining).ToList();
                return [.. due, .. Shuffle(fresh)];
        }
    }

    private List<Entry> Shuffle(List<Entry> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        return items;
    }

    private Card BuildCard(Entry entry)
    {
        var sentence = _library.SentencesFor(entry.Id)
            .FirstOrDefault(s => Cloze.ContainsWord(s.Text, entry.Headword));

        var mode = ResolveMode(entry, sentence);

        return mode switch
        {
            QuizMode.Cloze => new Card(entry, mode, sentence, BlankedText(sentence!, entry), [], -1),
            QuizMode.Choice => BuildChoiceCard(entry, sentence),
            QuizMode.Typed => new Card(entry, mode, sentence,
                entry.Definition ?? "Type the word.", [], -1),
            _ => new Card(entry, QuizMode.Flip, sentence, entry.Headword, [], -1),
        };
    }

    private QuizMode ResolveMode(Entry entry, Sentence? sentence)
    {
        var clozeAvailable = sentence is not null;
        var choiceAvailable = _pool.Count(e => e.Id != entry.Id && !string.IsNullOrWhiteSpace(e.Headword)) >= 3
                              && !string.IsNullOrWhiteSpace(entry.Definition);

        if (_options.Mode is { } requested)
            return requested switch
            {
                QuizMode.Cloze when !clozeAvailable => QuizMode.Flip,
                QuizMode.Choice when !choiceAvailable => QuizMode.Flip,
                QuizMode.Typed when string.IsNullOrWhiteSpace(entry.Definition) => QuizMode.Flip,
                _ => requested,
            };

        if (clozeAvailable) return QuizMode.Cloze;
        if (choiceAvailable) return QuizMode.Choice;
        return QuizMode.Flip;
    }

    private static string BlankedText(Sentence sentence, Entry entry)
    {
        if (sentence.ClozeStart is { } start && sentence.ClozeLength is { } length)
            return Cloze.Blank(sentence.Text, start, length);
        return Cloze.BlankFor(sentence.Text, entry.Headword) ?? sentence.Text;
    }

    private Card BuildChoiceCard(Entry entry, Sentence? sentence)
    {
        var samePos = _pool.Where(e => e.Id != entry.Id &&
                                       e.PartOfSpeech == entry.PartOfSpeech).ToList();
        var others = _pool.Where(e => e.Id != entry.Id).ToList();

        var distractors = Shuffle(samePos).Select(e => e.Headword)
            .Concat(Shuffle(others).Select(e => e.Headword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(w => !string.Equals(w, entry.Headword, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        var choices = new List<string>(distractors) { entry.Headword };
        for (var i = choices.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (choices[i], choices[j]) = (choices[j], choices[i]);
        }

        return new Card(entry, QuizMode.Choice, sentence,
            entry.Definition ?? "Which word fits?", choices,
            choices.FindIndex(c => c == entry.Headword));
    }
}
```

Note: `Total` grows when a card is graded `Again`, because the card really is
coming back. The header reads `03 / 12` and the denominator ticking up is the
honest display.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter ReviewSessionTests`
Expected: 19 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Core/ReviewSession.cs Core/Library.cs tests/ReviewSessionTests.cs
git commit -m "Add review sessions with queue building and immediate persistence"
```

---

### Task 10: Statistics

**Files:**
- Create: `Core/Stats.cs`
- Test: `tests/StatsTests.cs`

**Interfaces:**
- Consumes: `Db`, `Library`, `Mastery`, `MasteryBucket`.
- Produces:
  - `Overview(int TotalWords, int DueToday, int Mastered, int StreakDays, double Retention, int ReviewsToday)`
  - `RetentionPoint(int UpperDays, double Rate, int Samples)`
  - `HeatmapDay(DateOnly Day, int Count)`
  - `Stats(Db db)` with `Summary(DateTime nowUtc) -> Overview`,
    `Heatmap(DateTime nowUtc, int weeks = 53) -> IReadOnlyList<HeatmapDay>`,
    `Breakdown() -> IReadOnlyDictionary<MasteryBucket, int>`,
    `RetentionCurve() -> IReadOnlyList<RetentionPoint>`

Retention counts scheduled reviews with `interval_before > 0` only — a word's
first exposure is not a memory test, and including it would flatter the number.
The streak counts consecutive days with at least one scheduled review, ending
today or, if nothing has been reviewed yet today, yesterday.

- [ ] **Step 1: Write the failing test**

Create `tests/StatsTests.cs`:

```csharp
using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class StatsTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    private static (Library Library, Stats Stats) Fixture()
    {
        var db = Db.OpenMemory();
        return (new Library(db), new Stats(db));
    }

    private static void Log(Library library, long entryId, DateTime at, bool correct,
                            int before = 5, int after = 10)
        => library.AddReview(new Review(0, entryId, QuizMode.Flip,
            correct ? Grade.Good : Grade.Again, correct, true, 900, before, after, at));

    [Fact]
    public void SummaryCountsWordsAndDueCards()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        library.AddEntry("laconic");
        library.SaveScheduling(library.GetScheduling(a) with
        { DueUtc = Now.AddDays(-1), LastReviewedUtc = Now.AddDays(-6), IntervalDays = 5 });

        var summary = stats.Summary(Now);
        Assert.Equal(2, summary.TotalWords);
        Assert.Equal(2, summary.DueToday);
    }

    [Fact]
    public void ArchivedWordsAreExcludedFromTheTotal()
    {
        var (library, stats) = Fixture();
        library.AddEntry("eminence");
        var shelved = library.AddEntry("laconic");
        library.SetArchived(shelved, true);

        Assert.Equal(1, stats.Summary(Now).TotalWords);
    }

    [Fact]
    public void MasteredCountsWordsPastNinetyDays()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        library.SaveScheduling(library.GetScheduling(a) with
        { IntervalDays = 120, Repetitions = 8, LastReviewedUtc = Now, DueUtc = Now.AddDays(120) });
        Log(library, a, Now, correct: true);

        Assert.Equal(1, stats.Summary(Now).Mastered);
    }

    [Fact]
    public void ReviewsTodayCountsOnlyTodayAndOnlyScheduled()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        Log(library, a, Now, correct: true);
        Log(library, a, Now.AddDays(-1), correct: true);
        library.AddReview(new Review(0, a, QuizMode.Flip, Grade.Good, true,
            Scheduled: false, 900, 5, 10, Now));

        Assert.Equal(1, stats.Summary(Now).ReviewsToday);
    }

    [Fact]
    public void RetentionIgnoresFirstExposures()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        Log(library, a, Now, correct: false, before: 0);
        Log(library, a, Now, correct: true, before: 5);
        Log(library, a, Now, correct: true, before: 5);

        Assert.Equal(1.0, stats.Summary(Now).Retention, 3);
    }

    [Fact]
    public void RetentionIsZeroWithNothingToMeasure() =>
        Assert.Equal(0, Fixture().Stats.Summary(Now).Retention);

    [Fact]
    public void StreakCountsConsecutiveDaysEndingToday()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        foreach (var offset in new[] { 0, -1, -2, -4 })
            Log(library, a, Now.AddDays(offset), correct: true);

        Assert.Equal(3, stats.Summary(Now).StreakDays);
    }

    [Fact]
    public void StreakSurvivesADayNotYetStudied()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        foreach (var offset in new[] { -1, -2 })
            Log(library, a, Now.AddDays(offset), correct: true);

        Assert.Equal(2, stats.Summary(Now).StreakDays);
    }

    [Fact]
    public void StreakIsZeroAfterAMissedDay()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        Log(library, a, Now.AddDays(-3), correct: true);

        Assert.Equal(0, stats.Summary(Now).StreakDays);
    }

    [Fact]
    public void HeatmapCoversEveryDayInTheWindowAndCountsReviews()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        Log(library, a, Now, correct: true);
        Log(library, a, Now, correct: true);
        Log(library, a, Now.AddDays(-10), correct: true);

        var heatmap = stats.Heatmap(Now, weeks: 2);

        Assert.Equal(14, heatmap.Count);
        Assert.Equal(DateOnly.FromDateTime(Now), heatmap[^1].Day);
        Assert.Equal(2, heatmap[^1].Count);
        Assert.Equal(1, heatmap.Single(d => d.Day == DateOnly.FromDateTime(Now.AddDays(-10))).Count);
        Assert.All(heatmap, d => Assert.True(d.Count >= 0));
    }

    [Fact]
    public void BreakdownBucketsEveryUnarchivedWord()
    {
        var (library, stats) = Fixture();
        var fresh = library.AddEntry("eminence");
        var strong = library.AddEntry("laconic");
        library.SaveScheduling(library.GetScheduling(strong) with
        { IntervalDays = 45, Repetitions = 5, LastReviewedUtc = Now, DueUtc = Now.AddDays(45) });
        Log(library, strong, Now, correct: true);

        var breakdown = stats.Breakdown();

        Assert.Equal(1, breakdown[MasteryBucket.New]);
        Assert.Equal(1, breakdown[MasteryBucket.Strong]);
        Assert.Equal(0, breakdown[MasteryBucket.Mastered]);
        Assert.Equal(5, breakdown.Count);
    }

    [Fact]
    public void RetentionCurveGroupsByIntervalBucket()
    {
        var (library, stats) = Fixture();
        var a = library.AddEntry("eminence");
        Log(library, a, Now, correct: true, before: 1);
        Log(library, a, Now, correct: false, before: 1);
        Log(library, a, Now, correct: true, before: 40);

        var curve = stats.RetentionCurve();

        Assert.Equal(0.5, curve.Single(p => p.UpperDays == 1).Rate, 3);
        Assert.Equal(2, curve.Single(p => p.UpperDays == 1).Samples);
        Assert.Equal(1.0, curve.Single(p => p.UpperDays == 90).Rate, 3);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter StatsTests`
Expected: FAIL — `Stats` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Core/Stats.cs`:

```csharp
namespace SirWordALot.Core;

public sealed record Overview(
    int TotalWords, int DueToday, int Mastered, int StreakDays, double Retention, int ReviewsToday);

public sealed record RetentionPoint(int UpperDays, double Rate, int Samples);

public sealed record HeatmapDay(DateOnly Day, int Count);

/// <summary>Aggregate queries over the append-only review log.</summary>
public sealed class Stats(Db db)
{
    private static readonly int[] IntervalBuckets = [1, 3, 7, 14, 30, 90, 365];

    public Overview Summary(DateTime nowUtc)
    {
        var total = Count("SELECT COUNT(*) FROM entries WHERE archived = 0");

        var due = Count(
            """
            SELECT COUNT(*) FROM entries e JOIN scheduling s ON s.entry_id = e.id
            WHERE e.archived = 0 AND s.due_utc <= $now
            """,
            ("$now", Iso.Text(nowUtc)));

        var mastered = Count(
            """
            SELECT COUNT(*) FROM entries e JOIN scheduling s ON s.entry_id = e.id
            WHERE e.archived = 0 AND s.interval_days >= 90 AND s.repetitions > 0
            """);

        var reviewsToday = Count(
            "SELECT COUNT(*) FROM reviews WHERE scheduled = 1 AND reviewed_utc >= $start AND reviewed_utc < $end",
            ("$start", Iso.Text(nowUtc.Date)), ("$end", Iso.Text(nowUtc.Date.AddDays(1))));

        return new Overview(total, due, mastered, Streak(nowUtc), Retention(), reviewsToday);
    }

    private double Retention()
    {
        var attempts = Count("SELECT COUNT(*) FROM reviews WHERE scheduled = 1 AND interval_before > 0");
        if (attempts == 0) return 0;
        var correct = Count(
            "SELECT COUNT(*) FROM reviews WHERE scheduled = 1 AND interval_before > 0 AND correct = 1");
        return correct / (double)attempts;
    }

    private int Streak(DateTime nowUtc)
    {
        var days = db.Query(
            """
            SELECT DISTINCT substr(reviewed_utc, 1, 10) FROM reviews
            WHERE scheduled = 1 ORDER BY 1 DESC
            """,
            r => DateOnly.Parse(r.GetString(0))).ToHashSet();

        if (days.Count == 0) return 0;

        var cursor = DateOnly.FromDateTime(nowUtc.Date);
        if (!days.Contains(cursor)) cursor = cursor.AddDays(-1);

        var streak = 0;
        while (days.Contains(cursor)) { streak++; cursor = cursor.AddDays(-1); }
        return streak;
    }

    public IReadOnlyList<HeatmapDay> Heatmap(DateTime nowUtc, int weeks = 53)
    {
        var last = DateOnly.FromDateTime(nowUtc.Date);
        var first = last.AddDays(-(weeks * 7 - 1));

        var counts = db.Query(
            """
            SELECT substr(reviewed_utc, 1, 10), COUNT(*) FROM reviews
            WHERE scheduled = 1 AND reviewed_utc >= $start
            GROUP BY 1
            """,
            r => (Day: DateOnly.Parse(r.GetString(0)), Count: r.GetInt32(1)),
            ("$start", Iso.Text(first.ToDateTime(TimeOnly.MinValue))))
            .ToDictionary(x => x.Day, x => x.Count);

        var days = new List<HeatmapDay>(weeks * 7);
        for (var day = first; day <= last; day = day.AddDays(1))
            days.Add(new HeatmapDay(day, counts.GetValueOrDefault(day)));
        return days;
    }

    public IReadOnlyDictionary<MasteryBucket, int> Breakdown()
    {
        var rows = db.Query(
            """
            SELECT s.interval_days, s.repetitions FROM entries e
            JOIN scheduling s ON s.entry_id = e.id
            WHERE e.archived = 0
            """,
            r => (Interval: r.GetInt32(0), Repetitions: r.GetInt32(1)));

        var breakdown = Enum.GetValues<MasteryBucket>().ToDictionary(b => b, _ => 0);
        foreach (var row in rows)
            breakdown[Mastery.Bucket(row.Repetitions, row.Interval)]++;
        return breakdown;
    }

    public IReadOnlyList<RetentionPoint> RetentionCurve()
    {
        var rows = db.Query(
            "SELECT interval_before, correct FROM reviews WHERE scheduled = 1 AND interval_before > 0",
            r => (Interval: r.GetInt32(0), Correct: r.GetInt32(1) != 0));

        return IntervalBuckets.Select(upper =>
        {
            var lower = Array.IndexOf(IntervalBuckets, upper) == 0
                ? 1
                : IntervalBuckets[Array.IndexOf(IntervalBuckets, upper) - 1] + 1;

            var inBucket = rows.Where(x => x.Interval >= lower && x.Interval <= upper).ToList();
            var rate = inBucket.Count == 0 ? 0 : inBucket.Count(x => x.Correct) / (double)inBucket.Count;
            return new RetentionPoint(upper, rate, inBucket.Count);
        }).ToList();
    }

    private int Count(string sql, params (string, object?)[] args) =>
        System.Convert.ToInt32(db.Scalar(sql, args));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter StatsTests`
Expected: 12 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Core/Stats.cs tests/StatsTests.cs
git commit -m "Add statistics over the review log"
```

---

### Task 11: Settings

**Files:**
- Create: `Core/Settings.cs`
- Test: `tests/SettingsTests.cs`

**Interfaces:**
- Consumes: `Db`, `SchedulerKind`.
- Produces `Settings(Db db)` with typed properties backed by the `settings`
  table, each persisting on set and raising `Changed`:
  `Palette` (string, default `"Petrol"`), `Motion` (bool, `true`),
  `Scheduler` (`SchedulerKind`, `Sm2`), `DailyGoal` (int, `30`),
  `NewPerDay` (int, `20`), `CaptureHotkey` (string, `"Ctrl+Alt+W"`),
  `CaptureEnabled` (bool, `true`), `LookupEnabled` (bool, `true`),
  `BackupFolder` (string, `<data root>\backups`), `WindowBounds` (string?, null),
  plus `Get(string key, string fallback)` and `Set(string key, string value)`.

- [ ] **Step 1: Write the failing test**

Create `tests/SettingsTests.cs`:

```csharp
using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class SettingsTests
{
    [Fact]
    public void DefaultsMatchTheSpec()
    {
        var settings = new Settings(Db.OpenMemory());

        Assert.Equal("Petrol", settings.Palette);
        Assert.True(settings.Motion);
        Assert.Equal(SchedulerKind.Sm2, settings.Scheduler);
        Assert.Equal(30, settings.DailyGoal);
        Assert.Equal(20, settings.NewPerDay);
        Assert.Equal("Ctrl+Alt+W", settings.CaptureHotkey);
        Assert.True(settings.CaptureEnabled);
        Assert.True(settings.LookupEnabled);
        Assert.Null(settings.WindowBounds);
        Assert.EndsWith("backups", settings.BackupFolder);
    }

    [Fact]
    public void ValuesPersistAcrossInstances()
    {
        var db = Db.OpenMemory();
        new Settings(db) { Palette = "Midnight", NewPerDay = 5, Scheduler = SchedulerKind.Leitner };

        var reopened = new Settings(db);
        Assert.Equal("Midnight", reopened.Palette);
        Assert.Equal(5, reopened.NewPerDay);
        Assert.Equal(SchedulerKind.Leitner, reopened.Scheduler);
    }

    [Fact]
    public void SettingAValueRaisesChanged()
    {
        var settings = new Settings(Db.OpenMemory());
        var raised = 0;
        settings.Changed += () => raised++;

        settings.Motion = false;
        settings.DailyGoal = 50;

        Assert.Equal(2, raised);
    }

    [Fact]
    public void CorruptStoredValuesFallBackToTheDefault()
    {
        var db = Db.OpenMemory();
        var settings = new Settings(db);
        settings.Set("new_per_day", "not a number");
        settings.Set("motion", "perhaps");

        var reopened = new Settings(db);
        Assert.Equal(20, reopened.NewPerDay);
        Assert.True(reopened.Motion);
    }

    [Fact]
    public void NewPerDayAndDailyGoalAreClampedToSaneRanges()
    {
        var settings = new Settings(Db.OpenMemory()) { NewPerDay = -5, DailyGoal = 100000 };

        Assert.Equal(0, settings.NewPerDay);
        Assert.Equal(9999, settings.DailyGoal);
    }

    [Fact]
    public void GetReturnsTheFallbackForAnUnknownKey() =>
        Assert.Equal("x", new Settings(Db.OpenMemory()).Get("nope", "x"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter SettingsTests`
Expected: FAIL — `Settings` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Core/Settings.cs`:

```csharp
namespace SirWordALot.Core;

/// <summary>Typed access to the key/value settings table. Every setter writes
/// through immediately, so preferences survive a crash.</summary>
public sealed class Settings(Db db)
{
    public event Action? Changed;

    public string Get(string key, string fallback) =>
        db.Scalar("SELECT value FROM settings WHERE key = $key", ("$key", key)) as string ?? fallback;

    public void Set(string key, string value)
    {
        db.Execute(
            """
            INSERT INTO settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """,
            ("$key", key), ("$value", value));
        Changed?.Invoke();
    }

    private bool GetBool(string key, bool fallback) =>
        bool.TryParse(Get(key, ""), out var value) ? value : fallback;

    private int GetInt(string key, int fallback, int min, int max) =>
        int.TryParse(Get(key, ""), out var value) ? Math.Clamp(value, min, max) : fallback;

    public string Palette
    {
        get => Get("palette", "Petrol");
        set => Set("palette", value);
    }

    public bool Motion
    {
        get => GetBool("motion", true);
        set => Set("motion", value.ToString());
    }

    public SchedulerKind Scheduler
    {
        get => Enums.SchedulerKindFrom(Get("scheduler", "sm2"));
        set => Set("scheduler", value.ToColumn());
    }

    public int DailyGoal
    {
        get => GetInt("daily_goal", 30, 0, 9999);
        set => Set("daily_goal", Math.Clamp(value, 0, 9999).ToString());
    }

    public int NewPerDay
    {
        get => GetInt("new_per_day", 20, 0, 9999);
        set => Set("new_per_day", Math.Clamp(value, 0, 9999).ToString());
    }

    public string CaptureHotkey
    {
        get => Get("capture_hotkey", "Ctrl+Alt+W");
        set => Set("capture_hotkey", value);
    }

    public bool CaptureEnabled
    {
        get => GetBool("capture_enabled", true);
        set => Set("capture_enabled", value.ToString());
    }

    public bool LookupEnabled
    {
        get => GetBool("lookup_enabled", true);
        set => Set("lookup_enabled", value.ToString());
    }

    public string BackupFolder
    {
        get => Get("backup_folder", Path.Combine(Db.DataRoot(), "backups"));
        set => Set("backup_folder", value);
    }

    public string? WindowBounds
    {
        get => Get("window_bounds", "") is { Length: > 0 } value ? value : null;
        set => Set("window_bounds", value ?? "");
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter SettingsTests`
Expected: 6 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Core/Settings.cs tests/SettingsTests.cs
git commit -m "Add typed settings backed by the database"
```

---

### Task 12: Import, export and backups

**Files:**
- Create: `Core/Portability.cs`, `Core/Backups.cs`
- Test: `tests/PortabilityTests.cs`, `tests/BackupTests.cs`

**Interfaces:**
- Consumes: `Library`, `Entry`, `Sentence`, `Relation`, `Scheduling`, `Review`.
- Produces:
  - `MergePolicy` enum: `KeepMine`, `TakeTheirs`, `KeepBoth`
  - `ImportReport(int Added, int Updated, int Skipped, IReadOnlyList<string> Conflicts)`
  - `Portability.FormatVersion -> const int` (1)
  - `Portability.ExportJson(Library library) -> string`
  - `Portability.ImportJson(Library library, string json, MergePolicy policy) -> ImportReport`
  - `Portability.ExportCsv(Library library) -> string`
  - `Portability.ImportCsv(Library library, string csv, MergePolicy policy) -> ImportReport`
  - `Portability.ExportAnkiTsv(Library library) -> string`
  - `Backups(string folder)` with `Write(Library library, DateTime nowUtc) -> string`,
    `List() -> IReadOnlyList<string>`, `Prune(int keep = 20) -> int`,
    `Restore(Library library, string path) -> ImportReport`
  - `Backups.WriteAtomic(string path, string content) -> void` (static)

`KeepBoth` suffixes the incoming headword with `" (imported)"`, then
`" (imported 2)"` and so on, because `normalized` is unique.

- [ ] **Step 1: Write the failing test**

Create `tests/PortabilityTests.cs`:

```csharp
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
        var sentenceId = library.AddSentence("She rose to eminence.", id, "Gibbon");
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
        Assert.Equal(Portability.FormatVersion, document.RootElement.GetProperty("version").GetInt32());
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
            Portability.ImportJson(target, """{"version":99,"entries":[]}""", MergePolicy.TakeTheirs));
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
                .RootElement.GetProperty("version").GetInt32());
        Assert.StartsWith("headword,", Portability.ExportCsv(empty));
        Assert.Equal("", Portability.ExportAnkiTsv(empty));
    }
}
```

Create `tests/BackupTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter "PortabilityTests|BackupTests"`
Expected: FAIL — `Portability` and `Backups` do not exist.

- [ ] **Step 3: Write the implementation**

Create `Core/Portability.cs`:

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SirWordALot.Core;

public enum MergePolicy { KeepMine, TakeTheirs, KeepBoth }

public sealed record ImportReport(int Added, int Updated, int Skipped, IReadOnlyList<string> Conflicts);

/// <summary>JSON is the lossless format and the backup format. CSV and Anki TSV
/// are deliberately lossy exchange formats.</summary>
public static class Portability
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] CsvHeader =
    [
        "headword", "pronunciation", "part_of_speech", "definition", "notes",
        "starred", "tags", "synonyms", "antonyms", "related", "sentences"
    ];

    // ---- JSON ----------------------------------------------------------

    public sealed record SentenceDto(string Text, string? Source, int[]? Cloze);

    public sealed record SchedulingDto(
        string Scheduler, int Box, double Ease, int IntervalDays,
        int Repetitions, int Lapses, string DueUtc, string? LastReviewedUtc);

    public sealed record ReviewDto(
        string Mode, int Grade, bool Correct, bool Scheduled,
        int ElapsedMs, int IntervalBefore, int IntervalAfter, string ReviewedUtc);

    public sealed record EntryDto(
        string Headword, string? Pronunciation, string? AudioUrl, string? PartOfSpeech,
        string? Definition, string? Notes, bool Starred, bool Archived, string Source,
        string CreatedUtc, List<string> Tags, Dictionary<string, List<string>> Relations,
        List<SentenceDto> Sentences, SchedulingDto? Scheduling, List<ReviewDto> Reviews);

    public sealed record DeckDto(string Name, string? Description, string? FilterJson, List<string> Entries);

    public sealed record Document(
        int Version, string ExportedUtc, List<EntryDto> Entries,
        List<SentenceDto> LooseSentences, List<DeckDto> Decks);

    public static string ExportJson(Library library)
    {
        var entries = library.AllEntries(includeArchived: true).Select(entry =>
        {
            var relations = library.Relations(entry.Id)
                .GroupBy(r => r.Kind.ToColumn())
                .ToDictionary(g => g.Key, g => g.Select(r => r.Word).ToList());

            var scheduling = library.GetScheduling(entry.Id);

            return new EntryDto(
                entry.Headword, entry.Pronunciation, entry.AudioUrl, entry.PartOfSpeech,
                entry.Definition, entry.Notes, entry.Starred, entry.Archived, entry.Source,
                Iso.Text(entry.CreatedUtc),
                library.TagsFor(entry.Id).ToList(),
                relations,
                library.SentencesFor(entry.Id).Select(ToDto).ToList(),
                new SchedulingDto(scheduling.Scheduler.ToColumn(), scheduling.Box, scheduling.Ease,
                    scheduling.IntervalDays, scheduling.Repetitions, scheduling.Lapses,
                    Iso.Text(scheduling.DueUtc),
                    scheduling.LastReviewedUtc is { } last ? Iso.Text(last) : null),
                library.ReviewsFor(entry.Id).Select(r => new ReviewDto(
                    r.Mode.ToColumn(), (int)r.Grade, r.Correct, r.Scheduled,
                    r.ElapsedMs, r.IntervalBefore, r.IntervalAfter, Iso.Text(r.ReviewedUtc))).ToList());
        }).ToList();

        var decks = library.Decks().Select(deck => new DeckDto(
            deck.Name, deck.Description, deck.FilterJson,
            library.DeckEntries(deck.Id).Select(e => e.Headword).ToList())).ToList();

        var document = new Document(
            FormatVersion, Iso.Text(DateTime.UtcNow), entries,
            library.AllSentences(looseOnly: true).Select(ToDto).ToList(), decks);

        return JsonSerializer.Serialize(document, Json);

        static SentenceDto ToDto(Sentence s) => new(s.Text, s.Source,
            s.ClozeStart is { } start && s.ClozeLength is { } length ? [start, length] : null);
    }

    public static ImportReport ImportJson(Library library, string json, MergePolicy policy)
    {
        Document? document;
        try { document = JsonSerializer.Deserialize<Document>(json, Json); }
        catch (JsonException e) { throw new InvalidDataException("This file is not a valid export.", e); }

        if (document is null) throw new InvalidDataException("This file is empty.");
        if (document.Version > FormatVersion)
            throw new InvalidDataException(
                $"This export was written by a newer version (format {document.Version}).");

        var added = 0; var updated = 0; var skipped = 0;
        var conflicts = new List<string>();

        foreach (var dto in document.Entries ?? [])
        {
            var existing = library.FindEntry(dto.Headword);
            var headword = dto.Headword;

            if (existing is not null)
            {
                conflicts.Add(dto.Headword);
                switch (policy)
                {
                    case MergePolicy.KeepMine:
                        skipped++;
                        continue;
                    case MergePolicy.KeepBoth:
                        headword = UniqueHeadword(library, dto.Headword);
                        existing = null;
                        break;
                }
            }

            var id = existing?.Id ?? library.AddEntry(headword, dto.Source ?? "import");
            if (existing is null) added++; else updated++;

            library.UpdateEntry(library.GetEntry(id)! with
            {
                Headword = headword,
                Pronunciation = dto.Pronunciation,
                AudioUrl = dto.AudioUrl,
                PartOfSpeech = dto.PartOfSpeech,
                Definition = dto.Definition,
                Notes = dto.Notes,
                Starred = dto.Starred,
                Archived = dto.Archived,
            });

            foreach (var tag in dto.Tags ?? []) library.TagEntry(id, tag);

            foreach (var (kind, words) in dto.Relations ?? [])
                foreach (var word in words)
                    library.AddRelation(id, Enums.RelationKindFrom(kind), word, "import");

            foreach (var sentence in dto.Sentences ?? [])
            {
                var sentenceId = library.AddSentence(sentence.Text, id, sentence.Source);
                if (sentence.Cloze is [var start, var length])
                    library.UpdateSentence(library.SentencesFor(id).Single(s => s.Id == sentenceId)
                        with { ClozeStart = start, ClozeLength = length });
            }

            if (dto.Scheduling is { } s)
                library.SaveScheduling(new Scheduling(id, Enums.SchedulerKindFrom(s.Scheduler),
                    s.Box, s.Ease, s.IntervalDays, s.Repetitions, s.Lapses,
                    Iso.Parse(s.DueUtc), Iso.ParseOrNull(s.LastReviewedUtc)));

            foreach (var r in dto.Reviews ?? [])
                library.AddReview(new Review(0, id, Enums.QuizModeFrom(r.Mode), (Grade)r.Grade,
                    r.Correct, r.Scheduled, r.ElapsedMs, r.IntervalBefore, r.IntervalAfter,
                    Iso.Parse(r.ReviewedUtc)));
        }

        foreach (var loose in document.LooseSentences ?? [])
            library.AddSentence(loose.Text, null, loose.Source);

        foreach (var deck in document.Decks ?? [])
        {
            if (library.Decks().Any(d => d.Name == deck.Name)) continue;
            var deckId = library.CreateDeck(deck.Name, deck.Description, deck.FilterJson);
            foreach (var headword in deck.Entries ?? [])
                if (library.FindEntry(headword) is { } entry) library.AddToDeck(deckId, entry.Id);
        }

        return new ImportReport(added, updated, skipped, conflicts);
    }

    private static string UniqueHeadword(Library library, string headword)
    {
        var candidate = $"{headword} (imported)";
        var n = 2;
        while (library.FindEntry(candidate) is not null) candidate = $"{headword} (imported {n++})";
        return candidate;
    }

    // ---- CSV -----------------------------------------------------------

    public static string ExportCsv(Library library)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', CsvHeader)).Append('\n');

        foreach (var entry in library.AllEntries(includeArchived: true))
        {
            var relations = library.Relations(entry.Id);
            string Words(RelationKind kind) =>
                string.Join(';', relations.Where(r => r.Kind == kind).Select(r => r.Word));

            builder.Append(string.Join(',', new[]
            {
                Quote(entry.Headword), Quote(entry.Pronunciation), Quote(entry.PartOfSpeech),
                Quote(entry.Definition), Quote(entry.Notes), entry.Starred ? "true" : "false",
                Quote(string.Join(';', library.TagsFor(entry.Id))),
                Quote(Words(RelationKind.Synonym)), Quote(Words(RelationKind.Antonym)),
                Quote(Words(RelationKind.Related)),
                Quote(string.Join(" | ", library.SentencesFor(entry.Id).Select(s => s.Text))),
            })).Append('\n');
        }

        return builder.ToString();
    }

    public static ImportReport ImportCsv(Library library, string csv, MergePolicy policy)
    {
        var rows = ParseCsv(csv);
        if (rows.Count > 0 && rows[0].Count > 0 &&
            string.Equals(rows[0][0].Trim(), "headword", StringComparison.OrdinalIgnoreCase))
            rows.RemoveAt(0);

        var added = 0; var updated = 0; var skipped = 0;
        var conflicts = new List<string>();

        foreach (var row in rows)
        {
            if (row.Count == 0) continue;
            var headword = Field(row, 0).Trim();
            if (headword.Length == 0) continue;

            var existing = library.FindEntry(headword);
            if (existing is not null)
            {
                conflicts.Add(headword);
                if (policy == MergePolicy.KeepMine) { skipped++; continue; }
                if (policy == MergePolicy.KeepBoth) { headword = UniqueHeadword(library, headword); existing = null; }
            }

            var id = existing?.Id ?? library.AddEntry(headword, "import");
            if (existing is null) added++; else updated++;

            library.UpdateEntry(library.GetEntry(id)! with
            {
                Headword = headword,
                Pronunciation = Blank(Field(row, 1)),
                PartOfSpeech = Blank(Field(row, 2)),
                Definition = Blank(Field(row, 3)),
                Notes = Blank(Field(row, 4)),
                Starred = bool.TryParse(Field(row, 5), out var starred) && starred,
            });

            foreach (var tag in Split(Field(row, 6))) library.TagEntry(id, tag);
            foreach (var word in Split(Field(row, 7))) library.AddRelation(id, RelationKind.Synonym, word, "import");
            foreach (var word in Split(Field(row, 8))) library.AddRelation(id, RelationKind.Antonym, word, "import");
            foreach (var word in Split(Field(row, 9))) library.AddRelation(id, RelationKind.Related, word, "import");

            foreach (var text in Field(row, 10).Split('|', StringSplitOptions.RemoveEmptyEntries))
                if (text.Trim().Length > 0) library.AddSentence(text.Trim(), id);
        }

        return new ImportReport(added, updated, skipped, conflicts);

        static string Field(List<string> row, int index) => index < row.Count ? row[index] : "";
        static string? Blank(string value) => value.Trim().Length == 0 ? null : value.Trim();
        static IEnumerable<string> Split(string value) =>
            value.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim())
                 .Where(v => v.Length > 0);
    }

    private static string Quote(string? value)
    {
        value ??= "";
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    /// <summary>RFC 4180 reader: handles quoted fields containing commas,
    /// doubled quotes and embedded newlines.</summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\n')
            {
                row.Add(field.ToString()); field.Clear();
                if (row.Any(f => f.Length > 0)) rows.Add(row);
                row = [];
            }
            else if (c != '\r') field.Append(c);
        }

        row.Add(field.ToString());
        if (row.Any(f => f.Length > 0)) rows.Add(row);
        return rows;
    }

    // ---- Anki ----------------------------------------------------------

    public static string ExportAnkiTsv(Library library)
    {
        var builder = new StringBuilder();

        foreach (var entry in library.AllEntries())
        {
            var synonyms = library.Relations(entry.Id)
                .Where(r => r.Kind == RelationKind.Synonym).Select(r => r.Word).ToList();

            var back = entry.Definition ?? "";
            if (synonyms.Count > 0) back += (back.Length > 0 ? " — " : "") + string.Join(", ", synonyms);

            builder.Append(Flatten(entry.Headword)).Append('\t')
                   .Append(Flatten(back)).Append('\t')
                   .Append(Flatten(string.Join(' ', library.TagsFor(entry.Id)))).Append('\n');
        }

        return builder.ToString();

        static string Flatten(string value) =>
            value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
```

Create `Core/Backups.cs`:

```csharp
using System.Text;

namespace SirWordALot.Core;

/// <summary>Timestamped JSON snapshots with a fixed retention count.</summary>
public sealed class Backups(string folder)
{
    private const string Prefix = "sir-word-a-lot-";
    private const string Extension = ".json";

    public string Folder => folder;

    public string Write(Library library, DateTime nowUtc)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{Prefix}{nowUtc:yyyyMMdd-HHmmss}{Extension}");
        WriteAtomic(path, Portability.ExportJson(library));
        return path;
    }

    public IReadOnlyList<string> List() =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, $"{Prefix}*{Extension}")
                       .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
            : [];

    public int Prune(int keep = 20)
    {
        var stale = List().Skip(Math.Max(0, keep)).ToList();
        foreach (var path in stale) File.Delete(path);
        return stale.Count;
    }

    /// <summary>Replaces the library wholesale. A pre-restore snapshot is the
    /// caller's responsibility; the Settings view writes one first.</summary>
    public ImportReport Restore(Library library, string path)
    {
        foreach (var entry in library.AllEntries(includeArchived: true)) library.DeleteEntry(entry.Id);
        foreach (var sentence in library.AllSentences(looseOnly: true)) library.DeleteSentence(sentence.Id);
        foreach (var deck in library.Decks()) library.DeleteDeck(deck.Id);

        return Portability.ImportJson(library, File.ReadAllText(path), MergePolicy.TakeTheirs);
    }

    /// <summary>Writes through a temporary file in the same directory, then renames,
    /// so a crash mid-write never leaves a truncated file at the target path.</summary>
    public static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.tmp");

        File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter "PortabilityTests|BackupTests"`
Expected: 19 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Core/Portability.cs Core/Backups.cs tests/PortabilityTests.cs tests/BackupTests.cs
git commit -m "Add JSON, CSV and Anki export/import with atomic backups"
```

---

### Task 13: Online lookup with offline tolerance

**Files:**
- Create: `Core/Lookup.cs`
- Test: `tests/LookupTests.cs`

**Interfaces:**
- Consumes: `RelationKind`.
- Produces:
  - `LookupSense(string PartOfSpeech, string Definition, IReadOnlyList<string> Examples)`
  - `LookupResult(string Word, string? Pronunciation, string? AudioUrl, IReadOnlyList<LookupSense> Senses, IReadOnlyDictionary<RelationKind, IReadOnlyList<string>> Relations, bool Online, string? Message)`
  - `ILookup` with `Task<LookupResult> LookupAsync(string word, CancellationToken ct)`
  - `Lookup(HttpMessageHandler? handler = null) : ILookup`
  - `Lookup.Offline(string word, string message) -> LookupResult` (static)
  - `Lookup.ParseDatamuse(string json) -> IReadOnlyList<string>` (static)
  - `Lookup.ParseDictionary(string json) -> (string? Pronunciation, string? AudioUrl, IReadOnlyList<LookupSense> Senses)` (static)

Any network failure, timeout, non-success status or malformed payload produces
an offline `LookupResult` with a message — never an exception. Partial success
is kept: Datamuse relations survive a dictionaryapi failure and vice versa.

- [ ] **Step 1: Write the failing test**

Create `tests/LookupTests.cs`:

```csharp
using System.Net;
using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class LookupTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private const string DatamuseJson = """
        [{"word":"prominence","score":900},{"word":"renown","score":800},{"word":"","score":1}]
        """;

    private const string DictionaryJson = """
        [{
          "word":"eminence",
          "phonetic":"/ˈɛmɪnəns/",
          "phonetics":[{"text":"/ɛmɪnəns/","audio":""},
                       {"text":"/ˈɛmɪnəns/","audio":"https://example.invalid/eminence.mp3"}],
          "meanings":[
            {"partOfSpeech":"noun",
             "definitions":[{"definition":"A position of superiority.","example":"She rose to eminence."},
                            {"definition":"A piece of high ground."}],
             "synonyms":["prominence"],"antonyms":["obscurity"]}
          ]
        }]
        """;

    [Fact]
    public void ParseDatamuseReturnsWordsAndDropsBlanks() =>
        Assert.Equal(new[] { "prominence", "renown" }, Lookup.ParseDatamuse(DatamuseJson));

    [Fact]
    public void ParseDatamuseToleratesRubbish()
    {
        Assert.Empty(Lookup.ParseDatamuse("not json"));
        Assert.Empty(Lookup.ParseDatamuse("{}"));
        Assert.Empty(Lookup.ParseDatamuse("[]"));
    }

    [Fact]
    public void ParseDictionaryExtractsPronunciationAudioAndSenses()
    {
        var (pronunciation, audio, senses) = Lookup.ParseDictionary(DictionaryJson);

        Assert.Equal("/ˈɛmɪnəns/", pronunciation);
        Assert.Equal("https://example.invalid/eminence.mp3", audio);
        Assert.Equal(2, senses.Count);
        Assert.Equal("noun", senses[0].PartOfSpeech);
        Assert.Equal("A position of superiority.", senses[0].Definition);
        Assert.Equal(new[] { "She rose to eminence." }, senses[0].Examples);
        Assert.Empty(senses[1].Examples);
    }

    [Fact]
    public void ParseDictionaryToleratesRubbish()
    {
        Assert.Empty(Lookup.ParseDictionary("not json").Senses);
        Assert.Empty(Lookup.ParseDictionary("""{"title":"No Definitions Found"}""").Senses);
    }

    [Fact]
    public async Task LookupCombinesBothServices()
    {
        var lookup = new Lookup(new StubHandler(request =>
            request.RequestUri!.Host.Contains("datamuse") ? Ok(DatamuseJson) : Ok(DictionaryJson)));

        var result = await lookup.LookupAsync("eminence", CancellationToken.None);

        Assert.True(result.Online);
        Assert.Equal("eminence", result.Word);
        Assert.Equal("/ˈɛmɪnəns/", result.Pronunciation);
        Assert.Equal(2, result.Senses.Count);
        Assert.Equal(new[] { "prominence", "renown" }, result.Relations[RelationKind.Synonym]);
        Assert.NotEmpty(result.Relations[RelationKind.Antonym]);
        Assert.NotEmpty(result.Relations[RelationKind.Related]);
        Assert.NotEmpty(result.Relations[RelationKind.Rhyme]);
    }

    [Fact]
    public async Task ANetworkFailureProducesAnOfflineResultRatherThanThrowing()
    {
        var lookup = new Lookup(new StubHandler(_ => throw new HttpRequestException("no route")));

        var result = await lookup.LookupAsync("eminence", CancellationToken.None);

        Assert.False(result.Online);
        Assert.Equal("eminence", result.Word);
        Assert.Empty(result.Senses);
        Assert.All(result.Relations.Values, words => Assert.Empty(words));
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task DictionaryFailureStillKeepsThesaurusResults()
    {
        var lookup = new Lookup(new StubHandler(request =>
            request.RequestUri!.Host.Contains("datamuse")
                ? Ok(DatamuseJson)
                : new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await lookup.LookupAsync("eminence", CancellationToken.None);

        Assert.True(result.Online);
        Assert.Empty(result.Senses);
        Assert.Equal(new[] { "prominence", "renown" }, result.Relations[RelationKind.Synonym]);
    }

    [Fact]
    public async Task ThesaurusFailureStillKeepsDictionaryResults()
    {
        var lookup = new Lookup(new StubHandler(request =>
            request.RequestUri!.Host.Contains("datamuse")
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Ok(DictionaryJson)));

        var result = await lookup.LookupAsync("eminence", CancellationToken.None);

        Assert.True(result.Online);
        Assert.Equal(2, result.Senses.Count);
        Assert.Empty(result.Relations[RelationKind.Synonym]);
    }

    [Fact]
    public async Task ABlankWordIsRejectedWithoutTouchingTheNetwork()
    {
        var called = false;
        var lookup = new Lookup(new StubHandler(_ => { called = true; return Ok("[]"); }));

        var result = await lookup.LookupAsync("   ", CancellationToken.None);

        Assert.False(called);
        Assert.False(result.Online);
    }

    [Fact]
    public void OfflineResultCarriesEveryRelationKindAsAnEmptyList()
    {
        var result = Lookup.Offline("eminence", "No network.");

        Assert.False(result.Online);
        Assert.Equal(4, result.Relations.Count);
        Assert.All(result.Relations.Values, words => Assert.Empty(words));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter LookupTests`
Expected: FAIL — `Lookup` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Core/Lookup.cs`:

```csharp
using System.Text.Json;

namespace SirWordALot.Core;

public sealed record LookupSense(string PartOfSpeech, string Definition, IReadOnlyList<string> Examples);

public sealed record LookupResult(
    string Word,
    string? Pronunciation,
    string? AudioUrl,
    IReadOnlyList<LookupSense> Senses,
    IReadOnlyDictionary<RelationKind, IReadOnlyList<string>> Relations,
    bool Online,
    string? Message);

public interface ILookup
{
    Task<LookupResult> LookupAsync(string word, CancellationToken ct);
}

/// <summary>Datamuse for relations, dictionaryapi.dev for senses. Never throws:
/// any failure becomes an offline result carrying a message.</summary>
public sealed class Lookup : ILookup
{
    private const string DatamuseBase = "https://api.datamuse.com/words";
    private const string DictionaryBase = "https://api.dictionaryapi.dev/api/v2/entries/en";

    private static readonly RelationKind[] Kinds =
        [RelationKind.Synonym, RelationKind.Antonym, RelationKind.Related, RelationKind.Rhyme];

    private readonly HttpClient _http;

    public Lookup(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(6);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SirWordALot/0.1");
    }

    public static LookupResult Offline(string word, string message) => new(
        word, null, null, [],
        Kinds.ToDictionary(k => k, _ => (IReadOnlyList<string>)[]),
        Online: false, message);

    public async Task<LookupResult> LookupAsync(string word, CancellationToken ct)
    {
        var term = word.Trim();
        if (term.Length == 0) return Offline(word, "Enter a word to look up.");

        var relations = new Dictionary<RelationKind, IReadOnlyList<string>>();
        var anySucceeded = false;

        foreach (var (kind, parameter) in new[]
        {
            (RelationKind.Synonym, "rel_syn"),
            (RelationKind.Antonym, "rel_ant"),
            (RelationKind.Related, "ml"),
            (RelationKind.Rhyme, "rel_rhy"),
        })
        {
            var body = await GetAsync($"{DatamuseBase}?{parameter}={Uri.EscapeDataString(term)}&max=24", ct);
            relations[kind] = body is null ? [] : ParseDatamuse(body);
            anySucceeded |= body is not null;
        }

        var dictionaryBody = await GetAsync($"{DictionaryBase}/{Uri.EscapeDataString(term)}", ct);
        anySucceeded |= dictionaryBody is not null;

        var (pronunciation, audio, senses) = dictionaryBody is null
            ? (null, null, (IReadOnlyList<LookupSense>)[])
            : ParseDictionary(dictionaryBody);

        return anySucceeded
            ? new LookupResult(term, pronunciation, audio, senses, relations, Online: true, Message: null)
            : Offline(term, "Lookup unavailable. Check your connection or add the word by hand.");
    }

    private async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException
                                       or UriFormatException or IOException)
        {
            return null;
        }
    }

    public static IReadOnlyList<string> ParseDatamuse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

            return document.RootElement.EnumerateArray()
                .Select(element => element.TryGetProperty("word", out var word) ? word.GetString() : null)
                .Where(word => !string.IsNullOrWhiteSpace(word))
                .Select(word => word!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException) { return []; }
    }

    public static (string? Pronunciation, string? AudioUrl, IReadOnlyList<LookupSense> Senses)
        ParseDictionary(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return (null, null, []);

            string? pronunciation = null;
            string? audio = null;
            var senses = new List<LookupSense>();

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (pronunciation is null && entry.TryGetProperty("phonetic", out var phonetic))
                    pronunciation = Blank(phonetic.GetString());

                if (entry.TryGetProperty("phonetics", out var phonetics) &&
                    phonetics.ValueKind == JsonValueKind.Array)
                    foreach (var item in phonetics.EnumerateArray())
                    {
                        if (audio is null && item.TryGetProperty("audio", out var a))
                            audio = Blank(a.GetString());
                        if (pronunciation is null && item.TryGetProperty("text", out var t))
                            pronunciation = Blank(t.GetString());
                    }

                if (!entry.TryGetProperty("meanings", out var meanings) ||
                    meanings.ValueKind != JsonValueKind.Array) continue;

                foreach (var meaning in meanings.EnumerateArray())
                {
                    var partOfSpeech = meaning.TryGetProperty("partOfSpeech", out var pos)
                        ? pos.GetString() ?? "" : "";

                    if (!meaning.TryGetProperty("definitions", out var definitions) ||
                        definitions.ValueKind != JsonValueKind.Array) continue;

                    foreach (var definition in definitions.EnumerateArray())
                    {
                        var text = definition.TryGetProperty("definition", out var d) ? Blank(d.GetString()) : null;
                        if (text is null) continue;

                        var examples = definition.TryGetProperty("example", out var e) && Blank(e.GetString()) is { } x
                            ? new[] { x }
                            : [];

                        senses.Add(new LookupSense(partOfSpeech, text, examples));
                    }
                }
            }

            return (pronunciation, audio, senses);
        }
        catch (JsonException) { return (null, null, []); }

        static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter LookupTests`
Expected: 10 tests PASS.

- [ ] **Step 5: Run the whole Core suite and commit**

Run: `dotnet test tests/SirWordALot.Tests.csproj`
Expected: every test passes. This is the gate before any UI work starts.

```bash
git add Core/Lookup.cs tests/LookupTests.cs
git commit -m "Add Datamuse and dictionary lookup with offline tolerance"
```

---

### Task 14: The visual system

**Files:**
- Create: `Shell/Ui.cs`, `App.axaml`, `App.cs`
- Modify: `Program.cs`
- Test: `tools/Checks/Checks.csproj`, `tools/Checks/Program.cs`

**Interfaces:**
- Consumes: nothing from Core.
- Produces in `SirWordALot.Shell`:
  - `Ui.Ink/Cream/Muted/Line/Phosphor/Dark/Sidebar/RowRule/WindowBorder/TitlebarActive/TitlebarInactive/Danger -> IBrush`
  - `Ui.Mono/Serif -> FontFamily`
  - `Ui.Text(string, double size = 13, IBrush? color = null, bool mono = false) -> TextBlock`
  - `Ui.Label(string, IBrush? color = null) -> TextBlock` — 10.5px mono, 1.2 letter-spacing, uppercase caller-supplied
  - `Ui.Display(string, double size = 32) -> TextBlock` — Georgia
  - `Ui.Stack(double spacing, params Control[]) -> StackPanel`
  - `Ui.Row(double spacing, params Control[]) -> StackPanel`
  - `Ui.Rule(IBrush? brush = null) -> Border`
  - `Ui.Pad(Control, double padding = 24) -> Border`
  - `Ui.Columns(string definitions, params Control[]) -> Grid`
  - `Ui.Rows(string definitions, params Control[]) -> Grid`
  - `Ui.Scroll(Control) -> ScrollViewer`
  - `Ui.Button(string, Action, string? cls = null) -> Button`
  - `Ui.IconButton(string icon, string tip, Action) -> Button`
  - `Ui.Chip(string text, Action? click = null, Action? remove = null) -> Control`
  - `Ui.Heading(string eyebrow, string title, string subtitle) -> Control`
  - `Ui.StatTile(string index, string value, string caption) -> Control`
  - `Ui.MasteryBar(int percent) -> Control`
  - `Ui.HexId(long id) -> string` — the `0X 4A 92` eyebrow
  - `Glyph(string kind) : Control` with the HammerOS path set plus `star`, `quiz`, `stats`, `deck`, `tag`, `import`, `export`, `sound`
  - `Motion.Enabled`, `Motion.Enter(Control, double distance = 16)`, `Motion.For(...)`, `Motion.Cancel(...)`

- [ ] **Step 1: Write the failing test**

Create `tools/Checks/Checks.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>SirWordALot.Checks</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../../Core/**/*.cs" LinkBase="Core" />
    <Compile Include="../../Shell/**/*.cs" LinkBase="Shell" />
    <Compile Include="../../Views/**/*.cs" LinkBase="Views" />
  </ItemGroup>
  <ItemGroup>
    <AvaloniaResource Include="../../Assets/**" />
    <PackageReference Include="Avalonia" Version="11.3.0" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.0" />
    <PackageReference Include="Avalonia.Headless" Version="11.3.0" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.0" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.3.0" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.0" />
  </ItemGroup>
</Project>
```

Create `tools/Checks/Program.cs`. This is the headless harness every later UI
task extends; it exits non-zero on the first failure.

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using SirWordALot.Shell;

namespace SirWordALot.Checks;

internal static class Program
{
    private static int _failures;

    [STAThread]
    public static int Main(string[] args)
    {
        AppBuilder.Configure<HeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .WithInterFont()
            .SetupWithoutStarting();

        CheckPalette();
        CheckTypography();
        CheckFactories();

        Console.WriteLine(_failures == 0 ? "All checks passed." : $"{_failures} check(s) failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static void CheckPalette()
    {
        Expect("Ink", "#FF173B40", Hex(Ui.Ink));
        Expect("Cream", "#FFECEDE0", Hex(Ui.Cream));
        Expect("Muted", "#FF688178", Hex(Ui.Muted));
        Expect("Line", "#FFBCC8BB", Hex(Ui.Line));
        Expect("Phosphor", "#FFB3E5DA", Hex(Ui.Phosphor));
        Expect("Dark", "#FF102C32", Hex(Ui.Dark));
        Expect("Sidebar", "#FFDFE4D6", Hex(Ui.Sidebar));
        Expect("RowRule", "#FFD8DECF", Hex(Ui.RowRule));
        Expect("WindowBorder", "#FF9CAEA0", Hex(Ui.WindowBorder));
        Expect("TitlebarActive", "#FFE9EBDD", Hex(Ui.TitlebarActive));
        Expect("TitlebarInactive", "#FFCCD6C9", Hex(Ui.TitlebarInactive));
        Expect("Danger", "#FF99513D", Hex(Ui.Danger));
    }

    private static void CheckTypography()
    {
        var label = Ui.Label("LIBRARY");
        Expect("label size", 10.5, label.FontSize);
        Expect("label spacing", 1.2, label.LetterSpacing);
        Expect("label font", "Cascadia Mono", label.FontFamily.Name);
        Expect("label colour", Hex(Ui.Muted), Hex(label.Foreground!));

        Expect("display font", "Georgia", Ui.Display("Eminence").FontFamily.Name);
        Expect("display size", 32d, Ui.Display("Eminence").FontSize);
        Expect("body size", 13d, Ui.Text("body").FontSize);
        Expect("mono text font", "Cascadia Mono", Ui.Text("x", mono: true).FontFamily.Name);
    }

    private static void CheckFactories()
    {
        Expect("rule height", 1d, Ui.Rule().Height);
        Expect("button radius", 0d, Ui.Button("Save", () => { }).CornerRadius.TopLeft);
        Expect("hex id", "0X 4A 92", Ui.HexId(0x4A92));
        Expect("hex id pads", "0X 00 07", Ui.HexId(7));

        var columns = Ui.Columns("158,*", new Border(), new Border());
        Expect("columns count", 2, columns.ColumnDefinitions.Count);
        Expect("columns width", 158d, columns.ColumnDefinitions[0].Width.Value);

        var glyph = new Glyph("search") { Width = 15, Height = 15 };
        Expect("glyph width", 15d, glyph.Width);
    }

    private static string Hex(IBrush brush) => ((ISolidColorBrush)brush).Color.ToString().ToUpperInvariant();

    private static void Expect<T>(string name, T expected, T actual)
    {
        if (Equals(expected, actual)) { Console.WriteLine($"  ok    {name}"); return; }
        Console.WriteLine($"  FAIL  {name}: expected {expected}, got {actual}");
        _failures++;
    }
}

internal sealed class HeadlessApp : Application
{
    public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
}
```

- [ ] **Step 2: Run the checks to verify they fail**

Run: `dotnet run --project tools/Checks`
Expected: FAIL — `Ui`, `Glyph` and `Motion` do not exist.

- [ ] **Step 3: Write the implementation**

Create `Shell/Ui.cs`. The palette, `Glyph` and `Motion` are ported from
`D:\Workhammer\hammeros\Desktop\Ui.cs`; the chip, stat tile, mastery bar and
hex id are new.

```csharp
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;

namespace SirWordALot.Shell;

public static class Ui
{
    public static readonly IBrush
        Ink = Brush.Parse("#173B40"),
        Cream = Brush.Parse("#ECEDE0"),
        Muted = Brush.Parse("#688178"),
        Line = Brush.Parse("#BCC8BB"),
        Phosphor = Brush.Parse("#B3E5DA"),
        Dark = Brush.Parse("#102C32"),
        Sidebar = Brush.Parse("#DFE4D6"),
        RowRule = Brush.Parse("#D8DECF"),
        WindowBorder = Brush.Parse("#9CAEA0"),
        TitlebarActive = Brush.Parse("#E9EBDD"),
        TitlebarInactive = Brush.Parse("#CCD6C9"),
        Danger = Brush.Parse("#99513D");

    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    public static readonly FontFamily Serif = new("Georgia, Times New Roman");

    public static TextBlock Text(string text, double size = 13, IBrush? color = null, bool mono = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = color ?? Ink,
        FontFamily = mono ? Mono : FontFamily.Default,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Label(string text, IBrush? color = null) => new()
    {
        Text = text,
        FontSize = 10.5,
        LetterSpacing = 1.2,
        Foreground = color ?? Muted,
        FontFamily = Mono,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Display(string text, double size = 32) => new()
    {
        Text = text,
        FontFamily = Serif,
        FontSize = size,
        Foreground = Ink,
        TextWrapping = TextWrapping.Wrap,
    };

    public static StackPanel Stack(double spacing = 12, params Control[] children)
    {
        var panel = new StackPanel { Spacing = spacing };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    public static StackPanel Row(double spacing = 10, params Control[] children)
    {
        var panel = Stack(spacing, children);
        panel.Orientation = Orientation.Horizontal;
        return panel;
    }

    public static Border Rule(IBrush? brush = null) => new()
    {
        Height = 1,
        Background = brush ?? Line,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    public static Border Pad(Control child, double padding = 24) =>
        new() { Padding = new Thickness(padding), Child = child };

    public static Grid Columns(string definitions, params Control[] controls)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(definitions) };
        for (var i = 0; i < controls.Length; i++)
        {
            Grid.SetColumn(controls[i], i);
            grid.Children.Add(controls[i]);
        }
        return grid;
    }

    public static Grid Rows(string definitions, params Control[] controls)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions(definitions) };
        for (var i = 0; i < controls.Length; i++)
        {
            Grid.SetRow(controls[i], i);
            grid.Children.Add(controls[i]);
        }
        return grid;
    }

    public static ScrollViewer Scroll(Control content) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
    };

    public static Button Button(string text, Action action, string? cls = null)
    {
        var button = new Button { Content = text };
        if (cls is not null) button.Classes.Add(cls);
        button.Click += (_, _) => action();
        button.Transitions =
        [
            new BrushTransition
            {
                Property = Avalonia.Controls.Button.BackgroundProperty,
                Duration = TimeSpan.FromMilliseconds(140),
            },
        ];
        return button;
    }

    public static Button IconButton(string icon, string tip, Action action)
    {
        var button = Button("", action);
        button.Content = new Glyph(icon) { Width = 15, Height = 15 };
        ToolTip.SetTip(button, tip);
        return button;
    }

    /// <summary>A relation or tag chip. Clicking the body runs <paramref name="click"/>;
    /// the trailing × appears only when <paramref name="remove"/> is supplied.</summary>
    public static Control Chip(string text, Action? click = null, Action? remove = null)
    {
        var label = Text(text, 12);
        var body = Button("", click ?? (() => { }));
        body.Content = label;
        body.Padding = new Thickness(10, 5);
        body.IsHitTestVisible = click is not null;

        var border = new Border
        {
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            Background = Brush.Parse("#F3F4E9"),
            Child = remove is null ? body : Row(0, body, IconButton("close", "Remove", remove)),
        };
        return border;
    }

    public static Control Heading(string eyebrow, string title, string subtitle) => Stack(10,
        Label(eyebrow),
        Display(title),
        new TextBlock { Text = subtitle, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap });

    /// <summary>The Macrodata Refinement stat tile: index, value, rule, caption.</summary>
    public static Control StatTile(string index, string value, string caption) => new Border
    {
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(16, 14),
        Child = Stack(10,
            Columns("*,Auto",
                new TextBlock { Text = index, FontFamily = Mono, FontSize = 20, Foreground = Ink },
                new TextBlock
                {
                    Text = value, FontFamily = Mono, FontSize = 12, Foreground = Muted,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right,
                }),
            Rule(),
            Label(caption)),
    };

    public static Control MasteryBar(int percent)
    {
        var track = new Border { Height = 3, Background = RowRule, HorizontalAlignment = HorizontalAlignment.Stretch };
        var fill = new Border
        {
            Height = 3,
            Background = Brush.Parse("#4E8A7B"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Clamp(percent, 0, 100),
        };
        var grid = new Grid { Width = 100 };
        grid.Children.Add(track);
        grid.Children.Add(fill);
        return grid;
    }

    /// <summary>The institutional eyebrow identifier, e.g. <c>0X 4A 92</c>.</summary>
    public static string HexId(long id) => "0X " + $"{id & 0xFFFF:X4}".Insert(2, " ");
}

/// <summary>Hands a URL to the operating system's default handler. Used for
/// pronunciation audio, which the OS media player handles better than we would.</summary>
public static class Launch
{
    public static bool Url(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.ToString())
            { UseShellExecute = true });
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException
                                       or System.IO.FileNotFoundException)
        {
            return false;
        }
    }
}
```

Add a check for it in `CheckFactories`:

```csharp
        Expect("launch rejects rubbish", false, Launch.Url("javascript:alert(1)"));
        Expect("launch rejects blanks", false, Launch.Url(""));
```

Append `Motion` and `Glyph` to the same file, copied from
`D:\Workhammer\hammeros\Desktop\Ui.cs` lines 35–94, with the namespace changed
to `SirWordALot.Shell`, `Ui.Ink` kept as the default glyph colour, and these
paths added to the dictionary:

```csharp
        ["star"] = "M12,3 L14.8,9.3 L21.6,10 L16.5,14.6 L18,21.3 L12,17.8 L6,21.3 L7.5,14.6 L2.4,10 L9.2,9.3 Z",
        ["quiz"] = "M12,2 A10,10 0 1 1 11.99,2 M9,9 A3,3 0 1 1 12,13 L12,15 M12,18 L12,18.5",
        ["stats"] = "M3,21 L21,21 M6,21 L6,12 M11,21 L11,6 M16,21 L16,15 M21,21 L21,9",
        ["deck"] = "M4,7 L14,4 L20,6 L20,18 L10,21 L4,19 Z M10,21 L10,9 L20,6 M4,7 L10,9",
        ["tag"] = "M3,3 L11,3 L21,13 L13,21 L3,11 Z M7,7 L7.5,7",
        ["import"] = "M12,3 L12,15 M7,10 L12,15 L17,10 M3,20 L21,20",
        ["export"] = "M12,15 L12,3 M7,8 L12,3 L17,8 M3,20 L21,20",
        ["sound"] = "M4,9 L8,9 L13,5 L13,19 L8,15 L4,15 Z M17,8 A6,6 0 0 1 17,16",
        ["word"] = "M4,20 L9,4 L11,4 L16,20 M6.5,14 L13.5,14 M19,9 L19,20",
```

Create `App.axaml` — the HammerOS control styles verbatim, with the class names
this app uses:

```xml
<Application xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="SirWordALot.App" RequestedThemeVariant="Light">
  <Application.Styles>
    <FluentTheme>
      <FluentTheme.Palettes><ColorPaletteResources x:Key="Light" Accent="#416C5D"/></FluentTheme.Palettes>
    </FluentTheme>
    <Style Selector="Button /template/ ContentPresenter#PART_ContentPresenter">
      <Setter Property="Transitions">
        <Transitions>
          <BrushTransition Property="Background" Duration="0:0:0.14"/>
          <BrushTransition Property="Foreground" Duration="0:0:0.14"/>
        </Transitions>
      </Setter>
    </Style>
    <Style Selector="Window">
      <Setter Property="FontFamily" Value="Inter, Segoe UI"/>
      <Setter Property="FontSize" Value="13"/>
      <Setter Property="Foreground" Value="#183C40"/>
    </Style>
    <Style Selector="Button">
      <Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="#183C40"/>
      <Setter Property="CornerRadius" Value="0"/><Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Padding" Value="12,8"/><Setter Property="Cursor" Value="Hand"/>
      <Setter Property="HorizontalContentAlignment" Value="Center"/>
    </Style>
    <Style Selector="Button:pointerover /template/ ContentPresenter#PART_ContentPresenter">
      <Setter Property="Background" Value="#D4DED6"/><Setter Property="Foreground" Value="#123A3F"/>
    </Style>
    <Style Selector="Button:pressed /template/ ContentPresenter#PART_ContentPresenter">
      <Setter Property="Background" Value="#B6CCC3"/>
    </Style>
    <Style Selector="Button.primary">
      <Setter Property="Background" Value="#214B4D"/><Setter Property="Foreground" Value="#F0F0DF"/>
      <Setter Property="Padding" Value="18,11"/>
    </Style>
    <Style Selector="Button.outline">
      <Setter Property="BorderBrush" Value="#AABBB2"/><Setter Property="BorderThickness" Value="1"/>
    </Style>
    <Style Selector="Button.nav">
      <Setter Property="HorizontalAlignment" Value="Stretch"/>
      <Setter Property="HorizontalContentAlignment" Value="Left"/>
      <Setter Property="Padding" Value="18,9"/>
    </Style>
    <Style Selector="Button.nav.selected /template/ ContentPresenter#PART_ContentPresenter">
      <Setter Property="Background" Value="#CBD7C9"/>
    </Style>
    <Style Selector="Button.dark"><Setter Property="Foreground" Value="#9FCAC7"/></Style>
    <Style Selector="Button.dark:pointerover /template/ ContentPresenter#PART_ContentPresenter">
      <Setter Property="Background" Value="#24494D"/><Setter Property="Foreground" Value="#C5F6ED"/>
    </Style>
    <Style Selector="TextBox">
      <Setter Property="CornerRadius" Value="0"/><Setter Property="Background" Value="#F5F3E8"/>
      <Setter Property="BorderBrush" Value="#AFBEB5"/><Setter Property="Foreground" Value="#183C40"/>
      <Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="10,8"/>
    </Style>
    <Style Selector="TextBox:focus /template/ Border#PART_BorderElement">
      <Setter Property="BorderBrush" Value="#406E65"/><Setter Property="BorderThickness" Value="1"/>
    </Style>
    <Style Selector="TextBox.terminal">
      <Setter Property="FontFamily" Value="Cascadia Mono, Consolas"/>
      <Setter Property="Foreground" Value="#AFE2D8"/><Setter Property="CaretBrush" Value="#AFE2D8"/>
      <Setter Property="Background" Value="#102E34"/><Setter Property="BorderThickness" Value="0"/>
    </Style>
    <Style Selector="TextBox.terminal:focus /template/ Border#PART_BorderElement">
      <Setter Property="Background" Value="#102E34"/><Setter Property="BorderThickness" Value="0"/>
    </Style>
    <Style Selector="ComboBox"><Setter Property="CornerRadius" Value="0"/></Style>
    <Style Selector="ToolTip">
      <Setter Property="Background" Value="#193C40"/><Setter Property="Foreground" Value="#F0F0DF"/>
      <Setter Property="CornerRadius" Value="0"/>
    </Style>
  </Application.Styles>
</Application>
```

Create `App.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var db = Db.Open(Db.DefaultPath());
            var library = new Library(db);
            var settings = new Settings(db);
            Motion.Enabled = settings.Motion;
            desktop.MainWindow = new MainWindow(library, settings, new Search(db), new Stats(db), new Lookup());
        }
        base.OnFrameworkInitializationCompleted();
    }
}
```

Replace `Program.cs` with the Avalonia entry point, keeping the CLI flags:

```csharp
using Avalonia;

namespace SirWordALot;

internal static class Program
{
    public const string Version = "0.1.0";

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--version") || args.Contains("-v"))
        {
            Console.WriteLine($"Sir Word-a-lot {Version}");
            return;
        }

        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine(Help);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();

    private const string Help = """
        Sir Word-a-lot - a vocabulary and thesaurus trainer

          -v, --version   Print version and exit
          -h, --help      Show this help and exit

        Views
          Library     Browse, search, filter and edit every word
          Sentences   The sentence bank; link loose sentences to words
          Lookup      Fetch synonyms, senses and examples online
          Quiz        Flip, choice, cloze and typed drills over SM-2 or Leitner
          Decks       Manual decks and smart decks saved from a filter
          Stats       Streak, retention, review heatmap and mastery breakdown
          Console     Type commands against the real library
          Settings    Palette, scheduler, daily caps, capture hotkey, backups

        Keys
          Ctrl+Space  Command palette          /   Focus search
          Ctrl+Alt+W  Capture clipboard word   N   New word
          Q           Start a quiz             Space  Flip the card
          1-4         Grade Again/Hard/Good/Easy     Esc  Back

        Data lives in %APPDATA%\fezcode\SirWordALot.
        """;
}
```

- [ ] **Step 4: Run the checks to verify they pass**

Run: `dotnet build SirWordALot.csproj; dotnet run --project tools/Checks`
Expected: every check prints `ok`, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add Shell/Ui.cs App.axaml App.cs Program.cs tools/Checks
git commit -m "Add the HammerOS visual system and headless check harness"
```

---

### Task 15: The window shell

**Files:**
- Create: `Shell/MainWindow.cs`, `Shell/Toast.cs`
- Modify: `tools/Checks/Program.cs`
- Test: the new `CheckShell` routine in `tools/Checks/Program.cs`

**Interfaces:**
- Consumes: `Library`, `Settings`, `Search`, `Stats`, `ILookup`, `Ui`, `Glyph`, `Motion`.
- Produces:
  - `AppContext(Library Library, Settings Settings, Search Search, Stats Stats, ILookup Lookup, Action<string> Toast, Action<string, object?> Navigate)`
  - `NavItem(string Id, string Title, string Glyph)`
  - `MainWindow(Library, Settings, Search, Stats, ILookup)` with
    `.Navigate(string id, object? argument = null)`, `.ShowToast(string message)`,
    `.CurrentViewId -> string`, `.Nav -> IReadOnlyList<NavItem>`,
    `.Titlebar -> Border`, `.SidebarPanel -> Border`, `.Footer -> Border`,
    `.Host -> ContentControl`, `.StatusRight -> TextBlock`
  - `Toast.Show(Panel host, string message)`

Views are created lazily on first navigation and cached. Each view is a
`UserControl` whose constructor takes `AppContext`; the ones that subscribe to
`Library.Changed` implement `IDisposable` and are disposed when the window
closes.

- [ ] **Step 1: Write the failing check**

Append to `tools/Checks/Program.cs`, and call `CheckShell();` from `Main`
after `CheckFactories();`:

```csharp
    private static void CheckShell()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        var id = library.AddEntry("eminence");
        library.UpdateEntry(library.GetEntry(id)! with { Definition = "Fame." });

        var window = new MainWindow(library, new Settings(db), new Search(db), new Stats(db),
            new StubLookup());
        window.Show();

        Expect("no system decorations", SystemDecorations.None, window.SystemDecorations);
        Expect("default width", 1180d, window.Width);
        Expect("default height", 760d, window.Height);
        Expect("min width", 1040d, window.MinWidth);
        Expect("min height", 680d, window.MinHeight);
        Expect("window background", "#FFECEDE0", Hex(window.Background!));

        Expect("titlebar height", 43d, window.Titlebar.Height);
        Expect("titlebar colour", "#FFE9EBDD", Hex(window.Titlebar.Background!));
        Expect("sidebar width", 170d, window.SidebarPanel.Width);
        Expect("sidebar colour", "#FFDFE4D6", Hex(window.SidebarPanel.Background!));
        Expect("footer height", 42d, window.Footer.Height);
        Expect("nav items", 12, window.Nav.Count);
        Expect("starts on library", "library", window.CurrentViewId);

        window.Navigate("stats");
        Expect("navigates", "stats", window.CurrentViewId);
        Expect("host filled", true, window.Host.Content is not null);

        window.Navigate("library");
        Expect("returns", "library", window.CurrentViewId);
        Expect("status counts words", true, window.StatusRight.Text!.Contains("1 WORDS"));

        window.Close();
    }

    private sealed class StubLookup : ILookup
    {
        public Task<LookupResult> LookupAsync(string word, CancellationToken ct) =>
            Task.FromResult(Lookup.Offline(word, "Checks run offline."));
    }
```

Add these usings at the top of `tools/Checks/Program.cs`:

```csharp
using SirWordALot.Core;
using SirWordALot.Views;
```

- [ ] **Step 2: Run the checks to verify they fail**

Run: `dotnet run --project tools/Checks`
Expected: FAIL — `MainWindow` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Shell/Toast.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace SirWordALot.Shell;

/// <summary>A transient message in the bottom-right corner, in the institutional voice.</summary>
public static class Toast
{
    public static void Show(Panel host, string message)
    {
        var border = new Border
        {
            Background = Ui.Dark,
            Padding = new Thickness(18, 12),
            Margin = new Thickness(0, 0, 26, 58),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = Ui.Text(message, 12, Ui.Phosphor, mono: true),
            Opacity = 0,
        };

        host.Children.Add(border);
        Motion.For(border, 180, t => border.Opacity = t);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Motion.For(border, 220, t => border.Opacity = 1 - t, () => host.Children.Remove(border));
        };
        timer.Start();
    }
}
```

Create `Shell/MainWindow.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SirWordALot.Core;
using SirWordALot.Views;

namespace SirWordALot.Shell;

/// <summary>Everything a view needs from the shell, passed by constructor so views
/// stay independently constructible in the headless checks.</summary>
public sealed record AppContext(
    Library Library,
    Settings Settings,
    Search Search,
    Stats Stats,
    ILookup Lookup,
    Action<string> Toast,
    Action<string, object?> Navigate);

public sealed record NavItem(string Id, string Title, string Glyph);

public sealed class MainWindow : Window
{
    private static readonly NavItem[] Items =
    [
        new("library", "ALL WORDS", "word"),
        new("due", "DUE TODAY", "quiz"),
        new("starred", "STARRED", "star"),
        new("recent", "RECENT", "refresh"),
        new("archived", "ARCHIVED", "folder"),
        new("sentences", "SENTENCES", "notes"),
        new("lookup", "LOOKUP", "search"),
        new("quiz", "QUIZ", "refinement"),
        new("decks", "DECKS", "deck"),
        new("stats", "STATS", "stats"),
        new("console", "CONSOLE", "terminal"),
        new("settings", "SETTINGS", "settings"),
    ];

    private readonly Library _library;
    private readonly Settings _settings;
    private readonly AppContext _context;
    private readonly Dictionary<string, Control> _views = [];
    private readonly Dictionary<string, Button> _navButtons = [];
    private readonly Panel _overlay = new();
    private readonly TextBlock _statusLeft = Ui.Label("SWL / LIBRARY");
    private readonly TextBlock _statusRight = Ui.Label("");

    public IReadOnlyList<NavItem> Nav => Items;
    public string CurrentViewId { get; private set; } = "";
    public Border Titlebar { get; }
    public Border SidebarPanel { get; }
    public Border Footer { get; }
    public ContentControl Host { get; } = new();
    public TextBlock StatusRight => _statusRight;

    public MainWindow(Library library, Settings settings, Search search, Stats stats, ILookup lookup)
    {
        _library = library;
        _settings = settings;

        Title = "Sir Word-a-lot";
        SystemDecorations = SystemDecorations.None;
        Background = Ui.Cream;
        Width = 1180; Height = 760;
        MinWidth = 1040; MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _context = new AppContext(library, settings, search, stats, lookup, ShowToast,
            (id, argument) => Navigate(id, argument));

        Titlebar = BuildTitlebar();
        SidebarPanel = BuildSidebar();
        Footer = BuildFooter();

        var body = Ui.Columns("170,*", SidebarPanel, Host);
        Grid.SetRow(body, 1);

        var root = Ui.Rows("43,*,42", Titlebar, body, Footer);
        var framed = new Border
        {
            BorderBrush = Ui.WindowBorder,
            BorderThickness = new Thickness(1),
            Child = new Panel { Children = { root, _overlay } },
        };
        Content = framed;

        Activated += (_, _) => Titlebar.Background = Ui.TitlebarActive;
        Deactivated += (_, _) => Titlebar.Background = Ui.TitlebarInactive;
        library.Changed += UpdateStatus;
        Closed += (_, _) =>
        {
            library.Changed -= UpdateStatus;
            SaveBounds();
            foreach (var view in _views.Values) (view as IDisposable)?.Dispose();
        };

        RestoreBounds();
        KeyDown += OnKeyDown;
        Navigate("library");
    }

    private Border BuildTitlebar()
    {
        var title = Ui.Label("SIR WORD-A-LOT", Ui.Ink);
        var left = Ui.Row(12, new Glyph("word") { Width = 16, Height = 16 }, title);
        left.Margin = new Thickness(16, 0);

        var buttons = Ui.Row(0,
            Ui.IconButton("minimize", "Minimize", () => WindowState = WindowState.Minimized),
            Ui.IconButton("maximize", "Maximize / restore", ToggleMaximize),
            Ui.IconButton("close", "Close", Close));

        var bar = new Border
        {
            Height = 43,
            Background = Ui.TitlebarActive,
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = Ui.Columns("*,Auto", left, buttons),
        };

        bar.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual visual &&
                visual.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        bar.DoubleTapped += (_, e) =>
        {
            if (e.Source is Visual visual &&
                visual.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;
            ToggleMaximize();
        };

        return bar;
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private Border BuildSidebar()
    {
        var panel = Ui.Stack(2);
        panel.Margin = new Thickness(0, 22, 0, 0);

        void Section(string caption)
        {
            var label = Ui.Label(caption);
            label.Margin = new Thickness(18, 16, 18, 6);
            panel.Children.Add(label);
        }

        void Add(NavItem item)
        {
            var button = Ui.Button(TitleCase(item.Title), () => Navigate(item.Id), "nav");
            button.Content = Ui.Row(11,
                new Glyph(item.Glyph) { Width = 14, Height = 14 },
                Ui.Text(TitleCase(item.Title), 12.5));
            _navButtons[item.Id] = button;
            panel.Children.Add(button);
        }

        Section("LIBRARY");
        foreach (var item in Items[..5]) Add(item);

        var rule = Ui.Rule();
        rule.Margin = new Thickness(18, 16, 18, 4);
        panel.Children.Add(rule);
        foreach (var item in Items[5..10]) Add(item);

        var rule2 = Ui.Rule();
        rule2.Margin = new Thickness(18, 16, 18, 4);
        panel.Children.Add(rule2);
        foreach (var item in Items[10..]) Add(item);

        return new Border
        {
            Width = 170,
            Background = Ui.Sidebar,
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = Ui.Scroll(panel),
        };
    }

    private static string TitleCase(string caption) =>
        caption.Length == 0 ? caption : char.ToUpperInvariant(caption[0]) + caption[1..].ToLowerInvariant();

    private Border BuildFooter()
    {
        var grip = new Border
        {
            Width = 19,
            Height = 19,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.BottomRightCorner),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "◢", FontSize = 10, Foreground = Ui.Muted,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
            },
        };
        grip.PointerPressed += (_, e) =>
        {
            if (WindowState == WindowState.Normal) BeginResizeDrag(WindowEdge.SouthEast, e);
        };

        var right = Ui.Row(14, _statusRight, grip);
        right.HorizontalAlignment = HorizontalAlignment.Right;

        return new Border
        {
            Height = 42,
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 0),
            Child = Ui.Columns("*,Auto", _statusLeft, right),
        };
    }

    public void Navigate(string id, object? argument = null)
    {
        var item = Items.FirstOrDefault(i => i.Id == id) ?? Items[0];

        if (!_views.TryGetValue(item.Id, out var view))
        {
            view = Build(item.Id);
            _views[item.Id] = view;
        }

        if (view is IReceivesArgument receiver && argument is not null) receiver.Receive(argument);

        Host.Content = view;
        CurrentViewId = item.Id;
        _statusLeft.Text = $"SWL / {item.Title}";

        foreach (var (buttonId, button) in _navButtons)
        {
            button.Classes.Remove("selected");
            if (buttonId == item.Id) button.Classes.Add("selected");
        }

        Motion.Enter(view, 8);
        UpdateStatus();
    }

    private Control Build(string id) => id switch
    {
        "sentences" => new SentencesView(_context),
        "lookup" => new LookupView(_context),
        "quiz" => new QuizView(_context),
        "decks" => new DecksView(_context),
        "stats" => new StatsView(_context),
        "console" => new ConsoleView(_context),
        "settings" => new SettingsView(_context),
        _ => new LibraryView(_context, id),
    };

    public void ShowToast(string message) => Toast.Show(_overlay, message);

    private void UpdateStatus()
    {
        var summary = _context.Stats.Summary(DateTime.UtcNow);
        var connectivity = _settings.LookupEnabled ? "ONLINE" : "LOOKUP OFF";
        _statusRight.Text = $"{summary.TotalWords} WORDS  ·  {summary.DueToday} DUE  ·  {connectivity}";
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
        {
            CommandPalette.Show(_overlay, _context, Items);
            e.Handled = true;
        }
    }

    private void RestoreBounds()
    {
        if (_settings.WindowBounds is not { } stored) return;
        var parts = stored.Split(',');
        if (parts.Length != 4) return;
        if (!double.TryParse(parts[2], out var width) || !double.TryParse(parts[3], out var height)) return;

        Width = Math.Max(MinWidth, width);
        Height = Math.Max(MinHeight, height);

        if (double.TryParse(parts[0], out var x) && double.TryParse(parts[1], out var y))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)x, (int)y);
        }
    }

    private void SaveBounds()
    {
        if (WindowState != WindowState.Normal) return;
        _settings.WindowBounds = $"{Position.X},{Position.Y},{Width},{Height}";
    }
}

/// <summary>Implemented by views that can be navigated to with an argument,
/// such as the Library opening a specific entry.</summary>
public interface IReceivesArgument
{
    void Receive(object argument);
}
```

`CommandPalette.Show` arrives in Task 24; until then, stub it in
`Shell/CommandPalette.cs` so the shell compiles:

```csharp
using Avalonia.Controls;

namespace SirWordALot.Shell;

public static partial class CommandPalette
{
    public static void Show(Panel host, AppContext context, IReadOnlyList<NavItem> nav) { }
}
```

- [ ] **Step 4: Run the checks to verify they pass**

The shell check constructs every view, so it only passes once Tasks 16–23 have
landed. Implement stub views first — one file each under `Views/`, each a
`UserControl` whose content is `Ui.Pad(Ui.Heading("…", "…", "…"))` — then run:

Run: `dotnet run --project tools/Checks`
Expected: every check prints `ok`, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add Shell/MainWindow.cs Shell/Toast.cs Shell/CommandPalette.cs Views tools/Checks/Program.cs
git commit -m "Add the frameless window shell with navigation and status footer"
```

---

### Task 16: Library and entry detail

**Files:**
- Create: `Views/LibraryView.cs`, `Views/EntryView.cs`
- Modify: `tools/Checks/Program.cs` (add `CheckLibraryView`)

**Interfaces:**
- Consumes: `AppContext`, `Search`, `SearchFilter`, `SearchHit`, `Library`, `Ui`, `Glyph`.
- Produces:
  - `LibraryView(AppContext context, string preset) : UserControl, IDisposable, IReceivesArgument`
    with `.Filter -> SearchFilter`, `.Hits -> IReadOnlyList<SearchHit>`,
    `.Select(long entryId)`, `.Refresh()`, `.SearchBox -> TextBox`, `.Detail -> ContentControl`
  - `EntryView(AppContext context, long entryId, Action refreshList) : UserControl`
    with `.Entry -> Entry`, `.Save()`, `.ConfirmDelete()`

`preset` is one of `library`, `due`, `starred`, `recent`, `archived` and seeds
the filter. Selecting a relation chip that is not yet a word offers
`+ add as new entry`.

- [ ] **Step 1: Write the failing check**

Append to `tools/Checks/Program.cs` and call it from `Main`:

```csharp
    private static void CheckLibraryView()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        var eminence = library.AddEntry("eminence");
        library.UpdateEntry(library.GetEntry(eminence)! with
        { PartOfSpeech = "noun", Definition = "A position of superiority." });
        library.AddRelation(eminence, RelationKind.Synonym, "renown");
        library.AddSentence("She rose to eminence.", eminence);
        library.AddEntry("laconic");

        var context = new AppContext(library, new Settings(db), new Search(db), new Stats(db),
            new StubLookup(), _ => { }, (_, _) => { });

        var view = new LibraryView(context, "library");
        Expect("library lists both", 2, view.Hits.Count);

        view.SearchBox.Text = "lacon";
        Expect("search narrows", 1, view.Hits.Count);
        Expect("search matches", "laconic", view.Hits[0].Entry.Headword);

        view.SearchBox.Text = "";
        view.Select(eminence);
        Expect("detail opens", true, view.Detail.Content is EntryView);

        var detail = (EntryView)view.Detail.Content!;
        Expect("detail entry", "eminence", detail.Entry.Headword);

        var starred = new LibraryView(context, "starred");
        Expect("starred preset empty", 0, starred.Hits.Count);

        library.SetStarred(eminence, true);
        starred.Refresh();
        Expect("starred preset updates", 1, starred.Hits.Count);

        view.Dispose();
        starred.Dispose();
    }
```

- [ ] **Step 2: Run the checks to verify they fail**

Run: `dotnet run --project tools/Checks`
Expected: FAIL — the stub `LibraryView` has no `Hits`.

- [ ] **Step 3: Write the implementation**

Create `Views/LibraryView.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class LibraryView : UserControl, IDisposable, IReceivesArgument
{
    private readonly AppContext _context;
    private readonly string _preset;
    private readonly StackPanel _list = Ui.Stack(0);
    private readonly TextBlock _count = Ui.Label("");
    private readonly ComboBox _sort = new()
    {
        ItemsSource = new[] { "Recent", "Alphabetic", "Due", "Mastery" },
        SelectedIndex = 0, Width = 118,
    };
    private readonly ComboBox _partOfSpeech = new() { Width = 128 };
    private readonly ComboBox _tag = new() { Width = 128 };
    private readonly ComboBox _mastery = new()
    {
        ItemsSource = new[] { "Any mastery", "New", "Learning", "Familiar", "Strong", "Mastered" },
        SelectedIndex = 0, Width = 128,
    };

    private long? _selected;

    public TextBox SearchBox { get; } = new() { Watermark = "Search words and sentences…" };
    public ContentControl Detail { get; } = new();
    public IReadOnlyList<SearchHit> Hits { get; private set; } = [];

    public SearchFilter Filter => new(
        Text: string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text,
        PartOfSpeech: _partOfSpeech.SelectedIndex > 0 ? (string?)_partOfSpeech.SelectedItem : null,
        Tag: _tag.SelectedIndex > 0 ? (string?)_tag.SelectedItem : null,
        Mastery: _mastery.SelectedIndex > 0 ? (MasteryBucket)(_mastery.SelectedIndex - 1) : null,
        Due: _preset == "due" ? DueState.DueNow : DueState.Any,
        IncludeArchived: _preset == "archived",
        StarredOnly: _preset == "starred",
        Sort: _preset switch
        {
            "due" => SortOrder.Due,
            "recent" => SortOrder.Recent,
            _ => (SortOrder)_sort.SelectedIndex,
        });

    public LibraryView(AppContext context, string preset)
    {
        _context = context;
        _preset = preset;

        var toolbar = Ui.Columns("*,Auto,Auto",
            SearchBox,
            _sort,
            Ui.Button("+ Word", NewWord, "primary"));
        toolbar.Margin = new Thickness(16, 14, 16, 10);
        Grid.SetColumnSpan(toolbar, 1);

        var filters = Ui.Row(8, Ui.Label("FILTERS"), _partOfSpeech, _tag, _mastery);
        filters.Margin = new Thickness(16, 0, 16, 12);

        var listColumn = Ui.Rows("Auto,Auto,*,40",
            toolbar, filters, Ui.Scroll(_list),
            new Border
            {
                BorderBrush = Ui.Line,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 0),
                Child = _count,
            });

        var listBorder = new Border
        {
            Width = 320,
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = listColumn,
        };

        Detail.Content = Placeholder();
        Content = Ui.Columns("320,*", listBorder, Detail);

        SearchBox.TextChanged += (_, _) => Refresh();
        _sort.SelectionChanged += (_, _) => Refresh();
        _partOfSpeech.SelectionChanged += (_, _) => Refresh();
        _tag.SelectionChanged += (_, _) => Refresh();
        _mastery.SelectionChanged += (_, _) => Refresh();

        context.Library.Changed += Refresh;
        Refresh();
    }

    public void Receive(object argument)
    {
        switch (argument)
        {
            case long entryId:
                Select(entryId);
                break;
            case string tag when _tag.ItemsSource is IEnumerable<string> tags:
                var index = tags.ToList().IndexOf(tag);
                if (index >= 0) _tag.SelectedIndex = index;
                break;
        }
    }

    public void Dispose() => _context.Library.Changed -= Refresh;

    /// <summary>Used by the shell's <c>/</c> shortcut.</summary>
    public void FocusSearch() => SearchBox.Focus();

    /// <summary>Used by the shell's <c>N</c> shortcut.</summary>
    public void BeginNewWord() => NewWord();

    public void Refresh()
    {
        ReloadChoices();

        Hits = _context.Search.Run(Filter, DateTime.UtcNow);
        _list.Children.Clear();
        _count.Text = $"{Hits.Count:00} WORDS  ·  {_preset.ToUpperInvariant()}";

        foreach (var hit in Hits)
        {
            var left = Ui.Stack(5,
                Ui.Row(8,
                    hit.Entry.Starred
                        ? new Glyph("star") { Width = 11, Height = 11 }
                        : new Border { Width = 11 },
                    Ui.Text(hit.Entry.Headword, 13)),
                Ui.Row(10,
                    Ui.Label(hit.Entry.PartOfSpeech?.ToUpperInvariant() ?? "—"),
                    Ui.MasteryBar(hit.MasteryPercent)));

            var row = Ui.Columns("*,Auto", left,
                Ui.Label(hit.DueUtc <= DateTime.UtcNow ? "DUE" : hit.DueUtc.ToString("dd MMM").ToUpperInvariant()));

            var button = Ui.Button("", () => Select(hit.Entry.Id));
            button.Content = row;
            button.Padding = new Thickness(16, 13);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            if (hit.Entry.Id == _selected) button.Classes.Add("selected");

            _list.Children.Add(button);
            _list.Children.Add(Ui.Rule(Ui.RowRule));
        }

        if (Hits.Count == 0)
            _list.Children.Add(Ui.Pad(Ui.Text("No words match this filter.", 13, Ui.Muted), 26));

        if (_selected is { } id && Hits.All(h => h.Entry.Id != id))
        {
            _selected = null;
            Detail.Content = Placeholder();
        }
    }

    private void ReloadChoices()
    {
        var parts = new List<string> { "Any part of speech" };
        parts.AddRange(_context.Library.AllEntries(includeArchived: true)
            .Select(e => e.PartOfSpeech)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order());

        if (_partOfSpeech.ItemsSource is not IEnumerable<string> existing || !existing.SequenceEqual(parts))
        {
            var selected = _partOfSpeech.SelectedItem as string;
            _partOfSpeech.ItemsSource = parts;
            _partOfSpeech.SelectedIndex = Math.Max(0, parts.IndexOf(selected ?? ""));
        }

        var tags = new List<string> { "Any tag" };
        tags.AddRange(_context.Library.Tags().Select(t => t.Name));

        if (_tag.ItemsSource is not IEnumerable<string> tagged || !tagged.SequenceEqual(tags))
        {
            var selected = _tag.SelectedItem as string;
            _tag.ItemsSource = tags;
            _tag.SelectedIndex = Math.Max(0, tags.IndexOf(selected ?? ""));
        }
    }

    public void Select(long entryId)
    {
        _selected = entryId;
        Detail.Content = new EntryView(_context, entryId, Refresh);
        Motion.Enter((Control)Detail.Content, 8);
        Refresh();
    }

    private void NewWord()
    {
        var name = new TextBox { Watermark = "A word or phrase" };
        var error = Ui.Text("", 12, Ui.Danger);

        var form = Ui.Stack(16,
            Ui.Heading("LIBRARY MANAGEMENT", "A new word.", "It will be due for review immediately."),
            name, error,
            Ui.Row(12,
                Ui.Button("Cancel", () => Detail.Content = Placeholder(), "outline"),
                Ui.Button("Add word", () =>
                {
                    var value = name.Text?.Trim() ?? "";
                    if (Library.Normalize(value).Length == 0) { error.Text = "Enter a word."; return; }
                    if (_context.Library.FindEntry(value) is { } existing)
                    {
                        _context.Toast("That word is already in the library.");
                        Select(existing.Id);
                        return;
                    }
                    var id = _context.Library.AddEntry(value);
                    _context.Toast("Word added.");
                    Select(id);
                }, "primary")));

        Detail.Content = Ui.Pad(form, 30);
        name.Focus();
    }

    private static Control Placeholder() => Ui.Pad(Ui.Stack(12,
        Ui.Label("NO SELECTION"),
        Ui.Display("Choose a word.", 26),
        Ui.Text("Or add one. Every word you keep is a word you will meet again.", 12, Ui.Muted)), 34);
}
```

Create `Views/EntryView.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class EntryView : UserControl
{
    private readonly AppContext _context;
    private readonly Action _refreshList;
    private readonly ContentControl _body = new();
    private readonly TextBox _definition = new()
    { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 74 };
    private readonly TextBox _notes = new()
    { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
    private readonly TextBox _headword = new();
    private readonly TextBox _pronunciation = new() { Watermark = "/ˈɛmɪnəns/" };
    private readonly TextBox _partOfSpeech = new() { Watermark = "noun" };

    public Entry Entry { get; private set; }

    public EntryView(AppContext context, long entryId, Action refreshList)
    {
        _context = context;
        _refreshList = refreshList;
        Entry = context.Library.GetEntry(entryId)
                ?? throw new InvalidOperationException($"No entry {entryId}.");

        Content = _body;
        Render();
    }

    private void Render()
    {
        Entry = _context.Library.GetEntry(Entry.Id)!;
        var scheduling = _context.Library.GetScheduling(Entry.Id);
        var reviews = _context.Library.ReviewsFor(Entry.Id);
        var percent = Mastery.Percent(scheduling.IntervalDays, reviews.Count(r => r.Correct), reviews.Count);

        _headword.Text = Entry.Headword;
        _pronunciation.Text = Entry.Pronunciation ?? "";
        _partOfSpeech.Text = Entry.PartOfSpeech ?? "";
        _definition.Text = Entry.Definition ?? "";
        _notes.Text = Entry.Notes ?? "";

        var due = scheduling.DueUtc <= DateTime.UtcNow
            ? "DUE NOW"
            : $"DUE IN {(scheduling.DueUtc.Date - DateTime.UtcNow.Date).Days} D";

        var header = Ui.Columns("*,Auto",
            Ui.Label($"ENTRY / {Ui.HexId(Entry.Id)}"),
            Ui.Label($"{due}  ·  MASTERY {percent}%"));

        var stack = Ui.Stack(18,
            header,
            Ui.Display(Entry.Headword),
            Ui.Row(12,
                Ui.Text(Entry.Pronunciation ?? "—", 12, Ui.Muted, mono: true),
                Ui.Text(Entry.PartOfSpeech ?? "—", 12, Ui.Muted, mono: true),
                Entry.AudioUrl is null
                    ? new Border()
                    : Ui.IconButton("sound", "Play the pronunciation", () =>
                    {
                        if (!Launch.Url(Entry.AudioUrl)) _context.Toast("Could not open the pronunciation.");
                    })),
            Ui.Rule());

        stack.Children.Add(Section("HEADWORD", _headword));
        stack.Children.Add(Ui.Columns("*,12,*",
            Section("PRONUNCIATION", _pronunciation), new Border(), Section("PART OF SPEECH", _partOfSpeech)));
        stack.Children.Add(Section("DEFINITION", _definition));

        foreach (var kind in new[] { RelationKind.Synonym, RelationKind.Antonym, RelationKind.Related })
            stack.Children.Add(RelationSection(kind));

        stack.Children.Add(SentenceSection());
        stack.Children.Add(TagSection());
        stack.Children.Add(Section("NOTES", _notes));

        stack.Children.Add(Ui.Rule());
        stack.Children.Add(Ui.Row(10,
            Ui.Button("Save", Save, "primary"),
            Ui.Button(Entry.Starred ? "Unstar" : "Star", () =>
            {
                _context.Library.SetStarred(Entry.Id, !Entry.Starred);
                Render();
            }, "outline"),
            Ui.Button(Entry.Archived ? "Unarchive" : "Archive", () =>
            {
                _context.Library.SetArchived(Entry.Id, !Entry.Archived);
                _context.Toast(Entry.Archived ? "Word restored." : "Word archived.");
                Render();
            }, "outline"),
            Ui.Button("Delete", ConfirmDelete, "outline"),
            Ui.Button("Quiz this word", () => _context.Navigate("quiz", Entry.Id), "outline")));

        _body.Content = Ui.Scroll(Ui.Pad(stack, 30));
    }

    private static Control Section(string caption, Control control) => Ui.Stack(7, Ui.Label(caption), control);

    private Control RelationSection(RelationKind kind)
    {
        var words = _context.Library.Relations(Entry.Id).Where(r => r.Kind == kind).ToList();
        var chips = Ui.Row(7);
        chips.Children.Add(new WrapPanelHost(words.Select(relation => Ui.Chip(
            relation.Word,
            click: () => OpenOrOffer(relation.Word),
            remove: () => { _context.Library.RemoveRelation(Entry.Id, kind, relation.Word); Render(); }))));

        var box = new TextBox { Watermark = $"Add a {kind.ToColumn()}…", Width = 200 };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            var word = box.Text?.Trim() ?? "";
            if (word.Length == 0) return;
            _context.Library.AddRelation(Entry.Id, kind, word);
            Render();
        };

        return Ui.Stack(8, Ui.Label(kind.ToColumn().ToUpperInvariant() + "S"), chips.Children[0] as Control ?? chips, box);
    }

    private void OpenOrOffer(string word)
    {
        if (_context.Library.FindEntry(word) is { } existing)
        {
            _context.Navigate("library", existing.Id);
            return;
        }

        var prompt = Ui.Stack(16,
            Ui.Heading("NOT YET IN THE LIBRARY", word, "Add it and the network grows by one."),
            Ui.Row(10,
                Ui.Button("Back", Render, "outline"),
                Ui.Button("+ Add as new entry", () =>
                {
                    var id = _context.Library.AddEntry(word, "lookup");
                    _context.Toast($"{word} added.");
                    _context.Navigate("library", id);
                }, "primary")));

        _body.Content = Ui.Pad(prompt, 30);
    }

    private Control SentenceSection()
    {
        var stack = Ui.Stack(9, Ui.Label("EXAMPLES"));

        foreach (var sentence in _context.Library.SentencesFor(Entry.Id))
        {
            var text = Ui.Text(sentence.Text, 13);
            text.TextWrapping = TextWrapping.Wrap;

            stack.Children.Add(new Border
            {
                BorderBrush = Ui.Line,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 9),
                Child = Ui.Columns("*,Auto",
                    Ui.Stack(5, text, Ui.Label(sentence.Source?.ToUpperInvariant() ?? "NO SOURCE")),
                    Ui.Row(0,
                        Ui.IconButton("check", "Set the cloze blank from this sentence", () =>
                        {
                            if (Cloze.Find(sentence.Text, Entry.Headword) is not { } span)
                            {
                                _context.Toast("This sentence does not contain the word.");
                                return;
                            }
                            _context.Library.UpdateSentence(sentence with
                            { ClozeStart = span.Start, ClozeLength = span.Length });
                            _context.Toast("Cloze blank set.");
                            Render();
                        }),
                        Ui.IconButton("close", "Delete this sentence", () =>
                        {
                            _context.Library.DeleteSentence(sentence.Id);
                            Render();
                        }))),
            });
        }

        var box = new TextBox { Watermark = "Add an example sentence…", AcceptsReturn = false };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            var text = box.Text?.Trim() ?? "";
            if (text.Length == 0) return;
            _context.Library.AddSentence(text, Entry.Id);
            Render();
        };
        stack.Children.Add(box);

        return stack;
    }

    private Control TagSection()
    {
        var chips = _context.Library.TagsFor(Entry.Id).Select(tag => Ui.Chip(
            tag, remove: () => { _context.Library.UntagEntry(Entry.Id, tag); Render(); }));

        var box = new TextBox { Watermark = "Add a tag…", Width = 200 };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            var tag = box.Text?.Trim() ?? "";
            if (tag.Length == 0) return;
            _context.Library.TagEntry(Entry.Id, tag);
            Render();
        };

        return Ui.Stack(8, Ui.Label("TAGS"), new WrapPanelHost(chips), box);
    }

    public void Save()
    {
        var headword = _headword.Text?.Trim() ?? "";
        if (Library.Normalize(headword).Length == 0)
        {
            _context.Toast("A word needs a headword.");
            return;
        }

        var clash = _context.Library.FindEntry(headword);
        if (clash is not null && clash.Id != Entry.Id)
        {
            _context.Toast("Another entry already uses that headword.");
            return;
        }

        _context.Library.UpdateEntry(Entry with
        {
            Headword = headword,
            Pronunciation = Blank(_pronunciation.Text),
            PartOfSpeech = Blank(_partOfSpeech.Text),
            Definition = Blank(_definition.Text),
            Notes = Blank(_notes.Text),
        });

        _context.Toast("Saved.");
        _refreshList();
        Render();

        static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public void ConfirmDelete()
    {
        var confirm = Ui.Stack(18,
            Ui.Heading("LIBRARY MANAGEMENT", "Remove this word?",
                "Its sentences, relations and review history go with it. This cannot be undone."),
            Ui.Display(Entry.Headword, 24),
            Ui.Row(12,
                Ui.Button("Keep it", Render, "outline"),
                Ui.Button("Delete", () =>
                {
                    _context.Library.DeleteEntry(Entry.Id);
                    _context.Toast("Word deleted.");
                    _refreshList();
                }, "primary")));

        _body.Content = Ui.Pad(confirm, 30);
    }
}

/// <summary>A wrapping row of chips. Avalonia has no WrapPanel in the core package,
/// so this lays children out left to right and wraps at the available width.</summary>
public sealed class WrapPanelHost : Panel
{
    public WrapPanelHost(IEnumerable<Control> children)
    {
        foreach (var child in children) Children.Add(child);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0, y = 0, lineHeight = 0;
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            if (x + child.DesiredSize.Width > availableSize.Width && x > 0)
            {
                x = 0; y += lineHeight + 6; lineHeight = 0;
            }
            x += child.DesiredSize.Width + 6;
            lineHeight = Math.Max(lineHeight, child.DesiredSize.Height);
        }
        return new Size(availableSize.Width, y + lineHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;
        foreach (var child in Children)
        {
            if (x + child.DesiredSize.Width > finalSize.Width && x > 0)
            {
                x = 0; y += lineHeight + 6; lineHeight = 0;
            }
            child.Arrange(new Rect(x, y, child.DesiredSize.Width, child.DesiredSize.Height));
            x += child.DesiredSize.Width + 6;
            lineHeight = Math.Max(lineHeight, child.DesiredSize.Height);
        }
        return finalSize;
    }
}
```

Simplify `RelationSection` to use `WrapPanelHost` directly rather than the
`Ui.Row` indirection shown above:

```csharp
    private Control RelationSection(RelationKind kind)
    {
        var words = _context.Library.Relations(Entry.Id).Where(r => r.Kind == kind).ToList();

        var chips = new WrapPanelHost(words.Select(relation => Ui.Chip(
            relation.Word,
            click: () => OpenOrOffer(relation.Word),
            remove: () => { _context.Library.RemoveRelation(Entry.Id, kind, relation.Word); Render(); })));

        var box = new TextBox { Watermark = $"Add a {kind.ToColumn()}…", Width = 200 };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;
            var word = box.Text?.Trim() ?? "";
            if (word.Length == 0) return;
            _context.Library.AddRelation(Entry.Id, kind, word);
            Render();
        };

        return Ui.Stack(8, Ui.Label(kind.ToColumn().ToUpperInvariant() + "S"), chips, box);
    }
```

Use this second form.

- [ ] **Step 4: Run the checks to verify they pass**

Run: `dotnet run --project tools/Checks`
Expected: every check prints `ok`, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add Views/LibraryView.cs Views/EntryView.cs tools/Checks/Program.cs
git commit -m "Add the library list and entry detail views"
```

---

### Task 17: The sentence bank

**Files:**
- Create: `Views/SentencesView.cs`
- Modify: `tools/Checks/Program.cs` (add `CheckSentencesView`)

**Interfaces:**
- Consumes: `AppContext`, `Library`, `Cloze`, `Ui`.
- Produces: `SentencesView(AppContext context) : UserControl, IDisposable` with
  `.Rows -> IReadOnlyList<Sentence>`, `.Refresh()`, `.SearchBox -> TextBox`,
  `.Add(string text, string? source) -> long`,
  `.SuggestLinks(string text) -> IReadOnlyList<Entry>`

`SuggestLinks` scans a pasted sentence for headwords already in the library
using `Cloze.ContainsWord`, so an inflected form still matches.

- [ ] **Step 1: Write the failing check**

Append to `tools/Checks/Program.cs` and call it from `Main`:

```csharp
    private static void CheckSentencesView()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        var obfuscate = library.AddEntry("obfuscate");
        library.AddEntry("laconic");
        library.AddSentence("A loose one.");

        var context = new AppContext(library, new Settings(db), new Search(db), new Stats(db),
            new StubLookup(), _ => { }, (_, _) => { });

        var view = new SentencesView(context);
        Expect("lists the bank", 1, view.Rows.Count);

        var suggestions = view.SuggestLinks("He obfuscates the issue entirely.");
        Expect("suggests by inflection", 1, suggestions.Count);
        Expect("suggests the right word", obfuscate, suggestions[0].Id);

        view.Add("He obfuscates the issue entirely.", "Notebook");
        Expect("adds to the bank", 2, view.Rows.Count);

        view.SearchBox.Text = "obfuscates";
        Expect("search narrows", 1, view.Rows.Count);

        view.Dispose();
    }
```

- [ ] **Step 2: Run the checks to verify they fail**

Run: `dotnet run --project tools/Checks`
Expected: FAIL — the stub `SentencesView` has no `Rows`.

- [ ] **Step 3: Write the implementation**

Create `Views/SentencesView.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class SentencesView : UserControl, IDisposable
{
    private readonly AppContext _context;
    private readonly StackPanel _list = Ui.Stack(0);
    private readonly TextBlock _count = Ui.Label("");
    private readonly ComboBox _scope = new()
    {
        ItemsSource = new[] { "All sentences", "Loose only", "Linked only" },
        SelectedIndex = 0, Width = 140,
    };
    private readonly ContentControl _panel = new();

    public TextBox SearchBox { get; } = new() { Watermark = "Search sentences…" };
    public IReadOnlyList<Sentence> Rows { get; private set; } = [];

    public SentencesView(AppContext context)
    {
        _context = context;

        var toolbar = Ui.Columns("*,Auto,Auto", SearchBox, _scope, Ui.Button("+ Sentence", ShowAddForm, "primary"));
        toolbar.Margin = new Thickness(26, 22, 26, 14);

        var header = Ui.Columns("*,190,120", Ui.Label("SENTENCE"), Ui.Label("WORD"), Ui.Label("SOURCE"));
        header.Margin = new Thickness(26, 0, 26, 10);

        var footer = new Border
        {
            BorderBrush = Ui.Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(26, 0),
            Height = 40,
            Child = _count,
        };

        _panel.Content = Ui.Rows("Auto,Auto,Auto,*,40",
            toolbar, header, Ui.Rule(), Ui.Scroll(_list), footer);

        Content = _panel;

        SearchBox.TextChanged += (_, _) => Refresh();
        _scope.SelectionChanged += (_, _) => Refresh();
        context.Library.Changed += Refresh;
        Refresh();
    }

    public void Dispose() => _context.Library.Changed -= Refresh;

    public IReadOnlyList<Entry> SuggestLinks(string text) =>
        _context.Library.AllEntries()
            .Where(entry => Cloze.ContainsWord(text, entry.Headword))
            .ToList();

    public long Add(string text, string? source)
    {
        var id = _context.Library.AddSentence(text, null, source);
        var suggestions = SuggestLinks(text);
        if (suggestions.Count == 1) _context.Library.LinkSentence(id, suggestions[0].Id);
        return id;
    }

    public void Refresh()
    {
        var query = SearchBox.Text?.Trim() ?? "";

        Rows = _context.Library.AllSentences()
            .Where(s => _scope.SelectedIndex switch
            {
                1 => s.EntryId is null,
                2 => s.EntryId is not null,
                _ => true,
            })
            .Where(s => query.Length == 0 ||
                        s.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (s.Source ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _list.Children.Clear();
        _count.Text = $"{Rows.Count:00} SENTENCES  ·  {Rows.Count(r => r.EntryId is null):00} LOOSE";

        foreach (var sentence in Rows)
        {
            var word = sentence.EntryId is { } entryId
                ? _context.Library.GetEntry(entryId)?.Headword ?? "—"
                : "—";

            var text = Ui.Text(sentence.Text, 13);
            text.TextWrapping = TextWrapping.Wrap;

            var actions = Ui.Row(0,
                Ui.IconButton(sentence.EntryId is null ? "arrow" : "back",
                    sentence.EntryId is null ? "Link to a word" : "Unlink from its word",
                    () => ToggleLink(sentence)),
                Ui.IconButton("close", "Delete this sentence", () =>
                {
                    _context.Library.DeleteSentence(sentence.Id);
                    _context.Toast("Sentence deleted.");
                }));

            var row = Ui.Columns("*,190,120",
                text,
                Ui.Row(8, Ui.Text(word, 12, word == "—" ? Ui.Muted : Ui.Ink), actions),
                Ui.Label(sentence.Source?.ToUpperInvariant() ?? "—"));
            row.Margin = new Thickness(26, 15);

            _list.Children.Add(row);
            _list.Children.Add(Ui.Rule(Ui.RowRule));
        }

        if (Rows.Count == 0)
            _list.Children.Add(Ui.Pad(Ui.Text("No sentences yet. Paste one you met in the wild.", 13, Ui.Muted), 30));
    }

    private void ToggleLink(Sentence sentence)
    {
        if (sentence.EntryId is not null)
        {
            _context.Library.LinkSentence(sentence.Id, null);
            _context.Toast("Sentence returned to the bank.");
            return;
        }

        var suggestions = SuggestLinks(sentence.Text);
        var options = suggestions.Count > 0 ? suggestions : _context.Library.AllEntries();

        var picker = new ComboBox
        {
            ItemsSource = options.Select(e => e.Headword).ToList(),
            SelectedIndex = options.Count > 0 ? 0 : -1,
            Width = 240,
        };

        var form = Ui.Stack(16,
            Ui.Heading("SENTENCE BANK", "Link this sentence.",
                suggestions.Count > 0
                    ? "These words appear in it."
                    : "No word in the library appears in it; choose one anyway."),
            Ui.Text(sentence.Text, 14),
            picker,
            Ui.Row(12,
                Ui.Button("Cancel", RestoreList, "outline"),
                Ui.Button("Link", () =>
                {
                    if (picker.SelectedIndex < 0) { RestoreList(); return; }
                    _context.Library.LinkSentence(sentence.Id, options[picker.SelectedIndex].Id);
                    _context.Toast("Sentence linked.");
                    RestoreList();
                }, "primary")));

        ShowPanel(form);
    }

    private void ShowAddForm()
    {
        var text = new TextBox
        {
            Watermark = "Paste the sentence you found…",
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 96,
        };
        var source = new TextBox { Watermark = "Where it came from (optional)" };
        var error = Ui.Text("", 12, Ui.Danger);

        var form = Ui.Stack(16,
            Ui.Heading("SENTENCE BANK", "A sentence worth keeping.",
                "If a word you already own appears in it, it will be linked automatically."),
            text, source, error,
            Ui.Row(12,
                Ui.Button("Cancel", RestoreList, "outline"),
                Ui.Button("Add sentence", () =>
                {
                    var value = text.Text?.Trim() ?? "";
                    if (value.Length == 0) { error.Text = "Enter a sentence."; return; }
                    Add(value, string.IsNullOrWhiteSpace(source.Text) ? null : source.Text!.Trim());
                    _context.Toast("Sentence added.");
                    RestoreList();
                }, "primary")));

        ShowPanel(form);
        text.Focus();
    }

    private Control? _listContent;

    private void ShowPanel(Control content)
    {
        _listContent ??= (Control)_panel.Content!;
        _panel.Content = Ui.Pad(content, 34);
    }

    private void RestoreList()
    {
        if (_listContent is not null) _panel.Content = _listContent;
        Refresh();
    }
}
```

- [ ] **Step 4: Run the checks to verify they pass**

Run: `dotnet run --project tools/Checks`
Expected: every check prints `ok`, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add Views/SentencesView.cs tools/Checks/Program.cs
git commit -m "Add the sentence bank with inflection-aware linking"
```

---

### Task 18: The lookup view

**Files:**
- Create: `Views/LookupView.cs`
- Modify: `tools/Checks/Program.cs` (add `CheckLookupView`)

**Interfaces:**
- Consumes: `AppContext`, `ILookup`, `LookupResult`, `Library`.
- Produces: `LookupView(AppContext context) : UserControl, IReceivesArgument` with
  `.QueryBox -> TextBox`, `.Result -> LookupResult?`,
  `.RunAsync(string word) -> Task`, `.SelectedWords -> IReadOnlyList<(RelationKind, string)>`,
  `.SelectedSenses -> IReadOnlyList<LookupSense>`, `.Commit() -> long`

`Commit` creates or merges the entry from exactly the ticked chips and senses,
and returns the entry id.

- [ ] **Step 1: Write the failing check**

Append to `tools/Checks/Program.cs` and call it from `Main`:

```csharp
    private static void CheckLookupView()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        var context = new AppContext(library, new Settings(db), new Search(db), new Stats(db),
            new FakeLookup(), _ => { }, (_, _) => { });

        var view = new LookupView(context);
        view.RunAsync("eminence").GetAwaiter().GetResult();

        Expect("result arrives", true, view.Result is not null);
        Expect("senses listed", 1, view.Result!.Senses.Count);
        Expect("everything ticked by default", 2, view.SelectedWords.Count);

        var id = view.Commit();
        Expect("entry created", "eminence", library.GetEntry(id)!.Headword);
        Expect("definition taken", "A position of superiority.", library.GetEntry(id)!.Definition);
        Expect("relations taken", 2, library.Relations(id).Count);
        Expect("example taken", 1, library.SentencesFor(id).Count);

        var offlineContext = context with { Lookup = new StubLookup() };
        var offline = new LookupView(offlineContext);
        offline.RunAsync("eminence").GetAwaiter().GetResult();
        Expect("offline handled", false, offline.Result!.Online);
    }

    private sealed class FakeLookup : ILookup
    {
        public Task<LookupResult> LookupAsync(string word, CancellationToken ct) => Task.FromResult(
            new LookupResult(word, "/ˈɛmɪnəns/", "https://example.invalid/e.mp3",
                [new LookupSense("noun", "A position of superiority.", ["She rose to eminence."])],
                new Dictionary<RelationKind, IReadOnlyList<string>>
                {
                    [RelationKind.Synonym] = ["prominence"],
                    [RelationKind.Antonym] = ["obscurity"],
                    [RelationKind.Related] = [],
                    [RelationKind.Rhyme] = [],
                },
                Online: true, Message: null));
    }
```

- [ ] **Step 2: Run the checks to verify they fail**

Run: `dotnet run --project tools/Checks`
Expected: FAIL — the stub `LookupView` has no `RunAsync`.

- [ ] **Step 3: Write the implementation**

Create `Views/LookupView.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class LookupView : UserControl, IReceivesArgument
{
    private readonly AppContext _context;
    private readonly ContentControl _body = new();
    private readonly Dictionary<(RelationKind Kind, string Word), CheckBox> _wordBoxes = [];
    private readonly Dictionary<LookupSense, CheckBox> _senseBoxes = [];
    private CancellationTokenSource? _inFlight;

    public TextBox QueryBox { get; } = new() { Watermark = "A word to look up…" };
    public LookupResult? Result { get; private set; }

    public IReadOnlyList<(RelationKind Kind, string Word)> SelectedWords =>
        _wordBoxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();

    public IReadOnlyList<LookupSense> SelectedSenses =>
        _senseBoxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();

    public LookupView(AppContext context)
    {
        _context = context;

        var search = Ui.Columns("*,Auto", QueryBox,
            Ui.Button("Look up", () => _ = RunAsync(QueryBox.Text ?? ""), "primary"));
        search.Margin = new Thickness(0, 0, 0, 20);

        QueryBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) _ = RunAsync(QueryBox.Text ?? "");
        };

        _body.Content = Idle();
        Content = Ui.Pad(Ui.Rows("Auto,*", search, Ui.Scroll(_body)), 30);
    }

    public void Receive(object argument)
    {
        if (argument is string word) { QueryBox.Text = word; _ = RunAsync(word); }
    }

    public async Task RunAsync(string word)
    {
        var term = word.Trim();
        if (term.Length == 0) return;

        if (!_context.Settings.LookupEnabled)
        {
            Result = Lookup.Offline(term, "Lookup is switched off in Settings.");
            Render();
            return;
        }

        _inFlight?.Cancel();
        _inFlight = new CancellationTokenSource();
        _body.Content = Ui.Pad(Ui.Label("CONSULTING THE RECORD…"), 20);

        try
        {
            Result = await _context.Lookup.LookupAsync(term, _inFlight.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess()) Render();
        else Dispatcher.UIThread.Post(Render);
    }

    private void Render()
    {
        _wordBoxes.Clear();
        _senseBoxes.Clear();

        if (Result is not { } result) { _body.Content = Idle(); return; }

        if (!result.Online)
        {
            _body.Content = Ui.Stack(16,
                Ui.Label("LOOKUP UNAVAILABLE  ·  OFFLINE"),
                Ui.Display(result.Word, 26),
                Ui.Text(result.Message ?? "No connection.", 13, Ui.Muted),
                Ui.Row(12, Ui.Button("Add by hand", () =>
                {
                    var id = _context.Library.AddEntry(result.Word);
                    _context.Toast("Word added. Fill it in yourself.");
                    _context.Navigate("library", id);
                }, "primary")));
            return;
        }

        var existing = _context.Library.FindEntry(result.Word);

        var stack = Ui.Stack(20,
            Ui.Label($"LOOKUP / {(existing is null ? "NEW" : "ALREADY IN THE LIBRARY")}"),
            Ui.Display(result.Word),
            Ui.Row(12,
                Ui.Text(result.Pronunciation ?? "—", 12, Ui.Muted, mono: true),
                result.AudioUrl is null
                    ? Ui.Text("", 12)
                    : Ui.Button("▸ Pronounce", () =>
                    {
                        if (!Shell.Launch.Url(result.AudioUrl))
                            _context.Toast("Could not open the pronunciation.");
                    }, "outline")),
            Ui.Rule());

        foreach (var kind in new[] { RelationKind.Synonym, RelationKind.Antonym, RelationKind.Related, RelationKind.Rhyme })
        {
            var words = result.Relations.GetValueOrDefault(kind) ?? [];
            if (words.Count == 0) continue;

            var boxes = words.Select(word =>
            {
                var box = new CheckBox { Content = word, IsChecked = kind is RelationKind.Synonym or RelationKind.Antonym };
                _wordBoxes[(kind, word)] = box;
                return (Control)box;
            });

            stack.Children.Add(Ui.Stack(8,
                Ui.Label(kind.ToColumn().ToUpperInvariant() + "S"),
                new WrapPanelHost(boxes)));
        }

        if (result.Senses.Count > 0)
        {
            var senses = Ui.Stack(12, Ui.Label("SENSES"));
            foreach (var sense in result.Senses)
            {
                var box = new CheckBox { IsChecked = _senseBoxes.Count == 0 };
                _senseBoxes[sense] = box;

                var definition = Ui.Text(sense.Definition, 13);
                definition.TextWrapping = TextWrapping.Wrap;

                var detail = Ui.Stack(6, Ui.Label(sense.PartOfSpeech.ToUpperInvariant()), definition);
                foreach (var example in sense.Examples)
                {
                    var text = Ui.Text("“" + example + "”", 12.5, Ui.Muted);
                    text.FontFamily = Ui.Serif;
                    text.TextWrapping = TextWrapping.Wrap;
                    detail.Children.Add(text);
                }

                senses.Children.Add(new Border
                {
                    BorderBrush = Ui.Line,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(0, 10),
                    Child = Ui.Columns("Auto,*", box, detail),
                });
            }
            stack.Children.Add(senses);
        }

        stack.Children.Add(Ui.Rule());
        stack.Children.Add(Ui.Row(12,
            Ui.Button(existing is null ? "Add to library" : "Merge into existing", () =>
            {
                var id = Commit();
                _context.Toast(existing is null ? "Word added." : "Word merged.");
                _context.Navigate("library", id);
            }, "primary"),
            Ui.Button("Tick everything", () =>
            {
                foreach (var box in _wordBoxes.Values) box.IsChecked = true;
                foreach (var box in _senseBoxes.Values) box.IsChecked = true;
            }, "outline"),
            Ui.Button("Tick nothing", () =>
            {
                foreach (var box in _wordBoxes.Values) box.IsChecked = false;
                foreach (var box in _senseBoxes.Values) box.IsChecked = false;
            }, "outline")));

        _body.Content = stack;
        Motion.Enter(stack, 8);
    }

    public long Commit()
    {
        if (Result is not { Online: true } result)
            throw new InvalidOperationException("There is nothing to commit.");

        var id = _context.Library.AddEntry(result.Word, "lookup");
        var entry = _context.Library.GetEntry(id)!;
        var senses = SelectedSenses;

        _context.Library.UpdateEntry(entry with
        {
            Pronunciation = entry.Pronunciation ?? result.Pronunciation,
            AudioUrl = entry.AudioUrl ?? result.AudioUrl,
            PartOfSpeech = entry.PartOfSpeech ?? senses.FirstOrDefault()?.PartOfSpeech,
            Definition = string.IsNullOrWhiteSpace(entry.Definition)
                ? string.Join("  ·  ", senses.Select(s => s.Definition))
                : entry.Definition,
        });

        foreach (var (kind, word) in SelectedWords)
            _context.Library.AddRelation(id, kind, word, "lookup");

        var known = _context.Library.SentencesFor(id).Select(s => s.Text).ToHashSet();
        foreach (var example in senses.SelectMany(s => s.Examples))
            if (known.Add(example)) _context.Library.AddSentence(example, id, "dictionaryapi.dev");

        return id;
    }

    private Control Idle() => Ui.Stack(12,
        Ui.Label("LOOKUP"),
        Ui.Display("Find a word.", 26),
        Ui.Text(_context.Settings.LookupEnabled
            ? "Synonyms and antonyms come from Datamuse; senses and examples from dictionaryapi.dev. Nothing else leaves this machine."
            : "Lookup is switched off in Settings. Words can still be added by hand.", 12, Ui.Muted));
}
```

- [ ] **Step 4: Run the checks to verify they pass**

Run: `dotnet run --project tools/Checks`
Expected: every check prints `ok`, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add Views/LookupView.cs tools/Checks/Program.cs
git commit -m "Add the lookup view with per-item selection and offline fallback"
```

---

### Task 19: The quiz view

**Files:**
- Create: `Views/QuizView.cs`
- Modify: `tools/Checks/Program.cs` (add `CheckQuizView`)

**Interfaces:**
- Consumes: `AppContext`, `ReviewSession`, `ReviewScope`, `SessionOptions`, `Card`, `Cloze`.
- Produces: `QuizView(AppContext context) : UserControl, IReceivesArgument` with
  `.Session -> ReviewSession?`, `.Start(ReviewScope scope, QuizMode? mode)`,
  `.Reveal()`, `.Grade(Grade grade)`, `.IsRevealed -> bool`,
  `.AnswerBox -> TextBox`, `.SubmitTyped()`

`Grade` is disabled until the card is revealed, so a self-graded card cannot be
answered blind. Typed and cloze cards auto-grade through `SubmitTyped`:
exact → Good, edit distance within tolerance → Hard, otherwise Again.

- [ ] **Step 1: Write the failing check**

Append to `tools/Checks/Program.cs` and call it from `Main`:

```csharp
    private static void CheckQuizView()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        foreach (var word in new[] { "eminence", "laconic", "ersatz", "obdurate", "pellucid" })
        {
            var id = library.AddEntry(word);
            library.UpdateEntry(library.GetEntry(id)! with
            { PartOfSpeech = "adjective", Definition = $"Meaning of {word}." });
        }

        var context = new AppContext(library, new Settings(db), new Search(db), new Stats(db),
            new StubLookup(), _ => { }, (_, _) => { });

        var view = new QuizView(context);
        view.Start(new ReviewScope(ScopeKind.All), QuizMode.Flip);

        Expect("session starts", true, view.Session is not null);
        Expect("starts hidden", false, view.IsRevealed);

        var first = view.Session!.Current!.Entry.Id;
        view.Grade(Grade.Good);
        Expect("blind grading ignored", 0, library.ReviewsFor(first).Count);

        view.Reveal();
        Expect("reveals", true, view.IsRevealed);
        view.Grade(Grade.Good);
        Expect("graded once revealed", 1, library.ReviewsFor(first).Count);
        Expect("advances", false, view.IsRevealed);

        var typed = new QuizView(context);
        typed.Start(new ReviewScope(ScopeKind.All), QuizMode.Typed);
        var target = typed.Session!.Current!.Entry;
        typed.AnswerBox.Text = target.Headword;
        typed.SubmitTyped();
        Expect("exact answer grades Good", Grade.Good, library.ReviewsFor(target.Id)[^1].Grade);

        var nearly = new QuizView(context);
        nearly.Start(new ReviewScope(ScopeKind.All), QuizMode.Typed);
        var second = nearly.Session!.Current!.Entry;
        nearly.AnswerBox.Text = second.Headword[..^1] + "x";
        nearly.SubmitTyped();
        Expect("near miss grades Hard", Grade.Hard, library.ReviewsFor(second.Id)[^1].Grade);
    }
```

- [ ] **Step 2: Run the checks to verify they fail**

Run: `dotnet run --project tools/Checks`
Expected: FAIL — the stub `QuizView` has no `Start`.

- [ ] **Step 3: Write the implementation**

Create `Views/QuizView.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Diagnostics;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class QuizView : UserControl, IReceivesArgument
{
    private readonly AppContext _context;
    private readonly ContentControl _body = new();
    private readonly Stopwatch _clock = new();

    public ReviewSession? Session { get; private set; }
    public bool IsRevealed { get; private set; }
    public TextBox AnswerBox { get; private set; } = new();

    public QuizView(AppContext context)
    {
        _context = context;
        Content = _body;
        Focusable = true;
        KeyDown += OnKeyDown;
        ShowSetup();
    }

    public void Receive(object argument)
    {
        switch (argument)
        {
            case ReviewScope scope:
                Start(scope, null);
                break;
            case long entryId:
                Start(new ReviewScope(ScopeKind.Single, EntryId: entryId), null);
                break;
        }
    }

    private void ShowSetup()
    {
        Session = null;
        IsRevealed = false;

        var modes = new ComboBox
        {
            ItemsSource = new[] { "Mixed", "Flip", "Multiple choice", "Cloze", "Typed" },
            SelectedIndex = 0, Width = 190,
        };

        var decks = _context.Library.Decks();
        var tags = _context.Library.Tags();
        var scopes = new List<string> { "Due queue", "Starred", "Everything", "Free practice" };
        scopes.AddRange(decks.Select(d => $"Deck: {d.Name}"));
        scopes.AddRange(tags.Where(t => t.Name != "__focus").Select(t => $"Tag: {t.Name}"));

        var scopeBox = new ComboBox { ItemsSource = scopes, SelectedIndex = 0, Width = 190 };

        var summary = _context.Stats.Summary(DateTime.UtcNow);

        var tiles = Ui.Columns("*,14,*,14,*,14,*",
            Ui.StatTile("01", $"{summary.DueToday}", "DUE TODAY"),
            new Border(),
            Ui.StatTile("02", $"{summary.ReviewsToday}/{_context.Settings.DailyGoal}", "TODAY'S GOAL"),
            new Border(),
            Ui.StatTile("03", $"{summary.StreakDays}", "DAY STREAK"),
            new Border(),
            Ui.StatTile("04", $"{summary.Retention:P0}", "RETENTION"));

        _body.Content = Ui.Pad(Ui.Stack(24,
            Ui.Heading("REFINEMENT PROTOCOL", "Time to remember.",
                "Grade honestly. The schedule only works if you do."),
            tiles,
            Ui.Rule(),
            Ui.Row(16,
                Ui.Stack(7, Ui.Label("MODE"), modes),
                Ui.Stack(7, Ui.Label("SCOPE"), scopeBox)),
            Ui.Row(12,
                Ui.Button("Begin session", () =>
                {
                    var mode = modes.SelectedIndex switch
                    {
                        1 => QuizMode.Flip,
                        2 => QuizMode.Choice,
                        3 => QuizMode.Cloze,
                        4 => QuizMode.Typed,
                        _ => (QuizMode?)null,
                    };

                    var index = scopeBox.SelectedIndex;
                    var scope = index switch
                    {
                        0 => new ReviewScope(ScopeKind.Due),
                        1 => new ReviewScope(ScopeKind.Starred),
                        2 => new ReviewScope(ScopeKind.All),
                        3 => new ReviewScope(ScopeKind.FreePractice),
                        _ when index - 4 < decks.Count => new ReviewScope(ScopeKind.Deck, DeckId: decks[index - 4].Id),
                        _ => new ReviewScope(ScopeKind.Tag, Tag: tags[index - 4 - decks.Count].Name),
                    };

                    Start(scope, mode);
                }, "primary")),
            Ui.Text("Space flips · 1–4 grade Again, Hard, Good, Easy · Esc ends the session.", 12, Ui.Muted)), 34);
    }

    public void Start(ReviewScope scope, QuizMode? mode)
    {
        Session = new ReviewSession(_context.Library, scope,
            new SessionOptions(_context.Settings.NewPerDay, mode), DateTime.UtcNow);

        if (Session.IsFinished)
        {
            _body.Content = Ui.Pad(Ui.Stack(16,
                Ui.Heading("NOTHING DUE", "You are up to date.",
                    "Add words, or run a free practice session over everything you own."),
                Ui.Row(12, Ui.Button("Back", ShowSetup, "outline"))), 34);
            Session = null;
            return;
        }

        RenderCard();
    }

    private void RenderCard()
    {
        if (Session?.Current is not { } card) { RenderSummary(); return; }

        IsRevealed = false;
        _clock.Restart();
        Focus();

        var header = Ui.Columns("*,Auto",
            Ui.Label($"{Session.Position:00} / {Session.Total:00}  ·  {card.Mode.ToColumn().ToUpperInvariant()}"),
            Ui.Button("End session", RenderSummary, "outline"));

        var face = card.Mode switch
        {
            QuizMode.Choice => ChoiceFace(card),
            QuizMode.Cloze => TypedFace(card, card.Prompt, "Which word fills the blank?"),
            QuizMode.Typed => TypedFace(card, card.Prompt, "Which word is this?"),
            _ => FlipFace(card),
        };

        _body.Content = Ui.Pad(Ui.Rows("Auto,*", header, face), 34);
        Motion.Enter(face, 10);
    }

    private Control FlipFace(Card card) => Ui.Stack(26,
        Ui.Display(card.Entry.Headword, 44),
        Ui.Row(12, Ui.Button("Reveal  ·  Space", Reveal, "primary")));

    private Control ChoiceFace(Card card)
    {
        var prompt = Ui.Display(card.Prompt, 24);
        var options = Ui.Stack(8);

        for (var i = 0; i < card.Choices.Count; i++)
        {
            var index = i;
            var button = Ui.Button(card.Choices[i], () => AnswerChoice(card, index), "outline");
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(16, 12);
            options.Children.Add(button);
        }

        return Ui.Stack(24, Ui.Label("SELECT THE WORD"), prompt, options);
    }

    private Control TypedFace(Card card, string prompt, string caption)
    {
        AnswerBox = new TextBox { Watermark = "Your answer…" };
        AnswerBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) SubmitTyped(); };

        var text = Ui.Display(prompt, 26);

        return Ui.Stack(24,
            Ui.Label(caption.ToUpperInvariant()),
            text,
            AnswerBox,
            Ui.Row(12,
                Ui.Button("Submit", SubmitTyped, "primary"),
                Ui.Button("I don't know", () => Commit(card, Grade.Again), "outline")));
    }

    private void AnswerChoice(Card card, int index)
    {
        var correct = index == card.CorrectChoice;
        RevealAnswer(card, correct
            ? "Correct."
            : $"Not quite — the word is {card.Entry.Headword}.", correct ? Grade.Good : Grade.Again);
    }

    public void SubmitTyped()
    {
        if (Session?.Current is not { } card) return;

        var answer = AnswerBox.Text?.Trim() ?? "";
        var distance = Cloze.Distance(answer, card.Entry.Headword);
        var tolerance = card.Mode == QuizMode.Cloze ? 1 : 2;

        var grade = distance == 0 ? Grade.Good : distance <= tolerance ? Grade.Hard : Grade.Again;
        var message = grade switch
        {
            Grade.Good => "Correct.",
            Grade.Hard => $"Almost — the word is {card.Entry.Headword}.",
            _ => $"The word is {card.Entry.Headword}.",
        };

        RevealAnswer(card, message, grade);
    }

    private void RevealAnswer(Card card, string verdict, Grade suggested)
    {
        IsRevealed = true;

        var back = Ui.Stack(18,
            Ui.Label("ANSWER"),
            Ui.Display(card.Entry.Headword, 34),
            Ui.Text(verdict, 13, suggested == Grade.Again ? Ui.Danger : Ui.Ink),
            Ui.Rule(),
            Details(card),
            Ui.Rule(),
            GradeRow(card));

        _body.Content = Ui.Pad(Ui.Rows("Auto,*",
            Ui.Label($"{Session!.Position:00} / {Session.Total:00}  ·  {card.Mode.ToColumn().ToUpperInvariant()}"),
            Ui.Scroll(back)), 34);

        Motion.Enter(back, 8);
    }

    public void Reveal()
    {
        if (Session?.Current is not { } card || IsRevealed) return;
        RevealAnswer(card, "", Grade.Good);
    }

    private Control Details(Card card)
    {
        var stack = Ui.Stack(12);

        if (!string.IsNullOrWhiteSpace(card.Entry.Definition))
        {
            var definition = Ui.Text(card.Entry.Definition!, 14);
            definition.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(Ui.Stack(6, Ui.Label("DEFINITION"), definition));
        }

        var synonyms = _context.Library.Relations(card.Entry.Id)
            .Where(r => r.Kind == RelationKind.Synonym).Select(r => r.Word).ToList();

        if (synonyms.Count > 0)
            stack.Children.Add(Ui.Stack(6, Ui.Label("SYNONYMS"),
                Ui.Text(string.Join("   ", synonyms), 13, Ui.Muted)));

        if (card.Sentence is { } sentence)
        {
            var text = Ui.Text("“" + sentence.Text + "”", 14, Ui.Ink);
            text.FontFamily = Ui.Serif;
            text.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(Ui.Stack(6, Ui.Label("EXAMPLE"), text));
        }

        return stack;
    }

    private Control GradeRow(Card card)
    {
        var row = Ui.Row(10);

        foreach (var (grade, caption) in new[]
        {
            (Grade.Again, "Again"), (Grade.Hard, "Hard"), (Grade.Good, "Good"), (Grade.Easy, "Easy"),
        })
        {
            var days = Session!.PreviewIntervalDays(grade);
            var button = Ui.Button("", () => Commit(card, grade), grade == Grade.Good ? "primary" : "outline");
            button.Content = Ui.Stack(4,
                Ui.Text(caption, 13, grade == Grade.Good ? Brush.Parse("#F0F0DF") : Ui.Ink),
                Ui.Label(days == 0 ? "NOW" : $"{days} D",
                    grade == Grade.Good ? Brush.Parse("#BFD8C9") : Ui.Muted));
            row.Children.Add(button);
        }

        return row;
    }

    public void Grade(Grade grade)
    {
        if (Session?.Current is not { } card || !IsRevealed) return;
        Commit(card, grade);
    }

    private void Commit(Card card, Grade grade)
    {
        Session!.Answer(grade, (int)_clock.ElapsedMilliseconds, DateTime.UtcNow);
        _ = card;
        RenderCard();
    }

    private void RenderSummary()
    {
        var summary = Session?.Summary ?? new SessionSummary(0, 0, 0, TimeSpan.Zero);
        Session = null;
        IsRevealed = false;

        var accuracy = summary.Reviewed == 0 ? 0 : summary.Correct / (double)summary.Reviewed;

        _body.Content = Ui.Pad(Ui.Stack(24,
            Ui.Heading("SESSION COMPLETE", "Please enjoy each word equally.",
                "Your schedule has been updated."),
            Ui.Columns("*,14,*,14,*",
                Ui.StatTile("01", $"{summary.Reviewed}", "REVIEWED"),
                new Border(),
                Ui.StatTile("02", $"{accuracy:P0}", "ACCURACY"),
                new Border(),
                Ui.StatTile("03", $"{summary.Lapsed}", "LAPSED")),
            Ui.Row(12,
                Ui.Button("Another session", ShowSetup, "primary"),
                Ui.Button("Back to the library", () => _context.Navigate("library", null), "outline"))), 34);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Session?.Current is null) return;

        switch (e.Key)
        {
            case Key.Space when !IsRevealed:
                Reveal(); e.Handled = true; break;
            case Key.D1 or Key.NumPad1: Grade(Grade.Again); e.Handled = true; break;
            case Key.D2 or Key.NumPad2: Grade(Grade.Hard); e.Handled = true; break;
            case Key.D3 or Key.NumPad3: Grade(Grade.Good); e.Handled = true; break;
            case Key.D4 or Key.NumPad4: Grade(Grade.Easy); e.Handled = true; break;
            case Key.Escape: RenderSummary(); e.Handled = true; break;
        }
    }
}
```

- [ ] **Step 4: Run the checks to verify they pass**

Run: `dotnet run --project tools/Checks`
Expected: every check prints `ok`, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add Views/QuizView.cs tools/Checks/Program.cs
git commit -m "Add the quiz view with four modes and interval previews"
```

---

### Task 20: Decks

**Files:**
- Create: `Views/DecksView.cs`
- Modify: `Views/LibraryView.cs` (add the `Save this filter as a deck` action)
- Modify: `tools/Checks/Program.cs` (add `CheckDecksView`)

**Interfaces:**
- Consumes: `AppContext`, `Library`, `Search`, `SearchFilter`.
- Produces: `DecksView(AppContext context) : UserControl, IDisposable` with
  `.Decks -> IReadOnlyList<Deck>`, `.Refresh()`, `.Open(long deckId)`,
  `.MembersOf(Deck deck) -> IReadOnlyList<Entry>`
- Produces on `LibraryView`: `.SaveFilterAsDeck(string name) -> long`

A deck with `filter_json` is evaluated live through `Search`; a deck without one
lists its stored members.

- [ ] **Step 1: Write the failing check**

Append to `tools/Checks/Program.cs` and call it from `Main`:

```csharp
    private static void CheckDecksView()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        var eminence = library.AddEntry("eminence");
        library.UpdateEntry(library.GetEntry(eminence)! with { PartOfSpeech = "noun" });
        var laconic = library.AddEntry("laconic");
        library.UpdateEntry(library.GetEntry(laconic)! with { PartOfSpeech = "adjective" });

        var context = new AppContext(library, new Settings(db), new Search(db), new Stats(db),
            new StubLookup(), _ => { }, (_, _) => { });

        var manual = library.CreateDeck("Exam");
        library.AddToDeck(manual, eminence);

        var view = new DecksView(context);
        Expect("lists decks", 1, view.Decks.Count);
        Expect("manual members", 1, view.MembersOf(view.Decks[0]).Count);

        var libraryView = new LibraryView(context, "library");
        libraryView.SearchBox.Text = "laconic";
        var smart = libraryView.SaveFilterAsDeck("Smart");
        libraryView.Dispose();

        view.Refresh();
        Expect("smart deck added", 2, view.Decks.Count);

        var smartDeck = view.Decks.Single(d => d.Id == smart);
        Expect("smart deck evaluates live", 1, view.MembersOf(smartDeck).Count);
        Expect("smart deck resolves", "laconic", view.MembersOf(smartDeck)[0].Headword);

        view.Dispose();
    }
```

- [ ] **Step 2: Run the checks to verify they fail**

Run: `dotnet run --project tools/Checks`
Expected: FAIL — the stub `DecksView` has no `Decks`.

- [ ] **Step 3: Write the implementation**

Add to `Views/LibraryView.cs`:

```csharp
    public long SaveFilterAsDeck(string name) =>
        _context.Library.CreateDeck(name, $"Saved from the {_preset} view.",
            System.Text.Json.JsonSerializer.Serialize(Filter));
```

and add the button to the toolbar row in the constructor, replacing the
existing three-column toolbar:

```csharp
        var toolbar = Ui.Columns("*,Auto,Auto,Auto",
            SearchBox,
            _sort,
            Ui.Button("Save as deck", PromptForDeckName, "outline"),
            Ui.Button("+ Word", NewWord, "primary"));
```

with:

```csharp
    private void PromptForDeckName()
    {
        var name = new TextBox { Watermark = "Deck name" };
        var error = Ui.Text("", 12, Ui.Danger);

        var form = Ui.Stack(16,
            Ui.Heading("SMART DECK", "Save this filter.",
                "The deck re-evaluates every time you open it."),
            name, error,
            Ui.Row(12,
                Ui.Button("Cancel", () => Detail.Content = Placeholder(), "outline"),
                Ui.Button("Save deck", () =>
                {
                    var value = name.Text?.Trim() ?? "";
                    if (value.Length == 0) { error.Text = "Name the deck."; return; }
                    if (_context.Library.Decks().Any(d => d.Name == value))
                    { error.Text = "A deck already has that name."; return; }
                    SaveFilterAsDeck(value);
                    _context.Toast("Smart deck saved.");
                    Detail.Content = Placeholder();
                }, "primary")));

        Detail.Content = Ui.Pad(form, 30);
        name.Focus();
    }
```

Create `Views/DecksView.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class DecksView : UserControl, IDisposable
{
    private readonly AppContext _context;
    private readonly ContentControl _body = new();

    public IReadOnlyList<Deck> Decks { get; private set; } = [];

    public DecksView(AppContext context)
    {
        _context = context;
        Content = _body;
        context.Library.Changed += Refresh;
        Refresh();
    }

    public void Dispose() => _context.Library.Changed -= Refresh;

    /// <summary>A deck with a stored filter is evaluated live; otherwise its
    /// membership rows are the answer.</summary>
    public IReadOnlyList<Entry> MembersOf(Deck deck)
    {
        if (deck.FilterJson is not { Length: > 0 } json) return _context.Library.DeckEntries(deck.Id);

        try
        {
            var filter = System.Text.Json.JsonSerializer.Deserialize<SearchFilter>(json);
            if (filter is null) return [];
            return _context.Search.Run(filter, DateTime.UtcNow).Select(h => h.Entry).ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    public void Refresh()
    {
        Decks = _context.Library.Decks();

        var list = Ui.Stack(0);

        foreach (var deck in Decks)
        {
            var members = MembersOf(deck);
            var smart = deck.FilterJson is { Length: > 0 };

            var left = Ui.Stack(6,
                Ui.Row(9,
                    new Glyph(smart ? "search" : "deck") { Width = 14, Height = 14 },
                    Ui.Text(deck.Name, 14)),
                Ui.Label($"{members.Count:00} WORDS  ·  {(smart ? "SMART" : "MANUAL")}" +
                         (deck.Description is { Length: > 0 } d ? $"  ·  {d.ToUpperInvariant()}" : "")));

            var actions = Ui.Row(0,
                Ui.IconButton("quiz", "Quiz this deck",
                    () => _context.Navigate("quiz", new ReviewScope(ScopeKind.Deck, DeckId: deck.Id))),
                Ui.IconButton("arrow", "Open this deck", () => Open(deck.Id)),
                Ui.IconButton("close", "Delete this deck", () => ConfirmDelete(deck)));

            var row = Ui.Columns("*,Auto", left, actions);
            row.Margin = new Thickness(30, 16);

            list.Children.Add(row);
            list.Children.Add(Ui.Rule(Ui.RowRule));
        }

        if (Decks.Count == 0)
            list.Children.Add(Ui.Pad(Ui.Text(
                "No decks yet. Filter the library and save the filter, or build one by hand.", 13, Ui.Muted), 30));

        var header = Ui.Columns("*,Auto",
            Ui.Heading("COLLECTIONS", "Decks.", "A deck is a smaller promise than the whole library."),
            Ui.Button("+ Deck", PromptForDeck, "primary"));
        header.Margin = new Thickness(30, 26, 30, 20);

        _body.Content = Ui.Rows("Auto,Auto,*", header, Ui.Rule(), Ui.Scroll(list));
    }

    public void Open(long deckId)
    {
        var deck = Decks.FirstOrDefault(d => d.Id == deckId);
        if (deck is null) return;

        var members = MembersOf(deck);
        var list = Ui.Stack(0);

        foreach (var entry in members)
        {
            var actions = deck.FilterJson is { Length: > 0 }
                ? (Control)Ui.Label("VIA FILTER")
                : Ui.IconButton("close", "Remove from this deck", () =>
                {
                    _context.Library.RemoveFromDeck(deck.Id, entry.Id);
                    Open(deck.Id);
                });

            var row = Ui.Columns("*,Auto",
                Ui.Text(entry.Headword, 13),
                actions);
            row.Margin = new Thickness(30, 13);

            list.Children.Add(row);
            list.Children.Add(Ui.Rule(Ui.RowRule));
        }

        var adder = new ComboBox
        {
            ItemsSource = _context.Library.AllEntries()
                .Where(e => members.All(m => m.Id != e.Id)).Select(e => e.Headword).ToList(),
            Width = 220,
            PlaceholderText = "Add a word…",
        };
        adder.SelectionChanged += (_, _) =>
        {
            if (adder.SelectedItem is not string headword) return;
            if (_context.Library.FindEntry(headword) is { } entry) _context.Library.AddToDeck(deck.Id, entry.Id);
            Open(deck.Id);
        };

        var header = Ui.Columns("*,Auto",
            Ui.Heading($"DECK / {Ui.HexId(deck.Id)}", deck.Name,
                deck.FilterJson is { Length: > 0 }
                    ? "A smart deck. Its membership follows the filter it was saved from."
                    : "A manual deck."),
            Ui.Row(10,
                deck.FilterJson is { Length: > 0 } ? new Border() : adder,
                Ui.Button("Back", Refresh, "outline")));
        header.Margin = new Thickness(30, 26, 30, 20);

        _body.Content = Ui.Rows("Auto,Auto,*", header, Ui.Rule(), Ui.Scroll(list));
    }

    private void PromptForDeck()
    {
        var name = new TextBox { Watermark = "Deck name" };
        var description = new TextBox { Watermark = "What it is for (optional)" };
        var error = Ui.Text("", 12, Ui.Danger);

        _body.Content = Ui.Pad(Ui.Stack(16,
            Ui.Heading("COLLECTIONS", "A new deck.", "Add words to it from the library or from here."),
            name, description, error,
            Ui.Row(12,
                Ui.Button("Cancel", Refresh, "outline"),
                Ui.Button("Create deck", () =>
                {
                    var value = name.Text?.Trim() ?? "";
                    if (value.Length == 0) { error.Text = "Name the deck."; return; }
                    if (_context.Library.Decks().Any(d => d.Name == value))
                    { error.Text = "A deck already has that name."; return; }
                    _context.Library.CreateDeck(value,
                        string.IsNullOrWhiteSpace(description.Text) ? null : description.Text!.Trim());
                    _context.Toast("Deck created.");
                }, "primary"))), 34);

        name.Focus();
    }

    private void ConfirmDelete(Deck deck)
    {
        _body.Content = Ui.Pad(Ui.Stack(18,
            Ui.Heading("COLLECTIONS", "Remove this deck?",
                "The words themselves stay in the library."),
            Ui.Display(deck.Name, 24),
            Ui.Row(12,
                Ui.Button("Keep it", Refresh, "outline"),
                Ui.Button("Delete", () =>
                {
                    _context.Library.DeleteDeck(deck.Id);
                    _context.Toast("Deck deleted.");
                }, "primary"))), 34);
    }
}
```

- [ ] **Step 4: Run the checks to verify they pass**

Run: `dotnet run --project tools/Checks`
Expected: every check prints `ok`, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add Views/DecksView.cs Views/LibraryView.cs tools/Checks/Program.cs
git commit -m "Add manual and smart decks"
```

---

### Task 21: The stats dashboard

**Files:**
- Create: `Views/StatsView.cs`, `Shell/Heatmap.cs`
- Modify: `tools/Checks/Program.cs` (add `CheckStatsView`)

**Interfaces:**
- Consumes: `AppContext`, `Stats`, `Overview`, `HeatmapDay`, `RetentionPoint`, `MasteryBucket`.
- Produces:
  - `Heatmap(IReadOnlyList<HeatmapDay> days) : Control` — 53×7 phosphor grid
  - `RetentionCurve(IReadOnlyList<RetentionPoint> points) : Control`
  - `StatsView(AppContext context) : UserControl, IDisposable` with
    `.Overview -> Overview`, `.Refresh()`

- [ ] **Step 1: Write the failing check**

Append to `tools/Checks/Program.cs` and call it from `Main`:

```csharp
    private static void CheckStatsView()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        var id = library.AddEntry("eminence");
        library.AddReview(new Review(0, id, QuizMode.Flip, Grade.Good, true, true, 900, 5, 10, DateTime.UtcNow));

        var context = new AppContext(library, new Settings(db), new Search(db), new Stats(db),
            new StubLookup(), _ => { }, (_, _) => { });

        var view = new StatsView(context);
        Expect("overview words", 1, view.Overview.TotalWords);
        Expect("overview reviews today", 1, view.Overview.ReviewsToday);
        Expect("overview streak", 1, view.Overview.StreakDays);

        var heatmap = new Heatmap(new Stats(db).Heatmap(DateTime.UtcNow, weeks: 53));
        heatmap.Measure(new Size(800, 200));
        Expect("heatmap has a width", true, heatmap.DesiredSize.Width > 0);

        view.Dispose();
    }
```

- [ ] **Step 2: Run the checks to verify they fail**

Run: `dotnet run --project tools/Checks`
Expected: FAIL — `Heatmap` and the real `StatsView` do not exist.

- [ ] **Step 3: Write the implementation**

Create `Shell/Heatmap.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SirWordALot.Core;

namespace SirWordALot.Shell;

/// <summary>A GitHub-style contribution grid: one column per week, one cell per day,
/// tinted from the cream surface towards phosphor as the count rises.</summary>
public sealed class Heatmap(IReadOnlyList<HeatmapDay> days) : Control
{
    private const double Cell = 11, Gap = 3;

    private static readonly IBrush[] Levels =
    [
        Brush.Parse("#E3E6D8"),
        Brush.Parse("#BFD7CB"),
        Brush.Parse("#93BFAE"),
        Brush.Parse("#63A28D"),
        Brush.Parse("#3C7D69"),
    ];

    protected override Size MeasureOverride(Size availableSize)
    {
        var weeks = (int)Math.Ceiling(days.Count / 7.0);
        return new Size(weeks * (Cell + Gap), 7 * (Cell + Gap));
    }

    public override void Render(DrawingContext context)
    {
        if (days.Count == 0) return;
        var busiest = Math.Max(1, days.Max(d => d.Count));

        for (var i = 0; i < days.Count; i++)
        {
            var week = i / 7;
            var weekday = i % 7;
            var level = days[i].Count == 0
                ? 0
                : Math.Clamp((int)Math.Ceiling(days[i].Count / (double)busiest * 4), 1, 4);

            context.FillRectangle(Levels[level],
                new Rect(week * (Cell + Gap), weekday * (Cell + Gap), Cell, Cell));
        }
    }
}

/// <summary>Correct-rate by interval bucket, drawn as a polyline with markers.</summary>
public sealed class RetentionCurve(IReadOnlyList<RetentionPoint> points) : Control
{
    protected override Size MeasureOverride(Size availableSize) => new(availableSize.Width, 120);

    public override void Render(DrawingContext context)
    {
        if (points.Count < 2) return;

        var pen = new Pen(Brush.Parse("#4E8A7B"), 1.5);
        var axis = new Pen(Ui.Line, 1);
        var height = Bounds.Height - 18;
        var step = Bounds.Width / (points.Count - 1);

        context.DrawLine(axis, new Point(0, height), new Point(Bounds.Width, height));

        Point At(int i) => new(i * step, height - points[i].Rate * height);

        for (var i = 1; i < points.Count; i++) context.DrawLine(pen, At(i - 1), At(i));
        for (var i = 0; i < points.Count; i++)
            context.DrawEllipse(points[i].Samples == 0 ? Ui.Line : Brush.Parse("#4E8A7B"), null, At(i), 2.5, 2.5);
    }
}
```

Create `Views/StatsView.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class StatsView : UserControl, IDisposable
{
    private readonly AppContext _context;
    private readonly ContentControl _body = new();

    public Overview Overview { get; private set; } = new(0, 0, 0, 0, 0, 0);

    public StatsView(AppContext context)
    {
        _context = context;
        Content = _body;
        context.Library.Changed += Refresh;
        Refresh();
    }

    public void Dispose() => _context.Library.Changed -= Refresh;

    public void Refresh()
    {
        var now = DateTime.UtcNow;
        Overview = _context.Stats.Summary(now);
        var breakdown = _context.Stats.Breakdown();
        var goal = _context.Settings.DailyGoal;

        var tiles = Ui.Columns("*,12,*,12,*,12,*,12,*,12,*",
            Ui.StatTile("01", $"{Overview.TotalWords}", "WORDS HELD"),
            new Border(),
            Ui.StatTile("02", $"{Overview.DueToday}", "DUE TODAY"),
            new Border(),
            Ui.StatTile("03", $"{Overview.Mastered}", "MASTERED"),
            new Border(),
            Ui.StatTile("04", $"{Overview.StreakDays}", "DAY STREAK"),
            new Border(),
            Ui.StatTile("05", $"{Overview.Retention:P0}", "RETENTION"),
            new Border(),
            Ui.StatTile("06", $"{Overview.ReviewsToday}/{goal}", "TODAY"));

        var heatmap = Ui.Stack(10,
            Ui.Columns("*,Auto",
                Ui.Label("REVIEW ACTIVITY  ·  53 WEEKS"),
                Ui.Label($"{_context.Stats.Heatmap(now).Sum(d => d.Count)} REVIEWS")),
            Ui.Scroll(new Heatmap(_context.Stats.Heatmap(now))));

        var bars = Ui.Stack(9, Ui.Label("MASTERY BREAKDOWN"));
        var largest = Math.Max(1, breakdown.Values.DefaultIfEmpty(0).Max());

        foreach (var (bucket, count) in breakdown.OrderBy(p => p.Key))
        {
            var fill = new Border
            {
                Height = 14,
                Background = Brush.Parse("#4E8A7B"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(1, count / (double)largest * 340),
            };
            var track = new Grid { Width = 340, Children = { new Border { Height = 14, Background = Ui.RowRule }, fill } };

            bars.Children.Add(Ui.Columns("120,Auto,*",
                Ui.Label(bucket.ToString().ToUpperInvariant()),
                track,
                Ui.Text($"  {count}", 12, Ui.Muted, mono: true)));
        }

        var curve = Ui.Stack(10,
            Ui.Label("RETENTION BY INTERVAL"),
            new RetentionCurve(_context.Stats.RetentionCurve()),
            Ui.Row(0, _context.Stats.RetentionCurve()
                .Select(p => (Control)Ui.Label($"{p.UpperDays}D  ")).ToArray()));

        var header = Ui.Heading("DEPARTMENT RECORD", "The work, measured.",
            "Every number here comes from your own answers.");
        header.Margin = new Thickness(0, 0, 0, 24);

        _body.Content = Ui.Scroll(Ui.Pad(Ui.Stack(28, header, tiles, Ui.Rule(), heatmap, Ui.Rule(), bars,
            Ui.Rule(), curve), 32));
    }
}
```

- [ ] **Step 4: Run the checks to verify they pass**

Run: `dotnet run --project tools/Checks`
Expected: every check prints `ok`, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add Views/StatsView.cs Shell/Heatmap.cs tools/Checks/Program.cs
git commit -m "Add the stats dashboard with heatmap and retention curve"
```

---

### Task 22: The console

**Files:**
- Create: `Views/ConsoleView.cs`, `Core/ConsoleCommands.cs`
- Test: `tests/ConsoleCommandTests.cs`

**Interfaces:**
- Consumes: `Library`, `Search`, `Stats`, `Portability`, `Backups`, `Settings`.
- Produces:
  - `ConsoleCommands(Library library, Search search, Stats stats, Settings settings)` with
    `Run(string line) -> IReadOnlyList<string>` and `Names -> IReadOnlyList<string>`
  - `ConsoleView(AppContext context) : UserControl`

The command parser lives in `Core` so it is tested without a window; the view is
a thin terminal surface over it.

- [ ] **Step 1: Write the failing test**

Create `tests/ConsoleCommandTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter ConsoleCommandTests`
Expected: FAIL — `ConsoleCommands` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Core/ConsoleCommands.cs`:

```csharp
namespace SirWordALot.Core;

/// <summary>The Department shell. Every command acts on the real library —
/// nothing here is simulated.</summary>
public sealed class ConsoleCommands(Library library, Search search, Stats stats, Settings settings)
{
    public IReadOnlyList<string> Names =>
        ["add", "find", "show", "sentence", "tag", "untag", "star", "due", "stats",
         "export", "import", "backup", "help", "clear"];

    public IReadOnlyList<string> Run(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return [];

        var space = trimmed.IndexOf(' ');
        var command = (space < 0 ? trimmed : trimmed[..space]).ToLowerInvariant();
        var rest = space < 0 ? "" : trimmed[(space + 1)..].Trim();

        return command switch
        {
            "help" => Help(),
            "clear" => ["\u0000clear"],
            "add" => Add(rest),
            "find" => Find(rest),
            "show" => Show(rest),
            "sentence" => AddSentence(rest),
            "tag" => Tag(rest, attach: true),
            "untag" => Tag(rest, attach: false),
            "star" => Star(rest),
            "due" => Due(),
            "stats" => Summary(),
            "export" => Export(rest),
            "import" => Import(rest),
            "backup" => Backup(),
            _ => [$"Unknown command '{command}'. Type help to begin."],
        };
    }

    private IReadOnlyList<string> Help() =>
    [
        "SIR WORD-A-LOT  ·  DEPARTMENT SHELL",
        "",
        "  add <word>                  Add a word to the library",
        "  find <query>                Search words, definitions and sentences",
        "  show <word>                 Print everything held about a word",
        "  sentence <word> <text>      Attach an example sentence",
        "  tag <word> <tag>            Tag a word",
        "  untag <word> <tag>          Remove a tag",
        "  star <word>                 Toggle the star on a word",
        "  due                         List what is due now",
        "  stats                       Print the overview",
        "  export <json|csv|tsv> <path>  Write the library to a file",
        "  import <path>               Read a JSON export back in",
        "  backup                      Write a timestamped snapshot",
        "  help                        Show this text",
        "  clear                       Clear the screen",
    ];

    private IReadOnlyList<string> Add(string word)
    {
        if (word.Length == 0) return ["Usage: add <word>"];
        if (library.FindEntry(word) is not null) return [$"'{word}' is already in the library."];
        library.AddEntry(word);
        return [$"Added '{word}'. It is due immediately."];
    }

    private IReadOnlyList<string> Find(string query)
    {
        if (query.Length == 0) return ["Usage: find <query>"];

        var hits = search.Run(new SearchFilter(Text: query), DateTime.UtcNow);
        if (hits.Count == 0) return ["No matches."];

        return hits.Take(20).Select(hit =>
            $"  {hit.Entry.Headword,-24} {hit.Entry.PartOfSpeech ?? "—",-12} {hit.MasteryPercent,3}%")
            .Prepend($"{hits.Count} match(es):").ToList();
    }

    private IReadOnlyList<string> Show(string word)
    {
        if (word.Length == 0) return ["Usage: show <word>"];
        if (library.FindEntry(word) is not { } entry) return [$"'{word}' is not in the library."];

        var scheduling = library.GetScheduling(entry.Id);
        var reviews = library.ReviewsFor(entry.Id);
        var lines = new List<string>
        {
            $"{entry.Headword}  {entry.Pronunciation ?? ""}  ·  {entry.PartOfSpeech ?? "—"}",
            "",
        };

        if (entry.Definition is { Length: > 0 }) lines.Add($"  {entry.Definition}");

        foreach (var kind in new[] { RelationKind.Synonym, RelationKind.Antonym, RelationKind.Related })
        {
            var words = library.Relations(entry.Id).Where(r => r.Kind == kind).Select(r => r.Word).ToList();
            if (words.Count > 0) lines.Add($"  {kind.ToColumn(),-9} {string.Join(", ", words)}");
        }

        foreach (var sentence in library.SentencesFor(entry.Id)) lines.Add($"  \"{sentence.Text}\"");

        var tags = library.TagsFor(entry.Id);
        if (tags.Count > 0) lines.Add($"  tags      {string.Join(", ", tags)}");

        lines.Add("");
        lines.Add($"  due {scheduling.DueUtc:yyyy-MM-dd}  ·  interval {scheduling.IntervalDays} d  " +
                  $"·  reps {scheduling.Repetitions}  ·  lapses {scheduling.Lapses}  ·  {reviews.Count} review(s)");
        return lines;
    }

    private IReadOnlyList<string> AddSentence(string rest)
    {
        var space = rest.IndexOf(' ');
        if (space < 0) return ["Usage: sentence <word> <text>"];

        var word = rest[..space];
        var text = rest[(space + 1)..].Trim();
        if (text.Length == 0) return ["Usage: sentence <word> <text>"];
        if (library.FindEntry(word) is not { } entry) return [$"'{word}' is not in the library."];

        library.AddSentence(text, entry.Id);
        return [$"Sentence attached to '{entry.Headword}'."];
    }

    private IReadOnlyList<string> Tag(string rest, bool attach)
    {
        var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return [$"Usage: {(attach ? "tag" : "untag")} <word> <tag>"];
        if (library.FindEntry(parts[0]) is not { } entry) return [$"'{parts[0]}' is not in the library."];

        if (attach) library.TagEntry(entry.Id, parts[1]);
        else library.UntagEntry(entry.Id, parts[1]);

        return [$"{(attach ? "Tagged" : "Untagged")} '{entry.Headword}' with '{parts[1].ToLowerInvariant()}'."];
    }

    private IReadOnlyList<string> Star(string word)
    {
        if (word.Length == 0) return ["Usage: star <word>"];
        if (library.FindEntry(word) is not { } entry) return [$"'{word}' is not in the library."];

        library.SetStarred(entry.Id, !entry.Starred);
        return [$"'{entry.Headword}' is {(entry.Starred ? "no longer starred" : "starred")}."];
    }

    private IReadOnlyList<string> Due()
    {
        var now = DateTime.UtcNow;
        var due = library.DueEntries(now).Concat(library.NewEntries(settings.NewPerDay)).ToList();
        if (due.Count == 0) return ["Nothing is due. Enjoy the quiet."];

        return due.Take(30).Select(e => $"  {e.Headword}")
            .Prepend($"{due.Count} due:").ToList();
    }

    private IReadOnlyList<string> Summary()
    {
        var overview = stats.Summary(DateTime.UtcNow);
        return
        [
            $"  words       {overview.TotalWords}",
            $"  due today   {overview.DueToday}",
            $"  mastered    {overview.Mastered}",
            $"  streak      {overview.StreakDays} day(s)",
            $"  retention   {overview.Retention:P1}",
            $"  today       {overview.ReviewsToday} / {settings.DailyGoal}",
        ];
    }

    private IReadOnlyList<string> Export(string rest)
    {
        var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return ["Usage: export <json|csv|tsv> <path>"];

        var content = parts[0].ToLowerInvariant() switch
        {
            "json" => Portability.ExportJson(library),
            "csv" => Portability.ExportCsv(library),
            "tsv" => Portability.ExportAnkiTsv(library),
            _ => null,
        };

        if (content is null) return ["Format must be json, csv or tsv."];

        try
        {
            Backups.WriteAtomic(parts[1], content);
            return [$"Wrote {content.Length} characters to {parts[1]}."];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [$"Could not write that file: {e.Message}"];
        }
    }

    private IReadOnlyList<string> Import(string path)
    {
        if (path.Length == 0) return ["Usage: import <path>"];
        if (!File.Exists(path)) return [$"No file at {path}."];

        try
        {
            var report = Portability.ImportJson(library, File.ReadAllText(path), MergePolicy.TakeTheirs);
            return [$"Added {report.Added}, updated {report.Updated}, skipped {report.Skipped}."];
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return [$"Could not import that file: {e.Message}"];
        }
    }

    private IReadOnlyList<string> Backup()
    {
        var backups = new Backups(settings.BackupFolder);
        var path = backups.Write(library, DateTime.UtcNow);
        backups.Prune();
        return [$"Snapshot written to {path}."];
    }
}
```

Create `Views/ConsoleView.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class ConsoleView : UserControl
{
    private readonly ConsoleCommands _commands;
    private readonly StackPanel _output = Ui.Stack(2);
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input = new() { Watermark = "Type help to begin" };
    private readonly List<string> _history = [];
    private int _historyIndex;

    public ConsoleView(AppContext context)
    {
        _commands = new ConsoleCommands(context.Library, context.Search, context.Stats, context.Settings);
        _input.Classes.Add("terminal");

        Write("SIR WORD-A-LOT [VERSION 0.1.0]");
        Write("(c) Fezcode. Every word is valued.");
        Write("");
        Write("Secure session established.");
        Write("");

        _scroll = new ScrollViewer
        {
            Content = _output,
            Padding = new Thickness(20, 16),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var prompt = Ui.Columns("Auto,*",
            Ui.Text("swl:~ $", 12.5, Ui.Phosphor, mono: true),
            _input);
        prompt.Margin = new Thickness(20, 0, 20, 14);

        var footer = new Border
        {
            Height = 30,
            Background = Brush.Parse("#0C2429"),
            Padding = new Thickness(20, 0),
            Child = Ui.Columns("*,Auto",
                Ui.Label("SWL  /  DEPARTMENT SHELL", Brush.Parse("#5E8F8B")),
                Ui.Label("LIVE", Ui.Phosphor)),
        };

        Content = new Border
        {
            Background = Ui.Dark,
            Child = Ui.Rows("*,Auto,Auto", _scroll, prompt, footer),
        };

        _input.KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Submit();
                e.Handled = true;
                break;
            case Key.Up when _history.Count > 0:
                _historyIndex = Math.Max(0, _historyIndex - 1);
                _input.Text = _history[_historyIndex];
                e.Handled = true;
                break;
            case Key.Down when _history.Count > 0:
                _historyIndex = Math.Min(_history.Count, _historyIndex + 1);
                _input.Text = _historyIndex < _history.Count ? _history[_historyIndex] : "";
                e.Handled = true;
                break;
        }
    }

    private void Submit()
    {
        var line = _input.Text ?? "";
        _input.Text = "";

        if (line.Trim().Length > 0)
        {
            _history.Add(line);
            _historyIndex = _history.Count;
        }

        Write($"swl:~ $ {line}", Brush.Parse("#6FA69C"));

        foreach (var output in _commands.Run(line))
        {
            if (output == "\u0000clear") { _output.Children.Clear(); return; }
            Write(output);
        }

        Write("");
        _scroll.ScrollToEnd();
    }

    private void Write(string text, IBrush? color = null) =>
        _output.Children.Add(Ui.Text(text, 12.5, color ?? Ui.Phosphor, mono: true));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter ConsoleCommandTests; dotnet run --project tools/Checks`
Expected: 15 tests pass; checks exit 0.

- [ ] **Step 5: Commit**

```bash
git add Core/ConsoleCommands.cs Views/ConsoleView.cs tests/ConsoleCommandTests.cs
git commit -m "Add the Department shell console over the real library"
```

---

### Task 23: Settings

**Files:**
- Create: `Views/SettingsView.cs`
- Modify: `tools/Checks/Program.cs` (add `CheckSettingsView`)

**Interfaces:**
- Consumes: `AppContext`, `Settings`, `Scheduler`, `Portability`, `Backups`.
- Produces: `SettingsView(AppContext context) : UserControl` with
  `.ApplyScheduler(SchedulerKind kind)`, `.ExportTo(string path, string format)`,
  `.ImportFrom(string path, MergePolicy policy) -> ImportReport`,
  `.WriteBackup() -> string`

Switching scheduler converts every existing `scheduling` row through
`Scheduler.Convert`, so no progress is lost.

- [ ] **Step 1: Write the failing check**

Append to `tools/Checks/Program.cs` and call it from `Main`:

```csharp
    private static void CheckSettingsView()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        var id = library.AddEntry("eminence");
        library.SaveScheduling(library.GetScheduling(id) with
        { IntervalDays = 7, Repetitions = 3, LastReviewedUtc = DateTime.UtcNow });

        var settings = new Settings(db);
        var context = new AppContext(library, settings, new Search(db), new Stats(db),
            new StubLookup(), _ => { }, (_, _) => { });

        var view = new SettingsView(context);
        var dueBefore = library.GetScheduling(id).DueUtc;

        view.ApplyScheduler(SchedulerKind.Leitner);

        Expect("scheduler stored", SchedulerKind.Leitner, settings.Scheduler);
        Expect("rows converted", SchedulerKind.Leitner, library.GetScheduling(id).Scheduler);
        Expect("box seeded from interval", 4, library.GetScheduling(id).Box);
        Expect("due date preserved", dueBefore, library.GetScheduling(id).DueUtc);

        var path = Path.Combine(Path.GetTempPath(), $"swl-check-{Guid.NewGuid():N}.json");
        try
        {
            view.ExportTo(path, "json");
            Expect("export writes", true, File.Exists(path));

            var target = new Library(Db.OpenMemory());
            var targetContext = context with { Library = target };
            var report = new SettingsView(targetContext).ImportFrom(path, MergePolicy.TakeTheirs);
            Expect("import reads", 1, report.Added);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
```

- [ ] **Step 2: Run the checks to verify they fail**

Run: `dotnet run --project tools/Checks`
Expected: FAIL — the stub `SettingsView` has no `ApplyScheduler`.

- [ ] **Step 3: Write the implementation**

Create `Views/SettingsView.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Views;

public sealed class SettingsView : UserControl
{
    private readonly AppContext _context;
    private readonly ContentControl _body = new();

    public SettingsView(AppContext context)
    {
        _context = context;
        Content = _body;
        Render();
    }

    public void ApplyScheduler(SchedulerKind kind)
    {
        _context.Settings.Scheduler = kind;

        foreach (var entry in _context.Library.AllEntries(includeArchived: true))
        {
            var current = _context.Library.GetScheduling(entry.Id);
            if (current.Scheduler == kind) continue;
            _context.Library.SaveScheduling(Scheduler.Convert(current, kind));
        }
    }

    public void ExportTo(string path, string format)
    {
        var content = format switch
        {
            "csv" => Portability.ExportCsv(_context.Library),
            "tsv" => Portability.ExportAnkiTsv(_context.Library),
            _ => Portability.ExportJson(_context.Library),
        };
        Backups.WriteAtomic(path, content);
    }

    public ImportReport ImportFrom(string path, MergePolicy policy)
    {
        var text = File.ReadAllText(path);
        return Path.GetExtension(path).ToLowerInvariant() is ".csv" or ".tsv"
            ? Portability.ImportCsv(_context.Library, text, policy)
            : Portability.ImportJson(_context.Library, text, policy);
    }

    public string WriteBackup()
    {
        var backups = new Backups(_context.Settings.BackupFolder);
        var path = backups.Write(_context.Library, DateTime.UtcNow);
        backups.Prune();
        return path;
    }

    private void Render()
    {
        var settings = _context.Settings;

        var palette = new ComboBox
        {
            ItemsSource = new[] { "Petrol", "Evergreen", "Midnight" },
            Width = 190,
        };
        palette.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "Petrol", "Evergreen", "Midnight" }, settings.Palette));
        palette.SelectionChanged += (_, _) =>
        {
            settings.Palette = (string)palette.SelectedItem!;
            _context.Toast("Palette saved. It applies to accents and the console.");
        };

        var motion = new ToggleSwitch { IsChecked = settings.Motion, OnContent = "Smooth", OffContent = "Reduced" };
        motion.IsCheckedChanged += (_, _) =>
        {
            settings.Motion = motion.IsChecked == true;
            Motion.Enabled = settings.Motion;
        };

        var scheduler = new ComboBox
        {
            ItemsSource = new[] { "SM-2 (spaced repetition)", "Leitner (five boxes)" },
            SelectedIndex = settings.Scheduler == SchedulerKind.Leitner ? 1 : 0,
            Width = 230,
        };
        scheduler.SelectionChanged += (_, _) =>
        {
            var kind = scheduler.SelectedIndex == 1 ? SchedulerKind.Leitner : SchedulerKind.Sm2;
            if (kind == settings.Scheduler) return;
            ApplyScheduler(kind);
            _context.Toast("Scheduler switched. Your due dates were kept.");
        };

        var goal = Number(settings.DailyGoal, value => settings.DailyGoal = value);
        var newPerDay = Number(settings.NewPerDay, value => settings.NewPerDay = value);

        var hotkey = new TextBox { Text = settings.CaptureHotkey, Width = 190 };
        hotkey.LostFocus += (_, _) => settings.CaptureHotkey = hotkey.Text ?? "Ctrl+Alt+W";

        var capture = new ToggleSwitch { IsChecked = settings.CaptureEnabled };
        capture.IsCheckedChanged += (_, _) => settings.CaptureEnabled = capture.IsChecked == true;

        var lookup = new ToggleSwitch { IsChecked = settings.LookupEnabled };
        lookup.IsCheckedChanged += (_, _) =>
        {
            settings.LookupEnabled = lookup.IsChecked == true;
            _context.Toast(settings.LookupEnabled
                ? "Lookup enabled."
                : "Lookup disabled. The app now makes no network calls at all.");
        };

        var stack = Ui.Stack(26,
            Ui.Heading("CONFIGURATION", "Settings.", "Everything here is stored with your library."),
            Ui.Rule(),
            Group("APPEARANCE",
                Field("Palette", "Tints accents, the console and the heatmap.", palette),
                Field("Motion", "Turn this off to reduce movement.", motion)),
            Group("STUDY",
                Field("Scheduler", "Switching converts your progress rather than resetting it.", scheduler),
                Field("Daily goal", "Reviews you aim to finish each day.", goal),
                Field("New words per day", "How many unseen words enter the queue each day.", newPerDay)),
            Group("CAPTURE AND LOOKUP",
                Field("Capture hotkey", "Grabs the clipboard word from anywhere.", hotkey),
                Field("Capture enabled", "Unregisters the hotkey when off.", capture),
                Field("Online lookup", "Datamuse and dictionaryapi.dev. Off means no network at all.", lookup)),
            Group("DATA",
                Field("Export", "JSON is lossless and round-trips everything.",
                    Ui.Row(10,
                        Ui.Button("Export JSON", () => Save("json"), "outline"),
                        Ui.Button("Export CSV", () => Save("csv"), "outline"),
                        Ui.Button("Export Anki TSV", () => Save("tsv"), "outline"))),
                Field("Import", "Merges on the headword; you choose what wins.",
                    Ui.Row(10, Ui.Button("Import a file…", Open, "outline"))),
                Field("Backups", $"Kept in {settings.BackupFolder}. The newest 20 are retained.",
                    Ui.Row(10,
                        Ui.Button("Back up now", () => _context.Toast($"Snapshot written to {WriteBackup()}."), "outline"),
                        Ui.Button("Restore…", ShowRestore, "outline"))),
                Field("Data location", Db.DataRoot(),
                    Ui.Button("Copy path", async () =>
                    {
                        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                            await clipboard.SetTextAsync(Db.DataRoot());
                        _context.Toast("Path copied.");
                    }, "outline"))),
            Ui.Rule(),
            Ui.Stack(6,
                Ui.Label("ABOUT"),
                Ui.Text($"Sir Word-a-lot {Program.Version}", 13),
                Ui.Text("An independent project. Dictionary data from dictionaryapi.dev; " +
                        "thesaurus data from Datamuse.", 12, Ui.Muted)));

        _body.Content = Ui.Scroll(Ui.Pad(stack, 34));
    }

    private static Control Group(string caption, params Control[] fields)
    {
        var stack = Ui.Stack(18, Ui.Label(caption));
        foreach (var field in fields) stack.Children.Add(field);
        return stack;
    }

    private static Control Field(string name, string help, Control control)
    {
        var text = Ui.Text(help, 12, Ui.Muted);
        text.TextWrapping = TextWrapping.Wrap;
        return Ui.Columns("300,*", Ui.Stack(5, Ui.Text(name, 13), text), control);
    }

    private static TextBox Number(int value, Action<int> set)
    {
        var box = new TextBox { Text = value.ToString(), Width = 110 };
        box.LostFocus += (_, _) =>
        {
            if (int.TryParse(box.Text, out var parsed)) set(parsed);
            else box.Text = value.ToString();
        };
        return box;
    }

    private async void Save(string format)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export the library",
            SuggestedFileName = $"sir-word-a-lot.{format}",
            DefaultExtension = format,
        });

        if (file?.TryGetLocalPath() is not { } path) return;

        try
        {
            ExportTo(path, format);
            _context.Toast($"Exported to {path}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _context.Toast($"Could not write that file: {e.Message}");
        }
    }

    private async void Open()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import into the library",
            AllowMultiple = false,
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        ShowMergePrompt(path);
    }

    private void ShowMergePrompt(string path)
    {
        void Run(MergePolicy policy)
        {
            try
            {
                var report = ImportFrom(path, policy);
                _context.Toast($"Added {report.Added}, updated {report.Updated}, skipped {report.Skipped}.");
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _context.Toast($"Could not import that file: {e.Message}");
            }
            Render();
        }

        _body.Content = Ui.Pad(Ui.Stack(18,
            Ui.Heading("IMPORT", "What wins on a clash?",
                "Words that already exist in your library can be kept, replaced or duplicated."),
            Ui.Text(path, 12, Ui.Muted, mono: true),
            Ui.Row(12,
                Ui.Button("Keep mine", () => Run(MergePolicy.KeepMine), "outline"),
                Ui.Button("Take theirs", () => Run(MergePolicy.TakeTheirs), "outline"),
                Ui.Button("Keep both", () => Run(MergePolicy.KeepBoth), "outline"),
                Ui.Button("Cancel", Render, "outline"))), 34);
    }

    private void ShowRestore()
    {
        var backups = new Backups(_context.Settings.BackupFolder);
        var snapshots = backups.List();

        if (snapshots.Count == 0)
        {
            _context.Toast("There are no snapshots yet.");
            return;
        }

        var picker = new ComboBox
        {
            ItemsSource = snapshots.Select(Path.GetFileName).ToList(),
            SelectedIndex = 0,
            Width = 320,
        };

        _body.Content = Ui.Pad(Ui.Stack(18,
            Ui.Heading("RESTORE", "Replace the library from a snapshot.",
                "A snapshot of the current library is written first, so this is reversible."),
            picker,
            Ui.Row(12,
                Ui.Button("Cancel", Render, "outline"),
                Ui.Button("Restore", () =>
                {
                    if (picker.SelectedIndex < 0) return;
                    WriteBackup();
                    backups.Restore(_context.Library, snapshots[picker.SelectedIndex]);
                    _context.Toast("Library restored.");
                    Render();
                }, "primary"))), 34);
    }
}
```

- [ ] **Step 4: Run the checks to verify they pass**

Run: `dotnet run --project tools/Checks`
Expected: every check prints `ok`, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add Views/SettingsView.cs tools/Checks/Program.cs
git commit -m "Add settings with scheduler conversion, export, import and restore"
```

---

### Task 24: Command palette, capture hotkey and startup backup

**Files:**
- Create: `Shell/Capture.cs`
- Rewrite: `Shell/CommandPalette.cs` (replacing the Task 15 stub)
- Modify: `Shell/MainWindow.cs` (wire capture, keyboard shortcuts, startup backup)
- Test: `tests/FuzzyTests.cs`; check `CheckCommandPalette` in `tools/Checks/Program.cs`

**Interfaces:**
- Produces:
  - `Fuzzy.Score(string candidate, string query) -> int` in `Core/Fuzzy.cs` — higher is better, `-1` for no match
  - `PaletteEntry(string Label, string Caption, Action Run)`
  - `CommandPalette.Build(AppContext context, IReadOnlyList<NavItem> nav) -> IReadOnlyList<PaletteEntry>`
  - `CommandPalette.Filter(IReadOnlyList<PaletteEntry> entries, string query) -> IReadOnlyList<PaletteEntry>`
  - `CommandPalette.Show(Panel host, AppContext context, IReadOnlyList<NavItem> nav)`
  - `Capture(Window window, Settings settings, Action<string> onCapture)` with
    `.TryRegister() -> bool`, `.Dispose()`

`Capture` uses `RegisterHotKey`/`UnregisterHotKey` on Windows and is a no-op
elsewhere. Registration failure raises a toast and leaves capture disabled
rather than throwing.

- [ ] **Step 1: Write the failing test**

Create `tests/FuzzyTests.cs`:

```csharp
using SirWordALot.Core;
using Xunit;

namespace SirWordALot.Tests;

public class FuzzyTests
{
    [Fact]
    public void AnEmptyQueryMatchesEverything() => Assert.True(Fuzzy.Score("Library", "") >= 0);

    [Fact]
    public void AnExactPrefixScoresHigherThanAScatteredMatch() =>
        Assert.True(Fuzzy.Score("library", "lib") > Fuzzy.Score("loose bank", "lib"));

    [Fact]
    public void MatchingIsCaseInsensitive() => Assert.True(Fuzzy.Score("Library", "LIB") >= 0);

    [Fact]
    public void CharactersMustAppearInOrder()
    {
        Assert.True(Fuzzy.Score("library", "lry") >= 0);
        Assert.Equal(-1, Fuzzy.Score("library", "yrl"));
    }

    [Fact]
    public void AMissingCharacterFails() => Assert.Equal(-1, Fuzzy.Score("library", "libz"));

    [Fact]
    public void ConsecutiveMatchesBeatGappedOnes() =>
        Assert.True(Fuzzy.Score("sentences", "sent") > Fuzzy.Score("settings", "sent"));
}
```

Append to `tools/Checks/Program.cs` and call it from `Main`:

```csharp
    private static void CheckCommandPalette()
    {
        var db = Db.OpenMemory();
        var library = new Library(db);
        library.AddEntry("eminence");
        library.CreateDeck("Exam");

        var navigated = "";
        var context = new AppContext(library, new Settings(db), new Search(db), new Stats(db),
            new StubLookup(), _ => { }, (id, _) => navigated = id);

        var nav = new MainWindow(library, new Settings(db), new Search(db), new Stats(db), new StubLookup()).Nav;
        var entries = CommandPalette.Build(context, nav);

        Expect("palette has views", true, entries.Any(e => e.Label == "All words"));
        Expect("palette has entries", true, entries.Any(e => e.Label == "eminence"));
        Expect("palette has decks", true, entries.Any(e => e.Label == "Exam"));
        Expect("palette has verbs", true, entries.Any(e => e.Label.Contains("Export")));

        var filtered = CommandPalette.Filter(entries, "emin");
        Expect("filter narrows", "eminence", filtered[0].Label);

        filtered[0].Run();
        Expect("running navigates", "library", navigated);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter FuzzyTests; dotnet run --project tools/Checks`
Expected: FAIL — `Fuzzy` and `CommandPalette.Build` do not exist.

- [ ] **Step 3: Write the implementation**

Create `Core/Fuzzy.cs`:

```csharp
namespace SirWordALot.Core;

/// <summary>Subsequence matching with a bonus for consecutive characters and for
/// matches at the start of the candidate. Returns -1 when the query does not match.</summary>
public static class Fuzzy
{
    public static int Score(string candidate, string query)
    {
        if (query.Length == 0) return 0;

        var haystack = candidate.ToLowerInvariant();
        var needle = query.Trim().ToLowerInvariant();

        var score = 0;
        var cursor = 0;
        var previous = -2;

        foreach (var c in needle)
        {
            var index = haystack.IndexOf(c, cursor);
            if (index < 0) return -1;

            score += 10;
            if (index == previous + 1) score += 8;
            if (index == 0) score += 12;
            score -= Math.Min(6, index - cursor);

            previous = index;
            cursor = index + 1;
        }

        return score;
    }
}
```

Replace `Shell/CommandPalette.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SirWordALot.Core;

namespace SirWordALot.Shell;

public sealed record PaletteEntry(string Label, string Caption, Action Run);

public static class CommandPalette
{
    public static IReadOnlyList<PaletteEntry> Build(AppContext context, IReadOnlyList<NavItem> nav)
    {
        var entries = new List<PaletteEntry>();

        foreach (var item in nav)
            entries.Add(new PaletteEntry(TitleCase(item.Title), "View",
                () => context.Navigate(item.Id, null)));

        foreach (var entry in context.Library.AllEntries(includeArchived: true))
            entries.Add(new PaletteEntry(entry.Headword, entry.PartOfSpeech ?? "Word",
                () => context.Navigate("library", entry.Id)));

        foreach (var deck in context.Library.Decks())
            entries.Add(new PaletteEntry(deck.Name, "Deck", () => context.Navigate("decks", deck.Id)));

        foreach (var tag in context.Library.Tags())
            entries.Add(new PaletteEntry(tag.Name, "Tag", () => context.Navigate("library", tag.Name)));

        entries.Add(new PaletteEntry("Start a quiz", "Action", () => context.Navigate("quiz", null)));
        entries.Add(new PaletteEntry("Look up a word", "Action", () => context.Navigate("lookup", null)));
        entries.Add(new PaletteEntry("Export the library", "Action", () => context.Navigate("settings", null)));
        entries.Add(new PaletteEntry("Back up now", "Action", () =>
        {
            var backups = new Backups(context.Settings.BackupFolder);
            var path = backups.Write(context.Library, DateTime.UtcNow);
            backups.Prune();
            context.Toast($"Snapshot written to {path}.");
        }));

        return entries;
    }

    public static IReadOnlyList<PaletteEntry> Filter(IReadOnlyList<PaletteEntry> entries, string query) =>
        entries
            .Select(entry => (Entry: entry, Score: Fuzzy.Score(entry.Label, query)))
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Label.Length)
            .Select(x => x.Entry)
            .Take(40)
            .ToList();

    public static void Show(Panel host, AppContext context, IReadOnlyList<NavItem> nav)
    {
        var entries = Build(context, nav);
        var results = Ui.Stack(0);
        var visible = new List<PaletteEntry>();
        var query = new TextBox { Watermark = "Go to a word, a view or an action…", FontSize = 15 };

        var card = new Border
        {
            Width = 560,
            Background = Ui.Cream,
            BorderBrush = Ui.WindowBorder,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 120, 0, 0),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 14, Blur = 38, Color = Color.Parse("#45091619") }),
            Child = Ui.Rows("Auto,Auto,*",
                Ui.Pad(query, 16),
                Ui.Rule(),
                new ScrollViewer { Content = results, MaxHeight = 340 }),
        };

        var scrim = new Border
        {
            Background = Brush.Parse("#33091619"),
            Child = card,
        };

        void Close()
        {
            host.Children.Remove(scrim);
        }

        void Render()
        {
            results.Children.Clear();
            visible.Clear();
            visible.AddRange(Filter(entries, query.Text ?? ""));

            foreach (var entry in visible)
            {
                var button = Ui.Button("", () => { Close(); entry.Run(); });
                button.Content = Ui.Columns("*,Auto", Ui.Text(entry.Label, 13), Ui.Label(entry.Caption.ToUpperInvariant()));
                button.Padding = new Thickness(18, 11);
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                results.Children.Add(button);
            }

            if (results.Children.Count == 0)
                results.Children.Add(Ui.Pad(Ui.Text("Nothing matches.", 13, Ui.Muted), 20));
        }

        query.TextChanged += (_, _) => Render();
        query.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            if (e.Key == Key.Enter && visible.Count > 0)
            {
                var first = visible[0];
                Close();
                first.Run();
                e.Handled = true;
            }
        };
        scrim.PointerPressed += (_, e) => { if (ReferenceEquals(e.Source, scrim)) Close(); };

        Render();
        host.Children.Add(scrim);
        query.Focus();
    }

    private static string TitleCase(string caption) =>
        caption.Length == 0 ? caption : char.ToUpperInvariant(caption[0]) + caption[1..].ToLowerInvariant();
}
```

Create `Shell/Capture.cs`:

```csharp
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SirWordALot.Core;

namespace SirWordALot.Shell;

/// <summary>A system-wide hotkey that pulls the clipboard's word into the app.
/// Windows only; a no-op elsewhere, and never fatal when registration fails.</summary>
public sealed class Capture(Window window, Settings settings, Action<string> onCapture) : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xA17;

    private nint _handle;
    private bool _registered;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    public bool TryRegister()
    {
        if (!OperatingSystem.IsWindows() || !settings.CaptureEnabled) return false;
        if (window.TryGetPlatformHandle()?.Handle is not { } handle || handle == 0) return false;

        var (modifiers, key) = Parse(settings.CaptureHotkey);
        if (key == 0) return false;

        _handle = handle;
        _registered = RegisterHotKey(_handle, HotkeyId, modifiers, key);

        if (_registered) Win32Properties.AddWndProcHookCallback(window, Hook);
        return _registered;
    }

    private nint Hook(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmHotkey || wParam != HotkeyId) return 0;
        handled = true;
        Dispatcher.UIThread.Post(async () =>
        {
            var text = window.Clipboard is { } clipboard ? await clipboard.GetTextAsync() : null;
            var word = (text ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";
            if (word.Length > 0) onCapture(word);
        });
        return 0;
    }

    /// <summary>Parses strings like <c>Ctrl+Alt+W</c> into Win32 modifier and virtual-key codes.</summary>
    private static (uint Modifiers, uint Key) Parse(string hotkey)
    {
        uint modifiers = 0;
        uint key = 0;

        foreach (var part in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.Trim().ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= 0x0002; break;
                case "alt": modifiers |= 0x0001; break;
                case "shift": modifiers |= 0x0004; break;
                case "win": modifiers |= 0x0008; break;
                default:
                    var token = part.Trim().ToUpperInvariant();
                    if (token.Length == 1 && char.IsLetterOrDigit(token[0])) key = token[0];
                    break;
            }
        }

        return modifiers == 0 ? (0, 0) : (modifiers, key);
    }

    public void Dispose()
    {
        if (_registered && _handle != 0) UnregisterHotKey(_handle, HotkeyId);
        _registered = false;
    }
}
```

Wire it into `Shell/MainWindow.cs`. Add the fields and the `Opened` handler:

```csharp
    private Capture? _capture;

    // inside the constructor, after RestoreBounds():
        Opened += (_, _) =>
        {
            _capture = new Capture(this, _settings, CaptureWord);
            if (_settings.CaptureEnabled && !_capture.TryRegister())
                ShowToast($"The {_settings.CaptureHotkey} hotkey is taken by another program.");

            var backups = new Backups(_settings.BackupFolder);
            try { backups.Write(_library, DateTime.UtcNow); backups.Prune(); }
            catch (IOException) { /* a backup failure must never block startup */ }
        };
```

extend the existing `Closed` handler with `_capture?.Dispose();`, add the
capture handler, and extend `OnKeyDown`:

```csharp
    private void CaptureWord(string word)
    {
        Activate();
        var id = _library.AddEntry(word, "capture");
        ShowToast($"Captured '{word}'.");
        Navigate("lookup", word);
        _ = id;
    }

    // added to OnKeyDown, after the Ctrl+Space branch. A bare letter must never
    // steal a keystroke from a field the user is typing into, so the guard is the
    // focused element, not the hosted view.
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (FocusManager?.GetFocusedElement() is TextBox or ComboBox) return;

        switch (e.Key)
        {
            case Key.OemQuestion or Key.Divide:
                Navigate("library");
                (Host.Content as LibraryView)?.FocusSearch();
                e.Handled = true;
                break;
            case Key.N:
                Navigate("library");
                (Host.Content as LibraryView)?.BeginNewWord();
                e.Handled = true;
                break;
            case Key.Q:
                Navigate("quiz");
                e.Handled = true;
                break;
        }
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/SirWordALot.Tests.csproj --filter FuzzyTests; dotnet run --project tools/Checks`
Expected: 6 tests pass; checks exit 0.

- [ ] **Step 5: Commit**

```bash
git add Core/Fuzzy.cs Shell/CommandPalette.cs Shell/Capture.cs Shell/MainWindow.cs tests/FuzzyTests.cs tools/Checks/Program.cs
git commit -m "Add the command palette, global capture hotkey and startup backup"
```

---

### Task 25: Rendered previews and measured verification

**Files:**
- Create: `tools/Preview/Preview.csproj`, `tools/Preview/Program.cs`
- Create: `tools/Measure/Measure.csproj`, `tools/Measure/Program.cs`

**Interfaces:**
- Produces:
  - `tools/Preview` renders each view to `<out>/<view>.png` at 1180×760 using the
    production controls, seeded with a demo library so the images are not empty.
  - `tools/Measure` reads those PNGs and asserts the palette and geometry, exiting
    non-zero on a mismatch.

This is how the UI is verified. The live desktop is never driven.

- [ ] **Step 1: Write the failing measurement**

Create `tools/Measure/Measure.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>SirWordALot.Measure</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SkiaSharp" Version="3.116.1" />
  </ItemGroup>
</Project>
```

Create `tools/Measure/Program.cs`:

```csharp
using SkiaSharp;

namespace SirWordALot.Measure;

internal static class Program
{
    private static int _failures;

    public static int Main(string[] args)
    {
        var folder = args.FirstOrDefault() ?? "dist/preview";
        if (!Directory.Exists(folder))
        {
            Console.WriteLine($"No preview folder at {folder}. Run tools/Preview first.");
            return 1;
        }

        foreach (var view in new[] { "library", "sentences", "lookup", "quiz", "decks", "stats", "settings" })
            MeasureChrome(Path.Combine(folder, $"{view}.png"), view);

        MeasureConsole(Path.Combine(folder, "console.png"));

        Console.WriteLine(_failures == 0 ? "All measurements passed." : $"{_failures} measurement(s) failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static void MeasureChrome(string path, string view)
    {
        if (!File.Exists(path)) { Fail($"{view}: no image at {path}"); return; }

        using var bitmap = SKBitmap.Decode(path);
        if (bitmap is null) { Fail($"{view}: could not decode {path}"); return; }

        Expect($"{view} width", 1180, bitmap.Width);
        Expect($"{view} height", 760, bitmap.Height);

        // The 1px window border.
        Expect($"{view} border", "#9CAEA0", Hex(bitmap, 0, 380));

        // Titlebar body, sampled below the border and left of the glyph.
        Expect($"{view} titlebar", "#E9EBDD", Hex(bitmap, 6, 20));

        // The titlebar is 43px tall plus the 1px window border, so row 45 is
        // already the body and row 40 is still chrome.
        Expect($"{view} titlebar depth", "#E9EBDD", Hex(bitmap, 300, 40));

        // The sidebar runs from x=1 to x=170 inclusive of the window border.
        Expect($"{view} sidebar", "#DFE4D6", Hex(bitmap, 8, 400));

        // Just right of the sidebar rule the body is cream.
        Expect($"{view} body", "#ECEDE0", Hex(bitmap, 640, 400));

        // Footer body, 42px tall above the bottom border.
        Expect($"{view} footer", "#ECEDE0", Hex(bitmap, 600, 738));
    }

    private static void MeasureConsole(string path)
    {
        if (!File.Exists(path)) { Fail($"console: no image at {path}"); return; }

        using var bitmap = SKBitmap.Decode(path);
        if (bitmap is null) { Fail($"console: could not decode {path}"); return; }

        Expect("console surface", "#102C32", Hex(bitmap, 640, 300));
        Expect("console keeps the sidebar", "#DFE4D6", Hex(bitmap, 8, 400));
    }

    private static string Hex(SKBitmap bitmap, int x, int y)
    {
        var pixel = bitmap.GetPixel(x, y);
        return $"#{pixel.Red:X2}{pixel.Green:X2}{pixel.Blue:X2}";
    }

    private static void Expect<T>(string name, T expected, T actual)
    {
        if (Equals(expected, actual)) { Console.WriteLine($"  ok    {name}"); return; }
        Fail($"{name}: expected {expected}, got {actual}");
    }

    private static void Fail(string message)
    {
        Console.WriteLine($"  FAIL  {message}");
        _failures++;
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project tools/Measure -- dist/preview`
Expected: FAIL — there is no preview folder yet.

- [ ] **Step 3: Write the preview renderer**

Create `tools/Preview/Preview.csproj` — identical to `tools/Checks/Checks.csproj`
but with `<RootNamespace>SirWordALot.Preview</RootNamespace>` and an added
`<PackageReference Include="Avalonia.Skia" Version="11.3.0" />`.

Create `tools/Preview/Program.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using SirWordALot.Core;
using SirWordALot.Shell;

namespace SirWordALot.Preview;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var folder = args.FirstOrDefault() ?? "dist/preview";
        Directory.CreateDirectory(folder);

        AppBuilder.Configure<PreviewApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithoutStarting();

        var db = Db.OpenMemory();
        var library = Seed(db);
        var settings = new Settings(db);
        var window = new MainWindow(library, settings, new Search(db), new Stats(db), new StubLookup());
        window.Show();

        foreach (var view in new[]
        {
            "library", "sentences", "lookup", "quiz", "decks", "stats", "console", "settings",
        })
        {
            window.Navigate(view);
            Render(window, Path.Combine(folder, $"{view}.png"));
            Console.WriteLine($"  wrote {view}.png");
        }

        window.Close();
        Console.WriteLine($"Previews written to {Path.GetFullPath(folder)}.");
        return 0;
    }

    private static void Render(Window window, string path)
    {
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();

        var target = new RenderTargetBitmap(
            new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        target.Render(window);
        using var stream = File.Create(path);
        target.Save(stream);
    }

    private static Library Seed(Db db)
    {
        var library = new Library(db);
        var now = DateTime.UtcNow;

        var words = new (string Word, string Pos, string Definition, string Sentence, string[] Synonyms)[]
        {
            ("eminence", "noun", "A position of superiority or fame.",
             "She rose to eminence after the war.", ["prominence", "renown", "standing"]),
            ("laconic", "adjective", "Using very few words.",
             "His laconic reply ended the meeting.", ["terse", "curt", "concise"]),
            ("ersatz", "adjective", "An inferior substitute.",
             "They served an ersatz coffee of roasted barley.", ["artificial", "imitation"]),
            ("obdurate", "adjective", "Stubbornly refusing to change.",
             "The committee remained obdurate.", ["unyielding", "inflexible"]),
            ("pellucid", "adjective", "Translucently clear.",
             "A pellucid stream ran past the mill.", ["limpid", "lucid"]),
            ("quixotic", "adjective", "Idealistic to the point of impracticality.",
             "A quixotic plan to rebuild the pier.", ["idealistic", "romantic"]),
        };

        var deck = library.CreateDeck("Examination", "For Tuesday");

        for (var i = 0; i < words.Length; i++)
        {
            var (word, pos, definition, sentence, synonyms) = words[i];
            var id = library.AddEntry(word, "lookup");

            library.UpdateEntry(library.GetEntry(id)! with
            {
                PartOfSpeech = pos,
                Definition = definition,
                Pronunciation = "/ˈ" + word[..Math.Min(4, word.Length)] + "/",
                Starred = i % 3 == 0,
            });

            foreach (var synonym in synonyms) library.AddRelation(id, RelationKind.Synonym, synonym, "lookup");
            library.AddRelation(id, RelationKind.Antonym, i % 2 == 0 ? "obscurity" : "verbose", "lookup");

            var sentenceId = library.AddSentence(sentence, id, "Reading notes");
            if (Cloze.Find(sentence, word) is { } span)
                library.UpdateSentence(library.SentencesFor(id).Single(s => s.Id == sentenceId)
                    with { ClozeStart = span.Start, ClozeLength = span.Length });

            library.TagEntry(id, i % 2 == 0 ? "latinate" : "reading");
            if (i < 3) library.AddToDeck(deck, id);

            var interval = new[] { 1, 6, 15, 40, 95, 3 }[i];
            library.SaveScheduling(library.GetScheduling(id) with
            {
                IntervalDays = interval,
                Repetitions = i + 1,
                LastReviewedUtc = now.AddDays(-1),
                DueUtc = now.AddDays(i % 2 == 0 ? -1 : interval),
            });

            for (var r = 0; r < 4 + i; r++)
                library.AddReview(new Review(0, id, QuizMode.Flip, Grade.Good, r % 5 != 0, true,
                    1200, Math.Max(1, interval - r), interval, now.AddDays(-r % 30)));
        }

        library.AddSentence("A sentence found in the wild, not yet attached to anything.");
        return library;
    }

    private sealed class StubLookup : ILookup
    {
        public Task<LookupResult> LookupAsync(string word, CancellationToken ct) =>
            Task.FromResult(Lookup.Offline(word, "Previews are rendered offline."));
    }
}

internal sealed class PreviewApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://SirWordALot/App.axaml"))
        { Source = new Uri("avares://SirWordALot/App.axaml") });
    }
}
```

If the `StyleInclude` cannot resolve `App.axaml` from the preview assembly,
replace `PreviewApp.Initialize` with a direct construction of the styles by
copying the `<Style>` elements into a small `.axaml` file under
`tools/Preview/PreviewStyles.axaml` and including that instead. The check to
run is the measurement — if the palette measures correctly, the styles loaded.

- [ ] **Step 4: Render and measure**

Run:

```powershell
dotnet run --project tools/Preview -- dist/preview
dotnet run --project tools/Measure -- dist/preview
```

Expected: eight PNGs written; every measurement prints `ok`, exit code 0.
Open `dist/preview/library.png` and confirm by eye that it reads as a HammerOS
application.

- [ ] **Step 5: Commit**

```bash
git add tools/Preview tools/Measure
git commit -m "Add rendered previews and measured palette and geometry checks"
```

---

### Task 26: README, icon, help and the published build

**Files:**
- Create: `README.md`, `Assets/SirWordALot.ico`, `Assets/banner.png`
- Modify: `SirWordALot.csproj` (application icon)
- Modify: `.gitignore` (already correct; confirm `dist/` is ignored)

**Interfaces:**
- Produces: a self-contained single-file Windows build in `dist/SirWordALot`.

- [ ] **Step 1: Generate the banner and icon from a rendered preview**

The banner is the library preview, cropped and captioned. Write
`tools/Preview/Program.cs`'s `library.png` first, then:

```powershell
dotnet run --project tools/Preview -- dist/preview
Copy-Item dist/preview/library.png Assets/banner.png
```

For the icon, render a 256×256 PNG of the `word` glyph on the cream background
and convert it. Add this to `tools/Preview/Program.cs` before `window.Close()`:

```csharp
        RenderIcon(Path.Combine(folder, "icon.png"));
```

and the method:

```csharp
    private static void RenderIcon(string path)
    {
        var panel = new Border
        {
            Width = 256, Height = 256, Background = Ui.Cream,
            BorderBrush = Ui.WindowBorder, BorderThickness = new Avalonia.Thickness(6),
            Child = new Glyph("word") { Width = 150, Height = 150 },
        };
        panel.Measure(new Size(256, 256));
        panel.Arrange(new Rect(0, 0, 256, 256));

        var target = new RenderTargetBitmap(new PixelSize(256, 256), new Vector(96, 96));
        target.Render(panel);
        using var stream = File.Create(path);
        target.Save(stream);
    }
```

Convert `dist/preview/icon.png` to `Assets/SirWordALot.ico` with any converter,
then add to `SirWordALot.csproj`:

```xml
    <ApplicationIcon>Assets/SirWordALot.ico</ApplicationIcon>
```

- [ ] **Step 2: Write the README**

Create `README.md`:

```markdown
# Sir Word-a-lot

![Sir Word-a-lot](Assets/banner.png)

A vocabulary and thesaurus trainer in the visual idiom of
[HammerOS](../hammeros): cream institutional chrome, petrol surfaces, phosphor
typography, and an unreasonably formal tone about the business of remembering
words.

Capture a word with the sentence you met it in, enrich it with real synonyms and
examples, then meet it again on a schedule until it sticks. Clicking a synonym
you do not own yet offers to add it, so one word fans out into a network.

## Run

Open `dist/SirWordALot/SirWordALot.exe`. The published Windows x64 build is
self-contained.

From source, with the .NET 10 SDK:

```powershell
dotnet run --project SirWordALot.csproj
```

`--help` and `--version` print to the console.

## Inside

| View | What it does |
| --- | --- |
| Library | Search, filter and sort every word; edit definitions, relations, sentences, tags and notes |
| Sentences | The sentence bank; paste a sentence and it links itself to any word it contains, inflections included |
| Lookup | Synonyms, antonyms, related words and rhymes from Datamuse; senses, pronunciation and examples from dictionaryapi.dev. Tick what you want and add it |
| Quiz | Flip, multiple choice, cloze and typed drills over SM-2 or Leitner, with the resulting interval shown on every grade button |
| Decks | Manual decks, plus smart decks saved from a library filter and re-evaluated live |
| Stats | Streak, retention, a 53-week review heatmap, mastery breakdown and a retention curve |
| Console | A Department shell: `add`, `find`, `show`, `sentence`, `tag`, `quiz`, `due`, `stats`, `export`, `import`, `backup` |
| Settings | Palette, motion, scheduler, daily caps, capture hotkey, lookup switch, export, import and restore |

| Shortcut | Action |
| --- | --- |
| Ctrl+Space | Command palette over views, words, decks and actions |
| Ctrl+Alt+W | Capture the clipboard word from anywhere |
| / | Focus search |
| N | New word |
| Q | Start a quiz |
| Space | Flip the card |
| 1–4 | Grade Again, Hard, Good, Easy |
| Esc | Back, or end the session |

## Scheduling

SM-2 by default: ease starts at 2.5 and floors at 1.3; `Good` gives 1 day, then
6, then the interval times the ease; `Easy` adds 30% and raises the ease;
`Hard` stretches by 20% and lowers it; `Again` resets the count and records a
lapse. Intervals above two days get ±5% fuzz so cards do not clump, and are
capped at a year.

Leitner is available in Settings — five boxes at 1, 2, 4, 8 and 16 days.
Switching converts your existing progress rather than resetting it.

Free practice writes no scheduling at all, so drilling for fun never pollutes
your retention statistics.

## Data

Everything lives in `%APPDATA%\fezcode\SirWordALot`: `library.db` (SQLite, WAL)
and `backups\`. A timestamped JSON snapshot is written at launch and at most
hourly, and the newest 20 are kept.

JSON export is lossless — it round-trips relations, sentences with their cloze
spans, SRS state and full review history. CSV and Anki TSV are lossy exchange
formats.

The only network calls are to `api.datamuse.com` and `api.dictionaryapi.dev`,
and only when you use Lookup. Turning lookup off in Settings stops them
entirely. Nothing else leaves the machine.

## Build and verify

```powershell
dotnet build SirWordALot.csproj
dotnet test tests/SirWordALot.Tests.csproj
dotnet run --project tools/Checks
dotnet run --project tools/Preview -- dist/preview
dotnet run --project tools/Measure -- dist/preview
dotnet publish SirWordALot.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/SirWordALot
```

The unit tests cover scheduling, cloze matching, search, import and export
against in-memory SQLite. The checks exercise real Avalonia controls headlessly.
The previews render every view to PNG and the measurements assert the palette
and geometry of those images.

An independent project, unaffiliated with Apple or *Severance*. Dictionary data
from [dictionaryapi.dev](https://dictionaryapi.dev); thesaurus data from
[Datamuse](https://datamuse.com/api/).
```

- [ ] **Step 3: Publish and verify the real binary**

Run:

```powershell
dotnet publish SirWordALot.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/SirWordALot
./dist/SirWordALot/SirWordALot.exe --version
./dist/SirWordALot/SirWordALot.exe --help
```

Expected: `Sir Word-a-lot 0.1.0`, then the help text. The published run matters
because `Microsoft.Data.Sqlite` carries a native dependency that `dotnet run`
resolves differently from a single-file bundle.

- [ ] **Step 4: Run everything once more**

```powershell
dotnet test tests/SirWordALot.Tests.csproj
dotnet run --project tools/Checks
dotnet run --project tools/Preview -- dist/preview
dotnet run --project tools/Measure -- dist/preview
```

Expected: all green. Report the actual counts, not a claim.

- [ ] **Step 5: Commit**

```bash
git add README.md Assets SirWordALot.csproj
git commit -m "Add README, icon and banner"
```

---

## Verification summary

| Layer | Command | Covers |
| --- | --- | --- |
| Core logic | `dotnet test tests/SirWordALot.Tests.csproj` | schema, CRUD, search, SM-2 and Leitner, mastery, cloze, sessions, stats, settings, import/export, backups, lookup parsing, console commands, fuzzy matching |
| Controls | `dotnet run --project tools/Checks` | palette, typography, factories, window geometry, every view's behaviour through real Avalonia controls |
| Pixels | `dotnet run --project tools/Preview -- dist/preview` then `dotnet run --project tools/Measure -- dist/preview` | rendered palette and chrome geometry measured from the PNGs |
| Shipping | `dotnet publish …` then `--version` / `--help` | the native SQLite dependency inside a single-file bundle |
