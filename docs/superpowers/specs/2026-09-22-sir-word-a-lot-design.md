# Sir Word-a-lot — Design

A windowed vocabulary and thesaurus trainer in the HammerOS visual idiom.
Capture words and sentences, enrich them from free dictionary and thesaurus
services, organise them, and drill them with spaced repetition.

- **Date:** 2026-09-22
- **Stack:** .NET 10, Avalonia 11.3, SQLite (`Microsoft.Data.Sqlite`)
- **Location:** `D:\Workhammer\SirWordALot`
- **Data root:** `%APPDATA%\fezcode\SirWordALot`

## 1. Purpose and scope

The app exists to grow an active vocabulary. The core loop is: meet a word,
capture it with the sentence it appeared in, enrich it with synonyms and real
examples, then meet it again on a schedule until it sticks. Clicking a synonym
you do not own yet offers to add it, so one word fans out into a network.

In scope: local library with full CRUD, a loose sentence bank, online lookup
that degrades cleanly offline, four quiz modes over two schedulers, tags and
decks, full-text search, import/export in three formats, automatic backups,
a command palette, a global capture hotkey, an in-app console, and a stats
dashboard.

Out of scope for this build: installers and release automation, sync between
machines, mobile, multi-user, and any language other than the interface
language of the source data.

## 2. Visual system

Copied from `D:\Workhammer\hammeros` so the app reads as one HammerOS
application lifted out of the desktop.

| Token | Value | Use |
| --- | --- | --- |
| Ink | `#173B40` | primary text |
| Cream | `#ECEDE0` | window body |
| Muted | `#688178` | secondary text, mono labels |
| Line | `#BCC8BB` | rules, borders |
| Phosphor | `#B3E5DA` | console text, heatmap |
| Dark | `#102C32` | console surface |
| Titlebar active | `#E9EBDD` | focused window chrome |
| Titlebar inactive | `#CCD6C9` | unfocused window chrome |
| Sidebar | `#DFE4D6` | navigation column |
| Window border | `#9CAEA0` | 1px outer border |
| Row rule | `#D8DECF` | list separators |
| Primary button | `#214B4D` bg, `#F0F0DF` fg | affirmative actions |
| Danger text | `#99513D` | validation errors |

Typography: **Inter** for interface text (embedded via
`Avalonia.Fonts.Inter`), **Cascadia Mono** falling back to Consolas for
labels, data and the console, **Georgia** falling back to Times New Roman for
display headwords.

Conventions, all inherited: zero corner radius everywhere; uppercase mono
labels at 10.5px with 1.2 letter-spacing; 1px rules instead of boxes; a 43px
titlebar; a `◢` resize grip; eased motion with a cubic ease-out, globally
disableable.

Window: `SystemDecorations=None`, custom chrome, drag by titlebar,
double-click to maximise, corner grip to resize, Windows snap preserved.
Default 1180x760, minimum 1040x680. Position and size persist.

The three palettes from HammerOS Settings (Petrol, Evergreen, Midnight) tint
accents, the console and the heatmap only. The window body stays cream in all
three; this is a light-chrome application, not a theme switcher.

## 3. Architecture

```
SirWordALot/
  SirWordALot.csproj  App.axaml  App.cs  Program.cs  app.manifest
  Assets/             icon, banner
  Shell/
    Ui.cs             palette, typography, control factories, Glyph, Motion
    MainWindow.cs     frameless window, chrome, sidebar, status footer
    CommandPalette.cs Ctrl+Space fuzzy palette
    Toast.cs          transient notifications
  Core/
    Db.cs             connection, schema, migrations
    Library.cs        CRUD over entries, sentences, relations, tags, decks
    Search.cs         FTS5 queries and structured filters
    Scheduler.cs      SM-2 and Leitner, pure functions
    ReviewSession.cs  queue building, answer recording
    Stats.cs          aggregate queries
    Lookup.cs         Datamuse + dictionaryapi.dev, offline tolerant
    Cloze.cs          inflection-aware blanking, Levenshtein
    Capture.cs        global hotkey, clipboard
    Portability.cs    JSON/CSV/TSV import and export, backups
    Settings.cs       key/value settings over SQLite
  Views/
    LibraryView  EntryView  SentencesView  LookupView
    QuizView  DecksView  StatsView  ConsoleView  SettingsView
  tools/
    Checks/     headless Avalonia assertions
    Preview/    renders each view to PNG
  tests/        xunit over Core against in-memory SQLite
  docs/
```

