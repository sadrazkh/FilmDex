# FilmDex — remember every file name you have, so you never download the same film twice

**Date:** 2026-09-10 · **Status:** design approved by the owner, ready for an implementation plan

The owner's words, which are the requirements:

> «یه برنامه میخواهم بهش پوشه هارو بدم و اسم فایلارو نگه داره بعد یه بخش داشته باشه اسم رو بزنم و
> بگرده ببینه شبیه این اسم هست یا نه دقیق مشابه نمیخواهم درصد شباهت کافیه خودم جفتشو نشون بده برام
> کافیه قرار نیست مادام العمر به فولدر دسترسی داشته باشه پس باید یدور بخونه و اسمارو نگه داره تو
> حافظش قراره بعدا براساس این دسته بندی اینا هم بکنیم ولی فعلا فقط سرچ میخواهم فیلم دانلود میکنم
> مطمئن باشم تکراری نیست»

Give it folders. It reads them **once** and keeps the names. Later — with the drive unplugged — type
a name and it shows what looks like it, with a similarity percentage. Not exact matching. Showing
both names side by side is enough. Categorisation comes later; today it is search only. The purpose
is to be sure a film about to be downloaded is not already on a disk somewhere.

---

## 1. Decisions taken in brainstorming

| Question | Answer |
|---|---|
| Shape of the program | Windows desktop app, WPF |
| What gets indexed | Video files, plus the names of folders that contain films |
| Matching logic | **Both** a smart film-aware score and a raw-string score, shown as two separate columns |

Everything below follows from those three.

## 2. Scope

**In scope:** adding folders as libraries, one-off recursive scans, persisting names offline,
re-scanning on demand, fuzzy search with a similarity percentage, showing the query and the match
together.

**Out of scope for this build:** categorisation (the database reserves a column and nothing else),
live filesystem watching, online metadata lookup (TMDb and friends), duplicate detection by file
hash or size, anything multi-user.

## 3. Architecture

Three projects on .NET 10 (SDK 10.0.401 is installed on this machine):

| Project | Target | Contains |
|---|---|---|
| `FilmDex.Core` | `net10.0` | Name parsing, scoring, in-memory index, SQLite store, folder scanner. **No WPF reference** — this is what the tests exercise. |
| `FilmDex.App` | `net10.0-windows`, `UseWPF` | Windows, view models, nothing else. |
| `FilmDex.Core.Tests` | `net10.0` | xUnit over `FilmDex.Core`. |

Packages: `Microsoft.Data.Sqlite` (store), `CommunityToolkit.Mvvm` (source-generated properties and
commands in the app), `xunit` + `xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk` (tests).
Restore must be proven to work before any other implementation step — an offline NuGet cache would
change the plan.

### Why SQLite plus an in-memory index

Three storage shapes were considered:

- **SQLite for durability, an in-memory index for querying (chosen).** Names are written once per
  scan and loaded into memory at startup. Search then touches no disk at all. At 100,000 entries
  this stays under 100 ms per keystroke, which is what makes search-as-you-type possible.
- **SQLite FTS5 or a trigram index.** Less code, but it does not yield a real similarity
  percentage, and expressing two different scores in SQL gets ugly.
- **A JSON file.** Fine for 500 names; for a film archive of tens of thousands, every scan rewrites
  the whole file.

## 4. Core components

### 4.1 `NameNormalizer`

`Parse(string rawName, bool isFolder)` → `ParsedName { Raw, CleanTitle, Year?, Season?, Episode?, Tags }`.

Pipeline, in order:

1. For files, strip the extension.
2. Unicode fold for Persian/Arabic text: `ي`→`ی`, `ك`→`ک`, `ة`→`ه`, `أ إ آ`→`ا`, `ؤ`→`و`;
   Arabic-Indic (`٠`–`٩`) and Persian (`۰`–`۹`) digits → ASCII; zero-width non-joiner (U+200C) →
   space; strip combining marks.
3. Lowercase, invariant culture.
4. `.` `_` `-` `+` `(` `)` `[` `]` `{` `}` `'` `,` → space; collapse runs of whitespace.
5. **Year:** the last standalone `(19|20)\d{2}` token. Recorded, and everything from that token
   onward is dropped — scene names put the title first and the junk after.
