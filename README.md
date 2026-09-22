# Sir Word-a-lot

![Sir Word-a-lot](Assets/banner.png)

A vocabulary and thesaurus trainer in the visual idiom of **HammerOS**: cream
institutional chrome, petrol surfaces, phosphor typography, and an unreasonably
formal tone about the business of remembering words.

Capture a word with the sentence you met it in, enrich it with real synonyms and
examples, then meet it again on a schedule until it sticks. Clicking a synonym
you do not own yet offers to add it, so one word fans out into a network.

## Run

Open `dist/SirWordALot/SirWordALot.exe`. The published Windows x64 build is
self-contained; keep the executable wherever you prefer.

From source, with the .NET 10 SDK:

```powershell
dotnet run --project SirWordALot.csproj
```

`--help` and `--version` print to the console and exit.

## Inside

| View | What it does |
| --- | --- |
| **Library** | Search, filter and sort every word; edit headword, pronunciation, part of speech, definition, relations, sentences, tags and notes |
| **Sentences** | The sentence bank. Paste a sentence and it links itself to any word it contains, inflections included — `obfuscates` finds `obfuscate` |
| **Lookup** | Synonyms, antonyms, related words and rhymes from Datamuse; senses, pronunciation, audio and real examples from dictionaryapi.dev. Tick exactly what you want and add it. Turkish meanings come from the bundled offline dictionary and need no connection |
| **Quiz** | Flip, multiple choice, cloze and typed drills over SM-2 or Leitner, with the resulting interval printed on every grade button |
| **Decks** | Manual decks, plus smart decks saved from a library filter and re-evaluated live |
| **Stats** | Streak, retention, a 53-week review heatmap, mastery breakdown and a retention curve |
| **Settings** | Palette, motion, scheduler, daily caps, capture hotkey, lookup switch, export, import and restore |

| Shortcut | Action |
| --- | --- |
| `Ctrl+Space` | Command palette over views, words, decks, tags and actions |
| `Ctrl+Alt+W` | Capture the clipboard word from anywhere |
| `/` | Focus search |
| `N` | New word |
| `Q` | Start a quiz |
| `Space` | Flip the card |
| `Enter` | Confirm the suggested grade |
| `1`–`4` | Grade Again, Hard, Good, Easy |
| `Esc` | Back, or end the session |

## Scheduling

SM-2 by default. Ease starts at 2.5 and floors at 1.3. `Good` gives 1 day, then
6, then the interval times the ease. `Easy` adds 30% and raises the ease. `Hard`
stretches by 20% and lowers it. `Again` resets the count, records a lapse and
returns the card later in the same session. Intervals above two days get ±5%
fuzz so cards do not clump, and are capped at a year.

Leitner is available in Settings — five boxes at 1, 2, 4, 8 and 16 days, where
`Again` returns to box 1, `Hard` holds and `Good`/`Easy` promote. Switching
scheduler converts your existing progress and keeps your due dates rather than
resetting you.

**Auto-graded modes suggest, they do not record.** Answering a choice, cloze or
typed card reveals the answer with the computed grade preselected; the review is
written when you confirm it. Recording before you have seen whether you were
right would make the override meaningless.

**Free practice** writes no scheduling at all, so drilling for fun never
pollutes your retention statistics.

## The offline dictionary

31,835 English headwords with Turkish meanings ship inside the binary, exported
from [atlas.dict](../atlas.dict) by `scripts/export-swl` in that repository. They
power the `TRANSLATION` field's **Use atlas.dict** button, the Turkish section in
Lookup, and the "did you mean" suggestions when a word is not found — all with no
network at all.

A caveat worth stating plainly: `atlas.dict`'s `en-tr` data is a *translation*
dictionary. Across its 34,406 headwords and 74,656 senses it carries 7 English
glosses, 6 examples and 7 synonyms. So it supplies meanings in Turkish, not
English definitions, phonetics or parts of speech — those still come from the
online services or from you.

To refresh the asset after atlas.dict changes:

```powershell
cd ..\atlas.dict
go run ./scripts/export-swl -out ..\SirWordALot\Assets\dictionary.json
python -c "import gzip;open('../SirWordALot/Assets/dictionary.json.gz','wb').write(gzip.compress(open('../SirWordALot/Assets/dictionary.json','rb').read(),9))"
```

## Data

Everything lives in `%APPDATA%\fezcode\SirWordALot`: `library.db` (SQLite, WAL)
and `backups\`. A timestamped JSON snapshot is written at launch and at most
hourly, and the newest 20 are kept.

JSON export is lossless — it round-trips relations, sentences with their cloze
spans, SRS state and the full review history, so export then import reproduces
the library exactly. CSV and Anki TSV are lossy exchange formats.

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

Four layers, none of which drive the live desktop:

- **Unit tests** cover the schema and migrations, CRUD, the SM-2 and Leitner
  transition tables, mastery, inflection-aware cloze matching, edit-distance
  grading, queue construction and daily caps, full-text search, JSON/CSV/TSV
  round-trips, merge conflicts, backups and lookup parsing — against in-memory
  SQLite.
- **Checks** exercise real Avalonia controls headlessly: the palette,
  typography, window geometry and every view's behaviour.
- **Preview** renders all nine screens to PNG using the production controls, and
  **Measure** reads those PNGs back and asserts the palette and chrome geometry
  in the pixels. It also asserts each view actually draws, which is what caught
  a bug where every preview rendered fully transparent.
- **Publish** then running `--version` matters because `Microsoft.Data.Sqlite`
  carries a native dependency that a single-file bundle resolves differently
  from `dotnet run`.

## Structure

- `Core/` — SQLite, scheduling, cloze, search, sessions, stats, portability,
  lookup. No Avalonia reference; a test asserts that stays true.
- `Shell/` — the frameless window, palette, control factories, motion, command
  palette, capture hotkey.
- `Views/` — one file per screen.
- `tools/` — headless checks, preview renderer, pixel measurements.

An independent project, unaffiliated with Apple or *Severance*. Dictionary data
from [dictionaryapi.dev](https://dictionaryapi.dev); thesaurus data from
[Datamuse](https://www.datamuse.com/api/).