`Core/` never references Avalonia. Every rule that matters — scheduling,
grading, cloze, merge, search — is a pure function or a SQL query, testable
without a window. `Views/` compose factories from `Shell/Ui.cs` exactly the
way `hammeros/Apps/FilesView.cs` composes them, building controls in C#
rather than XAML.

`Library` raises a `Changed` event; views subscribe and re-render, mirroring
`SystemState.Changed` in HammerOS. Views implement `IDisposable` to
unsubscribe.

## 4. Data model

SQLite at `%APPDATA%\fezcode\SirWordALot\library.db`, WAL mode, foreign keys
on. Schema version stored in `PRAGMA user_version`; `Db.cs` applies forward
migrations on open.

### entries

| Column | Type | Notes |
| --- | --- | --- |
| `id` | INTEGER PK | |
| `headword` | TEXT NOT NULL | as typed |
| `normalized` | TEXT NOT NULL UNIQUE | lowercased, trimmed, collapsed whitespace; duplicate key |
| `pronunciation` | TEXT | IPA |
| `audio_url` | TEXT | from dictionaryapi.dev, played on demand |
| `part_of_speech` | TEXT | |
| `definition` | TEXT | |
| `notes` | TEXT | |
| `starred` | INTEGER NOT NULL DEFAULT 0 | |
| `archived` | INTEGER NOT NULL DEFAULT 0 | |
| `source` | TEXT NOT NULL | `manual` / `lookup` / `import` / `capture` |
| `created_utc` | TEXT NOT NULL | ISO 8601 |
| `modified_utc` | TEXT NOT NULL | ISO 8601 |

### relations

`id`, `entry_id` (FK cascade), `kind` (`synonym` / `antonym` / `related` /
`rhyme` / `form`), `word`, `source`. Unique on `(entry_id, kind, word)`.

### sentences

`id`, `entry_id` (FK cascade, **nullable**), `text`, `source`, `cloze_start`,
`cloze_length`, `created_utc`. A null `entry_id` is a loose sentence in the
sentence bank, attachable later.

### tags / entry_tags

`tags(id, name UNIQUE)`; `entry_tags(entry_id, tag_id)` with a composite
primary key, both FKs cascading.

### decks / deck_entries

`decks(id, name UNIQUE, description, filter_json, created_utc)`. A non-null
`filter_json` makes it a smart deck evaluated live; `deck_entries(deck_id,
entry_id, position)` holds manual membership.

### scheduling

One row per entry, created when the entry is created.

`entry_id` PK FK, `scheduler` (`sm2` / `leitner`), `box`, `ease` REAL
DEFAULT 2.5, `interval_days`, `repetitions`, `lapses`, `due_utc`,
`last_reviewed_utc`.

### reviews

Append-only, never pruned. `id`, `entry_id`, `mode` (`flip` / `choice` /
`cloze` / `typed`), `grade` (0 Again, 1 Hard, 2 Good, 3 Easy), `correct`,
`scheduled` (0 for free practice), `elapsed_ms`, `interval_before`,
`interval_after`, `reviewed_utc`.

### settings

`key` PK, `value`.

### Full-text search

`entries_fts` over `(headword, definition, notes)` and `sentences_fts` over
`(text, source)`, both FTS5 external-content tables kept current by insert,
update and delete triggers. Search unions both and returns entries, with
sentence hits attributed to their linked entry.

### Modelling decisions

**Scheduling is per entry, not per (entry, mode).** Anki gives each card type
independent state; that would multiply the daily load by four for the same
vocabulary. One state per word, the session chooses the mode. The cost is that
the app cannot distinguish recognition strength from recall strength.

**`reviews` is append-only.** Stats are `GROUP BY` queries over it. Growth is
a few hundred kilobytes per year.