6. **Season/episode:** `s01e02`, `1x02`, `season 1`, `part 2`. Recorded and removed.
7. **Release tags:** remove tokens from a static set — resolutions (`480p 720p 1080p 2160p 4k uhd`),
   sources (`bluray brrip bdrip webrip web dl hdtv dvdrip hdrip remux cam ts tc`), codecs
   (`x264 x265 h264 h265 hevc avc xvid divx 10bit hi10p`), audio (`aac ac3 dts ddp eac3 truehd
   atmos mp3 flac`), edition words (`proper repack extended unrated uncut directors cut limited
   internal imax 3d multi dual`), and the Persian ones the owner's files actually carry
   (`دوبله فارسی زیرنویس دوبله‌فارسی softsub hardsub dubfa`). A trailing `-group` token goes too.
8. Standalone roman numerals `i`–`x` → digits, so `part ii` and `part 2` compare equal.
9. What remains, space-joined, is `CleanTitle`.

A name that normalises to nothing (all junk) keeps its raw form as `CleanTitle` rather than becoming
an empty string that matches everything.

### 4.2 `SimilarityScorer`

`Score(ParsedName query, ParsedName candidate)` → `MatchScore { TitlePercent, RawPercent, YearMismatch }`,
both percentages integers 0–100.

**`RawPercent`** — normalised Levenshtein similarity between the two lowercased raw names with
extensions stripped: `(1 - distance / maxLength) * 100`. Levenshtein uses a two-row DP with an
early-exit distance ceiling.

**`TitlePercent`** — the larger of:

- **token-sort ratio:** distinct tokens sorted and joined on each side, then the same normalised
  Levenshtein ratio. Strict: extra words cost you.
- **token-set ratio × 0.95:** the RapidFuzz construction — intersection `t0`, `t1 = t0 + rest of A`,
  `t2 = t0 + rest of B`, score is `max(ratio(t0,t1), ratio(t0,t2), ratio(t1,t2))`. This is what makes
  a short query (`matrix`) score high against a longer title (`the matrix reloaded`). The 0.95
  factor keeps a genuine exact match ranked above a containment match.

Then the year adjustment: both sides carry a year and they are **equal** → `+5`, capped at 100. Both
carry a year and they **differ** → `-15` and `YearMismatch = true`. The row is still shown; the UI
flags it. One side missing a year changes nothing.

Ranking is `TitlePercent` descending, then `RawPercent` descending.

### 4.3 `FileIndex`

Holds every entry in memory and answers queries.

Scoring all 100,000 entries per keystroke is too slow — three Levenshtein passes over ~30-character
strings is roughly 10⁸ character operations. So a prefilter runs first:

- Each entry's `CleanTitle` is reduced to a set of character bigrams, and an inverted index maps
  bigram → entry ids. Built once at load.
- For a query, a reused count array tallies shared bigrams per entry in one pass.
- **Containment coefficient** — `shared / min(queryBigrams, entryBigrams)` — must be ≥ 0.5. Containment
  rather than Dice, because a short query against a long title has a poor Dice score and would be
  filtered out precisely when the user needs it most.
- Candidates are capped at the 5,000 best by containment, then scored in full, in parallel.
- Queries shorter than two characters skip the prefilter and fall back to a substring scan.

The prefilter is a performance optimisation and must not change results: a test compares its output
against brute-force scoring over a corpus and requires the same set above the display threshold. The
containment floor exists to be loosened, not defended — if that test finds a match the prefilter
dropped, the floor comes down.

### 4.4 `LibraryStore` (SQLite)

Database at `%LOCALAPPDATA%\FilmDex\filmdex.db`. Schema version tracked in `PRAGMA user_version`;
migrations run on open.

```sql
CREATE TABLE Library (
  Id INTEGER PRIMARY KEY,
  Label TEXT NOT NULL,
  RootPath TEXT NOT NULL UNIQUE,
  LastScanUtc TEXT NULL,
  EntryCount INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE Entry (
  Id INTEGER PRIMARY KEY,
  LibraryId INTEGER NOT NULL REFERENCES Library(Id) ON DELETE CASCADE,
  Kind INTEGER NOT NULL,          -- 0 = file, 1 = folder
  RawName TEXT NOT NULL,
  Extension TEXT NULL,
  RelativePath TEXT NOT NULL,
  SizeBytes INTEGER NULL,
  ModifiedUtc TEXT NULL,
  CleanTitle TEXT NOT NULL,       -- precomputed, so search never re-parses
  Year INTEGER NULL,
  Season INTEGER NULL,
  Episode INTEGER NULL,
  CategoryId INTEGER NULL         -- reserved for the categorisation feature; unused today
);

CREATE INDEX IX_Entry_LibraryId ON Entry(LibraryId);
```

