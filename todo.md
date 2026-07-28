# todo.md — Windows ↔ Mac relay

Shared scratchpad between the **Mac session** (development, fixes) and the
**Windows session** (build, run, real-hardware testing). Windows can compile and
launch the app against an actual PoE 2 client; Mac cannot.

**Protocol**
- Windows appends findings under a new `## <date> — Windows` heading, newest at the bottom.
- Mac replies under `## <date> — Mac`, referencing the finding ID (`W-1`, `W-2`, …).
- Resolved items move to the Closed section; nothing is deleted.
- This file lives on **`feat/currency-exchange-helper`**, not `main`. Both sides
  commit there and push. Pull before writing to avoid conflicts.

---

## 2026-07-27 — Windows

### Environment baseline

First-time Windows setup — nothing was installed on this box before today.

| Component | Version |
|---|---|
| OS | Windows 11 Enterprise 10.0.22631 |
| CPU | 4 logical cores |
| Git | 2.55.0.windows.3 |
| .NET SDK | 10.0.302 (`C:\Program Files\dotnet\`) |
| Branch | `feat/currency-exchange-helper` @ `ab14435` |

The `net10.0-windows10.0.19041.0` TFM requires the .NET 10 SDK specifically —
9.x will not restore it. There is no `global.json`, so the SDK version floats;
worth pinning if you want Mac and Windows on an identical toolchain.

### Build status: GREEN

```
dotnet restore PoeAncientsPriceHelper.sln   -> clean (nuget.org only, per nuget.config)
dotnet build   -c Release                   -> 0 errors, 0 warnings
dotnet test    -c Release --no-build        -> 348 passed, 0 failed, 0 skipped
```

348 tests, up from 294 on `main` — all 54 new exchange tests pass
(`ExchangeNameResolverTests`, `ExchangePairStateTests`, `ExchangeRatesTests`,
`ExchangeScreenDetectorTests`, `PriceRepositoryTests`).

Output: `src\PoeAncientsPriceHelper\bin\Release\net10.0-windows10.0.19041.0\PoeAncientsPriceHelper.exe`,
reporting **3.8.0.0**. Content assets (`docs\`, `locales\`, `rumours.json`)
copied next to the exe as expected.

**No build action needed from Mac.** The branch compiles clean on Windows.

### Startup smoke test: PASS (3.8.0, no game running)

- Survived 1+ minute, `Responding: True`, main window created.
- No unhandled exception; nothing in the Windows Application event log.
- Idle CPU, gate loop only, no game on screen: **27.7% of one core for the
  first ~30s, settling to 7% of one core** thereafter. The spike is warmup
  (JIT + first poe.ninja fetch), not steady state.
- Working set: 227 MB → 250 MB → back to 207 MB. Fluctuates and returns; no
  leak signal over the window observed.

For contrast, `main` @ 3.7.1 idles at **1.7% of one core** on the same box. The
delta is expected — 3.7.1's rumour scanner sits idle until you press Start,
whereas `ExchangeHelperEnabled` defaults to `true` so the gate loop runs from
launch. Flagging the number only as a baseline, not as a complaint: 7% of one
core is ~1.75% of this 4-core machine. Will re-measure with PoE 2 actually
running, which is the number that matters.

### Not yet tested — needs the game

Everything that requires a live client: main-view detection, pair memory,
picker badges, the ninja pill under Market Ratio, overlay positioning, the
ESC / Left-Ctrl+click dismiss latch. Next session, with PoE 2 open.

### Findings

- **W-1 — Velopack update check fires twice at every launch.** Reproduces on
  both 3.7.1 and 3.8.0, so this is **pre-existing and not caused by the
  exchange work**. `update.log`:
  ```
  12:10:10.500  check start; IsInstalled=False   <- 3.7.1 launch
  12:10:13.835  check start; IsInstalled=False       (+3.3s)
  12:43:32.961  check start; IsInstalled=False   <- periodic, ~33 min later
  13:31:55.610  check start; IsInstalled=False   <- 3.8.0 launch
  13:31:55.793  check start; IsInstalled=False       (+0.18s)
  ```
  One launch → two checks; the later single entry looks like a healthy periodic
  timer. `IsInstalled=False` is expected when running out of `bin\` rather than
  a Velopack install. Low impact (two HTTP calls at startup), but if it's
  unintentional it's a cheap fix. **Is this intentional?**

- **W-2 — `headhunter.png` / `mirror.png` in the icon cache.** First run
  materialized `divine.png`, `exalted.png`, `headhunter.png`, `mirror.png` into
  `%LocalAppData%\PoeAncientsPriceHelper\`. Divine and Exalted are obviously
  relevant to currency exchange; Headhunter and Mirror look like leftovers from
  the logbook/remnant lineage. **Caveat: these were written by the 3.7.1 run and
  the cache persisted, so 3.8.0 did not re-fetch them — I have not confirmed
  3.8.0 requests them.** Say the word and I'll clear the cache and relaunch to
  find out. Cosmetic either way.

### Questions for Mac

1. **`ExchangeScreenDetector` ships English-only signatures**
   (`TitleSignatures = ["currency exchange"]`, `"i want"` / `"i have"`,
   `ExchangeScreenDetector.cs:33-37`). The comment calls per-locale strings a
   fast-follow, and notes a non-EN client "leaves the helper quietly idle
   rather than misfiring". Confirming that's the intended v1 behaviour — because
   from the user's chair, a non-English client is indistinguishable from a
   broken build. Worth a visible "exchange not detected" hint in the UI?

2. **What should Windows verify first once the game is open?** Give me a
   concrete pass/fail checklist for the currency-exchange path — which panel
   state, what should appear where, what "correct" looks like numerically — and
   I'll run it and report back here. My current understanding from the 3.8.0
   changelog is: open the exchange → select one side → browse the *other*
   side's picker → badges appear. Correct me if that's wrong.

3. **Reminder for triage:** the overlay is capture-excluded, so I cannot
   screenshot the badges to prove they rendered. Any visual bug report from
   Windows will be a human eyeball description, not an image. If you need
   machine-verifiable evidence, we'd need a debug/logging path that dumps what
   the detector resolved (keys, bounds, ratios) to a file.

---

## 2026-07-27 (later) — Windows

First real testing session against the 3.8.0 branch. Tested **without PoE 2
running**, using static screenshots of the exchange displayed on the primary
monitor (with the game closed, the engine scans the full screen — see
`ExchangeScanEngine.cs:123-132`). Manual ratio base set to `divine orb`.

### Confirmed working — please don't "fix" these

Verified end to end, so triage can skip them:

- **Prices load.** `GET poe.ninja/poe2/api/economy/exchange/current/overview?league=Runes%20of%20Aldur&type=Currency`
  → **HTTP 200, 53 items**. No `[PriceRepository] … HTTP <code>` lines in stderr
  across any run, so every category fetch succeeded.
- **OCR reads the panel.** `--ocr-test` on the picker screenshot returned **40
  rows**, resolving `chaos orb`, `divine orb`, `regal orb`, `orb of chance`,
  `artificer s orb`, `mirror of kalandra` and more — far above `MinCellsToConfirm`.
- **Label matching is fine.** `NameNormalizer.Normalize("I WANT")` → `"i want"`,
  an exact match. (Red herring for future triage: `--ocr-test` prints
  `norm='want'` for that line, because the *price* pipeline reads the leading
  `I` as a `1` multiplier. Different code path from the detector.)
- **Capture exclusion works.** The `SetWindowDisplayAffinity failed` warning
  never fired, and window enumeration shows the overlay window carrying
  affinity `17` (`WDA_EXCLUDEFROMCAPTURE`).
- **The overlay does render.** Enumerating the app's windows during a display
  caught the excluded full-screen overlay flipping to `Visible=True`. Detection
  → badge → render all work.

### Findings

- **W-4 — `ArgumentNullException: Value cannot be null. (Parameter 'encoder')`
  in the scan loop.** Reproduces on every launch, **once**, at startup; not
  again across ~50 subsequent gate ticks. Swallowed by the catch at
  `ExchangeScanEngine.cs:219-222` and written to `Console.Error`, which is a
  black hole in a WinExe — invisible without redirecting stderr. No `encoder`
  string exists anywhere in the source, so it's thrown inside GDI+ (an
  `Image.Save` overload receiving a null codec) on the capture→OCR conversion
  path, most likely a cold-start race before something is initialised. Low user
  impact (one scan pass lost) but it's a real unhandled path.

- **W-5 — Picker detection is strongly scale-sensitive, and fails silently.**
  This is the one that cost the most time. The full-frame pass runs at
  `upscale: 1` (`CaptureLines(area)`, `ExchangeScanEngine.cs:172`), unlike the
  gate which upscales 2× for small bands. A screenshot displayed at anything
  other than roughly native game scale produces text the OCR won't resolve;
  cells fall under `MinCellsToConfirm = 3`, `Detect` returns `None`, and the
  overlay simply never appears — **no error, no hint, nothing distinguishable
  from a broken build.** The tester only got badges after manually zooming to
  approximately in-game text size. In-game this may never bite (native
  resolution), but it makes screenshot-based testing a coin flip, and it would
  bite any user on a non-standard UI scale. Consider an upscale on the
  full-frame pass, or a visible "exchange detected but no cells resolved" state.

- **W-6 — Badge geometry is per-cell, so pills come out non-uniform and
  overlap.** Tester's description: *"showed in different sizes, covering other
  currency, generally ugly — it should be uniform."* Confirmed misaligned,
  overlapping/covering text, and inconsistent sizing. Root cause looks
  mechanical:
  - `ExchangeOverlay.cs:37` — *"Font per rounded pixel-height bucket, so pills
    scale with the cell text across resolutions"* — the pill font/size is
    derived from **each cell's own OCR text height**.
  - `ExchangeScreenDetector.DetectPicker` merges wrapped names with
    `Rectangle.Union(a.Bounds, b.Bounds)` (`ExchangeScreenDetector.cs:132`),
    so any two-line cell ("Orb of Augmentation", "Blacksmith's Whetstone",
    "Lesser Jeweller's Orb") has ~**double** the height of a one-line cell.
  - Those cells therefore land in a much larger font bucket and get a much
    larger pill, which spills into neighbouring cells.
  - Anchoring is to the OCR **text** bounds, not the panel's uniform grid cell,
    which also explains the misalignment.

  Suggested direction: pick **one** font size for the whole picker (median or
  modal single-line cell height, or derive it from the grid pitch), and anchor
  pills to a computed uniform cell rect rather than per-line OCR bounds. Ratio
  and volume strings vary in length too, so pill width should probably be
  clamped to the cell.

- **W-7 — The exchange path has no diagnostics at all.** `RumourDiag.cs` exists
  for the rumour path; the exchange side has exactly two `Console.Error` lines
  and nothing else. Every failure mode above (`None` detection, empty snapshot,
  missing base key) hides the overlay **silently** — `ShowPickerBadges` calls
  `HideOverlay()` and returns with no trace. From the user's chair, "no prices",
  "wrong scale", "no remembered pair", and "crashed" are indistinguishable.
  This is the single biggest blocker to Windows reporting useful bugs. An
  `ExchangeDiag` mirroring `RumourDiag` (what the gate saw, cells resolved,
  base key chosen, why it hid) would turn future rounds from guesswork into
  data.

- **W-8 — Two full-screen overlay windows, only one capture-excluded.**
  Enumeration shows two `1920x1080` WinForms windows owned by the app: one with
  display affinity `17` (excluded) and one with affinity `0` (**not** excluded).
  Both were observed `Visible=True` at different moments. I don't know which
  subsystem owns which, so this is an observation, not a claim — but if the
  affinity-`0` window is ever used for exchange badges, they'd be captured and
  fed back into the next OCR pass.

### Re W-2 (from the earlier entry)

Still not re-tested — the icon cache persisted from the 3.7.1 run, so 3.8.0
never re-fetched. Say the word and I'll clear `%LocalAppData%` and relaunch.

### UX note, not a bug

An `I WANT` picker with no remembered `I Have` (and no manual base) correctly
shows nothing — `ShowPickerBadges` needs a denominator. That is by design and
the code is right. But it is completely silent, and it was the tester's first
and most confusing dead end. A one-line hint ("select the other side first")
would save every new user the same hour.

---

## 2026-07-27 (later still) — Windows implemented W-6 and W-7

Mac was busy, so Windows took these two directly. Commit `8ab21a8` on this
branch. **Build clean, 0 warnings, 360 tests pass** (348 + 12 new). Please
review — especially the layout heuristics, which are my judgement calls about
a panel you know better than I do.

### W-6 fixed — `ExchangeBadgeLayout.cs` (new) + `ExchangeOverlay.cs`

Split the geometry into a pure, unit-testable class rather than growing the
form. Two changes:

- **One font for the whole panel**, from the **median single-line cell
  height**. Median so a stray merged OCR box can't skew it; single-line-only
  (cells taller than 1.5x the panel minimum are treated as wrapped) so
  two-line names don't inflate every pill. `FontPxForHeight` keeps the
  original curve, so a clean single-line panel renders exactly as before.
- **Pills right-align inside their own column.** Columns come from clustering
  cell x-*centres* (stable) rather than text extents (wildly variable), split
  at gaps over 10% of panel width. A column's right edge is the midpoint to
  the next column's centre, so a pill fills its own cell but can never reach
  the neighbour's label. The volume tail is dropped before a pill overflows.

The main-view pill still sizes off its own anchor — it's a single pill, not a
grid, so the old behaviour is correct there.

12 tests in `ExchangeBadgeLayoutTests.cs`, with bounds modelled on the real
3-column picker from the 1188x1030 screenshot.

**Judgement calls to sanity-check:** `WrappedHeightRatio = 1.5` and
`ColumnSplitFraction = 0.10`. Both are defensible from the one screenshot I
have; neither is validated against 4K, ultrawide, or a windowed client.

### W-7 fixed — `ExchangeDiag.cs` (new) + `ExchangeScanEngine.cs`

Mirrors `RumourDiag` exactly: `--debug` only, writes `exchange_scan.txt` in
the data dir, no-op otherwise. Records gate hits/misses with the band
rectangle and line count, detect results with OCR line + cell counts, the
chosen ratio base, and **a reason for every hide** — no prices, no base key,
cells detected but none priceable. Loop exceptions now log a stack trace.

Recurring reasons go through `Quiet()`, which logs a reason only when it
changes, so the ~1 Hz idle loop doesn't bury the useful lines.

Verified live. A real line from a debug run:

```
[14:52:43.404] gate miss band={X=480,Y=0,Width=960,Height=216} lines=8
               (no 'currency exchange' / 'i want' / 'i have' in the band)
```

### W-4 — new evidence, still open

The `ArgumentNullException('encoder')` **did not fire** on a `--debug` launch.
`--debug` skips `AutoStart`, so the price engine never started — which points
at a **startup race between the price engine and the exchange gate** over the
shared capture backend, rather than anything intrinsic to the exchange path.
Now that the loop logs stack traces, the next non-debug repro should name the
frame outright. Not fixed — I didn't want to guess at synchronisation in a hot
loop I don't own.

### W-5 — deliberately NOT fixed, needs your call

Raising the full-frame pass above `upscale: 1` is a real CPU/robustness
tradeoff and I don't think it's mine to make. Worth knowing before you decide:
a 2x upscale of a 1920x1080 frame is 3840x2160, which exceeds
`OcrEngine.MaxImageDimension` and gets scaled straight back down — so a naive
upscale costs CPU for a fraction of the intended gain. A targeted upscale of
just the detected panel region would be the better shape, but that needs a
detection pass first, which is the thing failing. Your call.

The new `detect NONE with N OCR lines` diagnostic at least makes this failure
visible now instead of silent.

### W-8 — still open, needs someone who knows the window ownership

Two full-screen overlay windows, only one carrying affinity `17`. I didn't
touch it because I can't tell from outside which subsystem owns which.

---

## 2026-07-27 (session 2) — Windows UX round

Tester confirmed the picker now works and pill sizes are consistent. Remaining
feedback drove this round. Commits `ae6fd6e`, `22be2f8`. **376 tests pass**,
build clean.

### W-9 fixed — badges blinked on and off

Reported as *"it shows sometimes... off, then on for a few seconds"*. A cell
that resolved on one pass and got clipped on the next lost its badge, over a
completely static grid.

New `ExchangePickerStabilizer` — the exchange analogue of `RumourStabilizer`,
which already exists for exactly this reason on the rumour side. Keyed by cell
**position** rather than row index, because the picker is a spatial grid, not a
short ordered list (same reasoning as the price overlay's `RowSlot`). A cell
missed for up to 3 passes (~2.7 s at the 900 ms default) keeps its badge;
beyond that it's dropped. Switching side, moving the panel, or hiding the
overlay all reset it, so badges can never outlive their panel. 9 tests.

### W-10 fixed — the ratio never said which side was which

Reported: *"1:188 1.4k div ... not very intuitive"*.

Important correction from the tester, worth recording because it shaped the
fix: **the base is whatever the player selects, not always Divine**, and
players quote the cheap side as 1 — "6k div per mirror", never "1 exalt is
0.02 div". The existing `FormatRatio` **already did this correctly** and is
unchanged. What was missing was naming the sides.

The base side now labels itself, and only the base — the cell's own name is
already beside the pill in the grid:

```
Exalted Orb          [ 188 : 1 div ]
Mirror of Kalandra   [ 1 : 6k div ]
```

The label follows the base across the separator when the open picker is the
*I Have* side (`RatioFor` orders as want : have). Core currencies use the
abbreviations players use (`div`/`ex`/`chaos`); anything else falls back to its
distinctive word (`mirror of kalandra` → `mirror`).

**Mac: this changed three of your `ExchangeRatesTests` expectations**
(`"492 : 1"` → `"492 : 1 div"` etc). Intended behaviour change, not a
regression — but it's your assertion I rewrote, so flagging it explicitly.

### W-11 fixed — staleness chip wording

`ninja 12m` read as a brand plus an unexplained number. Now **`snapshot 12m
old`**, which says what the number measures. Tester's suggestion.

### W-12 fixed — my own overlap bug from the previous round

The volume-drop check compared the pill against the cell's **left** edge, so it
only fired for pills wider than an entire cell — pills still landed on their
own cell's name. Now measured against the name's **right** edge, which is the
thing actually being overlapped.

### Per-cell diagnostics added

Every picker cell now logs either its ratio or exactly why it was skipped
(is the base / no snapshot entry / no computable ratio, with primary value and
max-volume pair dumped). Added while chasing a reported case where Exalted Orb
showed no badge against a divine base — poe.ninja carries full data for it
(`primaryValue 0.002268, maxVolumeCurrency "divine", maxVolumeRate 440.9`), so
the drop was upstream of the maths. With the stabilizer in, that specific
symptom looks like it was the blinking, but the trace stays.

### Still open: why only at 80% zoom?

Tester found the picker only detects with the screenshot at ~80% zoom. This is
**W-5** — the full-frame pass runs at `upscale: 1`, so OCR only resolves text
already near native game size, and 80% happened to land there for a
1188x1030 screenshot on a 1920x1080 monitor. Expected to be a non-issue at
native resolution in-game; to be confirmed against the real client.

---

## 2026-07-28 — DIRECTION CHANGE: per-cell overlay is being retired

**Mac: please read this before anything else in this file.** In-game testing
killed the per-cell badge approach. This reverses a design decision, so the
reasoning is recorded in full.

### What in-game testing found

Tester ran it against the real client repeatedly. Four failures:

1. **Scrolling the picker leaves ghost badges.** Partly my fault — the
   `ExchangePickerStabilizer` I added deliberately re-emits cells unseen for up
   to 3 passes (~2.7 s). That is correct for a static panel and actively wrong
   for a scrolling one: it *manufactures* ghosts.
2. **Switching category tabs quickly (Abyssal / Breach / Omens) breaks it.**
3. **OCR is too slow.** A full-frame pass at ~900 ms cannot track a grid the
   user is actively scrolling — the overlay always describes a screen that has
   already changed.
4. **Dense cells leave no room for a pill.** "Perfect Orb of Augmentation" and
   "Perfect Orb of Transmutation" wrap to two lines and fill their cell
   completely. There is nowhere to draw anything.

### Which of those are structural

1 and 2 are tunable (detect the panel changed, reset). **3 and 4 are not.**

Latency is inherent to full-frame OCR at this cadence. And (4) is fatal to the
entire idea of in-cell anchoring, mine included: if the cell is full of text,
no layout algorithm creates space. Uniform sizing and column clamping made the
pills tidier without touching the actual problem.

Tester's summary, which is correct: *"it works for verisium remnants cuz its
static, it does not for dynamic market comparisons."* The remnant panel is
static, finite and sparse. The exchange picker is none of those.

### The reframe

The market data was never the hard part — poe.ninja gives it to us cleanly and
that half is verified working. The hard part is tracking **where things are on
screen**. So stop tracking.

The only thing genuinely needed from the screen is **which currency is the
base**, and there is already a reliable non-OCR path for that: the manual base
dropdown in Settings. That collapses the OCR requirement from "read and track
~50 moving cells at 1 Hz" to "optionally notice the exchange is open" — a
nice-to-have that can fail without breaking correctness.

### Agreed direction: a companion panel

Chosen by the tester from three options. A fixed, draggable, always-on-top
window showing the full ratio table for the selected base:

```
┌─ Exchange Helper ────── snapshot 4m old ─┐
│ Base: Divine Orb          [change]       │
├──────────────────────────────────────────┤
│ Exalted Orb        188 : 1      1.4k vol │
│ Chaos Orb          8.8 : 1       69k vol │
│ Regal Orb          1.2k : 1      320 vol │
│ Perfect Orb of Aug 1 : 2.4      1.1k vol │
│ Mirror of Kalandra 1 : 6k         12 vol │
└──────────────────────────────────────────┘
```

Why this shape:

- **Scroll, tab-switch and ghosting stop being possible failure modes** — there
  is nothing anchored to a cell to go stale.
- **Volume always fits.** The tester wanted volume and it kept being dropped for
  space; in a panel there is no space pressure.
- **Degrades to fully working with zero OCR.** Base comes from the dropdown.
- Accepted tradeoff: you read across to the panel rather than seeing the number
  beside the cell you're looking at.

### What this retires

Superseded, kept in-tree until Mac reviews rather than deleted unilaterally:

- `ExchangeOverlay.cs` — per-cell layered overlay
- `ExchangeBadgeLayout.cs` — my column/font geometry (W-6), moot without cells
- `ExchangePickerStabilizer.cs` — moot, and the ghost source
- Picker detection + cell resolution in `ExchangeScreenDetector` /
  `ExchangeNameResolver` — the expensive full-frame pass

### What survives

- `PriceRepository` exchange fetch + snapshot — verified working, unchanged
- `ExchangeRates` — the ratio maths and normalization are correct and stay
- `ExchangeDiag`
- The cheap top-band gate, **optionally**, purely to auto-show the panel

---

## Might do — configurable exchange scan region

Raised by the tester: *"would it be better if we did the same thing remnants do
where we select the area to scan?"* Worth recording because the answer is yes,
and the exchange is the odd one out in this app:

| Path | How it finds its scan area |
|---|---|
| Price / remnants | Fully calibrated `RegionRect`, set by the user (F4) |
| Rumours (Atlas) | Computed default **with a user override** — `overrideRegion ?? WorldGateRegion(gateArea)` (`RumourScanEngine.cs:127`), backed by `RumourWorldAutoDetect` + `RumourWorldRect` |
| **Exchange** | **Hardcoded** — top 20% x middle 50%, no override, no calibration |

The rumour path is the precedent to copy: auto-detect by default, manual
override when auto-detect fails on a given setup. Would need
`ExchangeGateAutoDetect` + `ExchangeGateRect` config keys, a Settings section,
and wiring into `GateRegion`.

**Deliberately not built yet.** It's only worth doing once, tuned against the
real client — the current evidence is all from screenshots, and a region tuned
to those could be wrong for the game. Revisit if in-game testing shows the
hardcoded band missing the header.

---

## Closed

- **W-6** — non-uniform, overlapping picker badges. Fixed in `8ab21a8`, overlap
  check corrected in `22be2f8` (Windows). Awaiting Mac review of the layout
  heuristics.
- **W-7** — no diagnostics in the exchange path. Fixed in `8ab21a8`, per-cell
  tracing added in `ae6fd6e` (Windows).
- **W-9** — blinking badges. Fixed in `22be2f8` (Windows).
- **W-10** — unlabelled ratio sides. Fixed in `22be2f8` (Windows).
- **W-11** — staleness chip wording. Fixed in `22be2f8` (Windows).
- **W-12** — pill overlapping its own cell's name. Fixed in `22be2f8` (Windows).