## 5. Views

### Chrome

43px titlebar: `▤ SIR WORD-A-LOT` in uppercase mono, then minimize, maximize
and close glyph buttons. 170px sidebar on `#DFE4D6` with a 1px right rule:
LIBRARY (All words, Due today, Starred, Recent, Archived), rule, SENTENCES,
LOOKUP, QUIZ, DECKS, STATS, rule, CONSOLE, SETTINGS. 42px status footer with
a top rule: `SWL / <VIEW>` at the left, `<n> WORDS · <n> DUE · ONLINE|OFFLINE`
and the `◢` grip at the right.

### Library

Three columns: sidebar, entry list, detail. The list has a search box
(watermark `Search words and sentences…`), a sort control (recent, alphabetic,
due, mastery), a filter row (part of speech, tag, mastery bucket), a
`Save as deck` action and a `+ Word` button. Due state and archived state come
from the sidebar preset rather than the filter row, so the two cannot disagree.
`SearchFilter` also carries `AddedAfter`, used by smart decks. Rows show the
headword in Inter 13, part of speech in muted mono, and a thin mastery bar,
separated by `#D8DECF` rules.

### Entry detail

Eyebrow `ENTRY / 0X 4A 92` — a stable hex derived from the id — with
`DUE IN 3 D · MASTERY 62%` at the right. Headword in Georgia 32. IPA and part
of speech in muted mono with a `▸` button when `audio_url` is set; the button
hands the URL to the operating system's default handler rather than decoding
audio in-process, and only `http`/`https` URLs are accepted. Then
DEFINITION, SYNONYMS / ANTONYMS / RELATED as clickable chips, EXAMPLES with
per-sentence edit, delete and set-cloze controls, TAGS, NOTES. Footer: Save,
Star, Archive, Delete, Quiz this word.

Clicking a relation chip searches the library. If the word is absent the chip
offers `+ add as new entry`, which creates it and runs lookup.

Delete uses the inline confirm-panel pattern from `FilesView.Create`: the
detail pane is replaced by a confirmation, not a modal dialog.

### Sentences

The sentence bank: every sentence with its linked word or `—`, source and
date. Search and filter by linked/loose. Paste a sentence and the app scans it
for headwords already in the library (inflection-aware, via `Cloze.cs`) and
offers to link. Edit, delete, link and unlink.

### Lookup

A search box, then the headword in Georgia and tickable chips grouped as
synonyms, antonyms, related and rhymes from Datamuse, plus senses, parts of
speech, pronunciation, audio and real example sentences from dictionaryapi.dev.
`Add to library` builds an entry from exactly what is ticked. If the entry
already exists, the button becomes `Merge into existing`.

Offline, or with lookup disabled in Settings, the panel shows
`LOOKUP UNAVAILABLE · OFFLINE` in muted mono and presents the manual add form
instead. Network calls have a 6 second timeout, are never retried
automatically, and never block the UI thread.

### Quiz

Mode picker (Flip, Choice, Cloze, Typed, Mixed) and scope picker (due queue,
deck, tag, starred, all, free practice), then the card surface. A seventh scope,
a single word, is reachable only from an entry's `Quiz this word` button and
from a deck's quiz action. Grade buttons
show the interval each would produce (`2 d`, `6 d`). Session header shows
`03 / 12`, elapsed time and streak. `Esc` ends with a summary card.

Modes that cannot run are disabled with a mono hint: `NEEDS 4+ ENTRIES` for
Choice, `NO EXAMPLE SENTENCE` for Cloze.

### Decks

List, create, rename and delete decks. Manual add and remove. A
`Save this filter as a deck` action on the Library view creates a smart deck
from the active filter.

### Stats

Stat tiles in the Macrodata Refinement idiom (`01` over `32%` with a rule):
total words, due today, mastered, current streak, retention, reviews today. A
53-week review heatmap in phosphor tints. A mastery breakdown bar across New,
Learning, Familiar, Strong, Mastered. A retention curve of correct-rate by
interval bucket.

### Console

