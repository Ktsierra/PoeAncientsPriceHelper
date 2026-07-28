# Notice — origin and attribution

## Lineage

**Poe Currency Helper** began as a fork of **Poe Ancients Price Helper** by
*pedro*, a screen-overlay tool for Path of Exile 2 logbook pricing and Verisium
Remnants.

Version 1.0.0 is a substantial rewrite rather than an evolution. The OCR
pipeline, screen capture backends, global input hooks, the Island Rumour /
Verisium Remnant helper, the logbook price scanner, the calibration flow and
every overlay were removed. What carried over from the original project is:

- the poe.ninja fetch layer and its response parsing,
- the exchange ratio maths and its normalization (`ExchangeRates`),
- Happy Eyeballs dual-stack dialling (`HappyEyeballs`),
- the Velopack entry point and update plumbing,
- the config and paths plumbing (`AppConfig`, `ConfigStore`, `AppPaths`).

The original project's full history through v3.8.0 remains in this repository's
git history and is not rewritten or squashed away.

## Licence status — unresolved

**This repository has no LICENCE file, and neither did the project it was forked
from.**

Under default copyright, code published without a licence grants no permission
to copy, modify or redistribute it. That makes the legal footing for
redistributing this fork — as a binary release or as source — unclear, however
substantial the rewrite is, because some original code remains.

This is not something a fork can fix unilaterally. Resolving it means one of:

1. asking *pedro* to add an explicit licence (MIT is the convention in this
   ecosystem, and matches every dependency listed below) to the original
   project, after which this fork can adopt it;
2. obtaining written permission from *pedro* to relicense this derived work, and
   recording it here;
3. replacing the remaining carried-over code with independent implementations,
   after which this project can be licensed freely.

Until one of those happens, treat this repository as **all rights reserved** and
do not redistribute builds of it publicly.

## Third-party components

Bundled at runtime, each under its own licence:

- **[Velopack](https://github.com/velopack/velopack)** — installer and
  auto-update framework. © Caelan Sayler / Velopack Ltd.,
  [MIT](https://github.com/velopack/velopack/blob/develop/LICENSE).
- **[WPF UI](https://github.com/lepoco/wpfui)** (lepoco) —
  [MIT](https://github.com/lepoco/wpfui/blob/main/LICENSE).
- **[Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)** —
  [MIT](https://github.com/JamesNK/Newtonsoft.Json/blob/master/LICENSE.md).

## Data source

Market data comes from the unofficial **[poe.ninja](https://poe.ninja/poe2)**
API. This project is not affiliated with, endorsed by, or supported by poe.ninja
or Grinding Gear Games. Path of Exile is a trademark of Grinding Gear Games.
