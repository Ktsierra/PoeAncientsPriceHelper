# Changelog

All notable changes to **Poe Currency Helper** are documented here.
The format is loosely based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.0.0] — 2026-07-28

First release under the new name. The app is now a companion window for the Currency Exchange rather
than a screen overlay, and the version resets to 1.0.0 because almost nothing of the previous product
remains. See **Lineage** below.

### Added

- **Currency Exchange panel.** Pick a league and a base currency and see every tradeable currency
  priced against it, live from poe.ninja. Covers all 13 exchange categories — currency, runes,
  fragments, essences, soul cores, breach, delirium, ritual, idols, abyss, expedition, uncut gems and
  verisium — fetched concurrently and refreshed every 30 minutes.
- **Any base currency.** The base is chosen from the live list for your league, not fixed to Divine
  or Exalted. Hardcore leagues price in exalted, and the app follows the league's primary currency
  rather than assuming.
- **Ratios normalized the way players quote them.** The cheaper side is always `1`, so a mirror reads
  `1 : 4.9k div` rather than `0.05`. The base side is named, so the direction is never ambiguous.
- **Units-moved column.** Volume tells you the value the market moved; units tells you how much
  *stock* that was. Mirrors move more divines than chaos (80.1k vs 49.1k) while only ~17 mirrors
  change hands against ~429,000 chaos — a difference volume alone completely hides. Derived as
  `volume ÷ unit price`, since poe.ninja exposes no transaction count: it counts items moved, **not**
  the number of trades.
- **Filter and sort** — filter by name, sort by value moved, units moved, price, or name.
- **Always on top** (optional), plus remembered window position and size, with a guard so a panel
  left on a since-disconnected monitor still opens on screen.
- **"No data" currencies** are shown rather than silently omitted, so a currency you are looking for
  never just fails to appear. Hideable.

### Removed

Everything that existed to read the screen. The product is now only the currency exchange:

- All OCR (`Windows.Media.Ocr`) and screen capture (Windows Graphics Capture, GDI fallback).
- Global input hooks (SharpHook) and all hotkeys.
- The Island Rumour / Verisium Remnant helper.
- The logbook price scanner, the price overlay, the calibration flow and localized name maps.

Consequences: the `SharpHook` and `Vortice.Direct3D11` / `Vortice.DXGI` dependencies are gone, the
target framework moves from `net10.0-windows10.0.19041.0` back to `net10.0-windows` (that SDK pin
existed only for WinRT OCR and WGC capture), and the release build drops from **40.9 MB across 38
files to 8.3 MB across 9**.

### Why the overlay approach was abandoned

The previous version tried to draw price badges directly onto the in-game exchange picker. Testing
against the live client found four failures, two of them structural:

- Scrolling the picker left ghost badges behind.
- Switching category tabs quickly broke detection.
- OCR could not keep up with a grid the player was actively scrolling — the overlay always described
  a screen that had already changed.
- Currency names long enough to fill their cell ("Perfect Orb of Augmentation" wraps to two lines)
  left no room to draw anything at all.

The last two cannot be tuned away: latency is inherent to full-frame OCR at that cadence, and no
layout algorithm creates space in a cell that is already full. Reading the market instead of the
screen removes all four at once.

### Kept from the previous version

The poe.ninja fetch layer, the ratio maths and its normalization, Happy Eyeballs dual-stack dialling
(so a broken IPv6 path falls back to IPv4 instead of hanging), the Velopack installer and
auto-updater, and the config/paths plumbing.

### Migration

Settings now live in `%LocalAppData%\PoeCurrencyHelper`. Nothing carries over from
`%LocalAppData%\PoeAncientsPriceHelper` — the old config was mostly calibration regions, hotkeys and
capture settings that no longer exist. Pick your league and base currency once on first run.

---

## Lineage

This project began as a fork of **Poe Ancients Price Helper**, a screen-overlay tool for logbook
pricing and Verisium Remnants. Its changelog through v3.8.0 remains in this repository's git history.