`CleanTitle`, `Year`, `Season` and `Episode` are written at scan time. A change to the normaliser
therefore requires a re-scan, or a one-off reparse pass — the plan should include a "reparse all
entries" path that recomputes those columns from `RawName` without touching the disk, so a rule
change does not force the owner to plug in every drive again.

Writes go through one transaction per scan with a prepared statement.

### 4.5 `FolderScanner`

`ScanAsync(string root, IProgress<ScanProgress>, CancellationToken)` → `ScanResult`.

- `Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true,
  IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })` — reparse points are
  skipped so symlink and junction loops cannot hang a scan.
- Video extensions only: `mkv mp4 avi m4v mov wmv flv ts m2ts mpg mpeg webm rmvb divx vob iso`.
- A file under 50 MB whose name contains `sample` or `trailer` is dropped.
- **Folder entries:** every directory whose subtree contains at least one accepted video, excluding
  the root itself and excluding disc-structure and extras names (`bdmv stream video_ts subs
  subtitles sample extras featurettes certificate playlist backup`).
- Progress is reported at most every 250 ms, carrying files seen, entries accepted, and the current
  directory.
- The result carries a count of directories skipped as inaccessible, which the UI shows in the scan
  summary — a silent partial scan is exactly how a duplicate slips through.
- Long paths: the app manifest sets `longPathAware`, so paths beyond 260 characters are enumerated
  rather than throwing.

**Re-scan replaces the library's snapshot entirely.** A file deleted on disk disappears from the
index. This was confirmed with the owner; if "films I once had and deleted" ever matters, that is a
separate archive mode, not a change to this behaviour.

## 5. User interface

One window, two tabs.

**Libraries tab.** A list of scanned roots: label, path, entry count, last scan time. Buttons: add a
folder (drag-and-drop onto the window works too), re-scan, remove. A scan shows a progress bar with
the current directory and a cancel button; cancelling leaves the previous snapshot intact. When a
re-scan targets a path that no longer exists — the usual case, an external drive left unplugged —
the app says so plainly and does not touch the stored names.

**Search tab.** A text box that searches as you type, debounced 250 ms, each query cancelling the
one before it. Above the results, the app shows what was typed and what it normalised to, so a
surprising result set is explainable. Results grid:

| نام موجود در آرشیو | ٪ عنوان | ٪ نام خام | سال | کتابخانه | مسیر |
|---|---|---|---|---|---|

A threshold slider defaults to 60 % and filters on **the higher of the two percentages**, so a name
the film-aware parser mangles can still surface on its raw-string score. Results are capped at 200
rows. A year mismatch is marked on the row. Right-click offers copy path and open containing folder — the latter disabled when the
drive is not currently attached. Empty results say "nothing similar found", which is the answer the
owner is usually hoping for.

The UI is Persian, right-to-left (`FlowDirection="RightToLeft"`), which is what the owner reads.

## 6. Error handling

| Situation | Behaviour |
|---|---|
| Root path missing at scan time | Message naming the path; library and its entries untouched |
| Sub-directory unreadable | Skipped, counted, reported in the scan summary |
| Scan cancelled | Previous snapshot kept; the library is not left half-written |
| Database locked or corrupt | Message with the database path; the app stays usable for scanning nothing |
| Folder added twice | Matched on `RootPath`; offers a re-scan instead of a duplicate library |

## 7. Testing

xUnit over `FilmDex.Core` only; no UI tests.

- **Normalizer:** a table of real release names — `The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv`,
  `Interstellar (2014) [2160p] [HDR]`, `Breaking.Bad.S01E02.720p.WEB-DL`, a Persian dubbed name with
  `ي`/`ك` and a zero-width non-joiner, a name that is nothing but tags.
- **Scorer:** identical names score 100; containment scores high; unrelated names score low; equal
  years add, different years subtract and set the flag; scores are symmetric.
- **Index:** prefiltered results match brute-force results above the threshold, over a generated
  corpus.
- **Store:** round-trip through a temp database; re-scan replaces rather than appends; migration
  from an empty file; reparse pass updates `CleanTitle` without re-reading the disk.
- **Scanner:** against a temp directory tree — extension filter, sample-file rule, folder-entry rule,
  cancellation.

Performance targets, asserted loosely in tests and measured by hand: loading 100,000 entries under
1 second; a search under 100 ms.

## 8. Repository conventions

Standalone repository at `E:\Cash.Net\source\repos\sadrazkh\FilmDex`, branch `master`, commits in the
owner's name only with no co-author trailer. After each implementation step, the owner gets a task
table — what shipped, what remains, effort, prerequisites — and picks what runs next.
