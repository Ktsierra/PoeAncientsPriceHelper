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

## Closed

_(nothing yet)_