A `#102C32` surface with phosphor text and a `swl:~ $` prompt. Commands
operate on the real library: `add <word>`, `find <query>`, `show <word>`,
`sentence <word> <text>`, `tag <word> <tag>`, `untag`, `quiz [mode]`, `due`,
`stats`, `export <json|csv|tsv> <path>`, `import <path>`, `backup`, `help`,
`clear`. Command history with arrow keys.

### Settings

Palette, smooth motion, scheduler (SM-2 or Leitner), daily review goal, new
cards per day, capture hotkey, lookup enabled, backup folder, export and
import buttons, restore from backup, data location with a reveal-in-explorer
action, version and about.

### Cross-cutting

`Ctrl+Space` opens a fuzzy command palette over views, entries, decks, tags
and verbs (`add word…`, `start quiz`, `export CSV`). `Ctrl+Alt+W` (rebindable)
captures the clipboard into a prefilled add-word flyout with lookup already
running. Toasts appear bottom-right. Keyboard: `/` focuses search, `N` new
word, `Q` quiz, `Space` flips, `1`–`4` grade, `Esc` goes back.

## 6. Scheduling and grading

### SM-2 (default)

Ease starts at 2.5 and floors at 1.3.

| Grade | Interval | Ease | Other |
| --- | --- | --- | --- |
| Again (0) | 0, requeued this session | −0.20 | reps → 0, lapses +1 |
| Hard (1) | `max(1, interval × 1.2)` | −0.15 | reps +1 |
| Good (2) | 1 d, then 6 d, then `interval × ease` | unchanged | reps +1 |
| Easy (3) | 4 d, then `interval × ease × 1.3` | +0.15 | reps +1 |

Intervals above 2 days get ±5% fuzz so cards do not clump. Intervals are
capped at 365 days. New cards per day default to 20.

### Leitner

Five boxes at 1, 2, 4, 8 and 16 days. `Again` returns the card to box 1, `Hard`
holds it where it is, and `Good` and `Easy` promote it. Switching scheduler in
Settings seeds the other's fields from the current interval and keeps the
existing due date rather than resetting progress.

### Free practice

Writes no scheduling. Reviews are logged with `scheduled = 0` and excluded
from retention and streak statistics.

### Mastery

Derived, never stored: current interval capped at 60 days, scaled by that
entry's lifetime accuracy. Buckets: New (no reviews), Learning (< 7 d),
Familiar (7–29 d), Strong (30–89 d), Mastered (≥ 90 d).

### Modes

- **Flip** — headword in Georgia 44, then definition, part of speech,
  synonyms and one example. Self-graded.
- **Choice** — prompt is the definition; four options, distractors preferring
  the same part of speech, then random. Correct suggests Good, incorrect
  suggests Again.
- **Cloze** — an example sentence with the word blanked to `▁` characters
  matching its length, using the stored cloze span or one computed by
  `Cloze.cs`. Typed; edit distance ≤ 1 suggests Hard.
- **Typed** — definition shown, type the headword. Exact suggests Good; edit
  distance ≤ 2 suggests Hard; otherwise Again.

**Auto-graded modes suggest, they do not record.** Answering a Choice, Cloze or
Typed card reveals the answer with the computed grade preselected as the
primary button, confirmed with `Enter` or a click. The review row is written on
confirmation, not on submission. Recording before you have seen whether you
were right would make the override meaningless, and the grade is a judgement
about your memory that only you can make once the answer is in front of you.

`Cloze.cs` matches inflected forms: `-s`, `-es`, `-ed`, `-ing`, `-d`,
`-ied → y`, and final-consonant doubling.

### Queue and session

Due entries ordered by `due_utc` ascending, then new entries up to the daily
cap, shuffled within each priority band. Mixed mode picks per card: Cloze when
a sentence exists, Choice when at least four entries exist, otherwise Flip.
Cards graded Again return at the end of the session.

**Every answer writes its review row and scheduling update immediately.**
Ending a session early, or crashing, loses nothing.

## 7. Import, export and backup

**JSON** is the lossless format and the backup format:

```json
{
  "version": 1,
  "exported_utc": "2026-09-22T10:00:00Z",
  "entries": [{
    "headword": "eminence",
    "pronunciation": "/ˈɛmɪnəns/",
    "part_of_speech": "noun",
    "definition": "...",
    "notes": "", "starred": true, "archived": false,
    "tags": ["latinate"],
    "relations": { "synonym": ["prominence"], "antonym": [] },
    "sentences": [{ "text": "...", "source": "...", "cloze": [12, 8] }],
    "scheduling": { "scheduler": "sm2", "ease": 2.5, "interval_days": 6,
                    "repetitions": 2, "lapses": 0, "due_utc": "..." },
    "reviews": [{ "mode": "cloze", "grade": 2, "correct": true,
                  "reviewed_utc": "..." }]
  }],
  "decks": [{ "name": "...", "description": "", "filter_json": null,
              "entries": ["eminence"] }]
}
```

Export then import reproduces the library exactly, including SRS state and
history.

**CSV** is one flat row per entry with relations semicolon-joined; lossy by
design, for spreadsheets. **Anki TSV** is `front⇥back⇥tags`.

Import merges on `normalized` with a keep-mine / take-theirs / keep-both
prompt shown once with an `apply to all` option. CSV and TSV import present a
column mapping picker.

Backups are timestamped JSON snapshots in
`%APPDATA%\fezcode\SirWordALot\backups\`, written on launch and at most once
per hour when the library is dirty, retaining the last 20. Restore is
available in Settings and replaces the library after writing a pre-restore
snapshot.

All writes to disk outside SQLite are atomic: write to a temporary file in the
destination directory, then rename over the target.

## 8. External services

| Service | Use | Key |
| --- | --- | --- |
| Datamuse (`api.datamuse.com`) | synonyms, antonyms, related, rhymes | none |
| dictionaryapi.dev | senses, part of speech, IPA, audio, examples | none |

Both are free and require no key. Lookup is the only network activity; nothing
else leaves the machine. A failure of either degrades to the offline panel
without an error dialog. Lookup can be disabled entirely in Settings, after
which the app makes no network calls at all.

## 9. Testing

`Core/` has no Avalonia dependency, so xunit tests run against in-memory
SQLite covering: SM-2 and Leitner transition tables including ease floor and
interval cap, fuzz bounds, cloze inflection matching, Levenshtein grading
thresholds, queue construction and the new-card cap, mastery buckets, JSON
round-trip fidelity, CSV and TSV shape, import merge conflict resolution, FTS
search including sentence-hit attribution, duplicate detection and merge, and
schema migration from each prior version.

UI is verified headlessly, never by driving the desktop. `tools/Checks`
exercises real Avalonia controls and synthetic pointer input in a headless
platform, as `hammeros/tools/Checks` does. `tools/Preview` renders each view
to PNG; the rendered images are measured programmatically to confirm the
palette, the 43px titlebar height, mono letter-spacing and column layout.

Development follows TDD: a failing test, then the implementation.

## 10. Build and verification

```powershell
dotnet build SirWordALot.csproj
dotnet test tests/SirWordALot.Tests.csproj
dotnet run --project tools/Checks
dotnet run --project tools/Preview -- dist/preview
dotnet publish SirWordALot.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/SirWordALot
```

`-h` / `--help` and `-v` / `--version` are supported on the command line and
print to the console.

The repository is initialised with a `.gitignore` covering `bin/`, `obj/`,
`dist/`, `*.user` and `.vs/`, and a README with a banner image.

Installer, Forge integration and release automation are deliberately out of
scope until the app has been used.

## 11. Risks

- **Frameless window chrome** must handle Windows snap, multi-monitor DPI
  changes and maximise-to-work-area correctly. Mitigated by extending
  `AppFrame`'s proven logic to a real `Window` and asserting geometry in
  `tools/Checks`.
- **Global hotkey registration** can collide with another application.
  Registration failure is reported as a toast and the hotkey is disabled
  rather than failing silently.
- **`Microsoft.Data.Sqlite`** carries a native dependency; verified by running
  the published single-file build, not only `dotnet run`.
- **dictionaryapi.dev availability** is uneven. Treated as best-effort;
  Datamuse alone is enough to populate relations.
