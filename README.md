# Poe Currency Helper

A companion window for the **Path of Exile 2** Currency Exchange. Pick a league and a base currency,
and it shows every tradeable currency priced against it — live from
[poe.ninja](https://poe.ninja/poe2) — alongside how much value **and how much actual stock** the
market moved.

It does not watch your screen. There is no OCR, no screen capture and no global hotkeys: it reads
poe.ninja over HTTPS and draws a normal window. Put it on a second monitor, or keep it on top beside
a windowed client.

> Built for personal use — see [Where this came from](#where-this-came-from). It works, but it's not
> a polished product.

```
┌─ Poe Currency Helper ─────────────────────────────┐
│ League [Runes of Aldur ▾]   Base [Divine Orb ▾]   │
│ [filter…]                     Sort [Most value ▾] │
├───────────────────────────────────────────────────┤
│ Currency              Ratio      Volume    Units  │
│ Mirror of Kalandra    1 : 4.9k   80.1k div    17  │
│ Chaos Orb             8.75 : 1   49.1k div  429k  │
│ Omen of Light         1 : 12     28.2k div  2.3k  │
│ Hinekora's Lock       1 : 1.2k   24.7k div    21  │
├───────────────────────────────────────────────────┤
│ 556 currencies priced against Divine Orb          │
│ snapshot less than a minute old                   │
└───────────────────────────────────────────────────┘
```

## What the numbers mean

**Ratio** — the exchange rate, normalized the way players actually quote it: the cheaper side is
always `1`. Exalted reads `188 : 1 div`, a mirror reads `1 : 4.9k div`. The base side is named so
there is never a question of which way round it is, and the base is whatever *you* pick — nothing
assumes Divine.

**Volume** — the value the market moved, denominated in the league's primary currency.

**Units** — how much *stock* that value represents. This is the column volume alone hides: mirrors
move **more** divines than chaos (80.1k vs 49.1k) while only ~17 mirrors change hands against
~429,000 chaos. Sort by it to see what is genuinely liquid rather than merely expensive.

> Units is **derived**, not reported. poe.ninja exposes no transaction count, so this is
> `volume ÷ unit price` — the number of items moved, **not** the number of trades. One bulk purchase
> of 400 chaos and 400 separate trades look identical here.

All figures are **market aggregates from a periodic snapshot** (refreshed every 30 minutes; the
status line shows its age), not live order-book quotes. Treat them as a guide to where the market
is, not as an executable price.

## Features

- Every poe.ninja PoE2 exchange category in one table — currency, runes, fragments, essences, soul
  cores, breach, delirium, ritual, idols, abyss, expedition, uncut gems, verisium.
- **Any base currency**, not just Divine or Exalted — pick from the live list for your league.
- **Filter by name** and sort by value moved, units moved, price or name.
- **Softcore and Hardcore** — Hardcore prices in exalted rather than divine, and the app follows the
  league's primary currency rather than assuming.
- **Always on top** (optional) so it survives a windowed-fullscreen client.
- Remembers window position and size, with a guard so a panel left on a since-disconnected monitor
  still opens on screen.
- **Automatic updates** from GitHub Releases, powered by [Velopack](https://velopack.io/). Your
  settings live in `%LocalAppData%\PoeCurrencyHelper` and are kept across updates.

## Download & install

Grab **`PoeCurrencyHelper-win-Setup.exe`** from the [**Releases**](../../releases) page and run it.
It installs per-user (no admin required).

> Windows SmartScreen may warn that the app is unsigned — click **More info → Run anyway**.

## Build from source

Requires the **.NET 10 SDK**
([download](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)) and Windows 10 or 11.

```sh
dotnet build PoeCurrencyHelper.sln -c Release
dotnet test  PoeCurrencyHelper.sln -c Release

# self-contained release build
dotnet publish src/PoeCurrencyHelper/ -c Release -r win-x64 --self-contained true -o publish
```

## Troubleshooting

### Prices won't load

**"poe.ninja returned nothing for league …"** — the league name is sent verbatim to poe.ninja's API.
Check it matches a league that exists on [poe.ninja/poe2](https://poe.ninja/poe2); a new league needs
its name added to the dropdown.

**Fetches hang or fail on Starlink / CGNAT connections.** poe.ninja resolves to both IPv6 and IPv4,
and on a connection where the IPv6 path is broken the default behaviour stalls until each request
times out. The app races both families and uses whichever connects first, so a dead IPv6 route falls
back to IPv4 on its own — no firewall rule needed.

### Some antivirus software flags it

Unsigned, new, and downloaded by few people, so reputation- and ML-based engines sometimes return
verdicts like `FileRepMalware`. These are prevalence heuristics ("rare and unsigned"), not a match
against known malware. The full source is in this repo and you can build it yourself instead of
trusting the prebuilt download.

Worth noting that v1.0 is a much smaller target than its predecessor: it no longer captures the
screen or installs global input hooks, which were the two behaviours heuristics objected to most.

## Tech

- **.NET 10** (`net10.0-windows`), WPF
- **poe.ninja** API for market data — all 13 exchange categories fetched concurrently, 30-minute
  auto-refresh with fast retry after a failure
- **WPF UI** (lepoco) for theming
- **Velopack** for the installer and automatic updates

## Where this came from

This is a fork of **Poe Ancients Price Helper**, which reads your screen with OCR and draws prices
over the game. I tried to make that same approach work on the Currency Exchange — badges drawn right
onto the currency picker — and it didn't work. Scrolling left ghost badges behind, switching tabs
broke it, OCR couldn't keep up with a list you're actively scrolling, and long names like "Perfect
Orb of Augmentation" fill their cell completely so there's nowhere to draw anything.

The first two were fixable. The last two weren't, so I stopped fighting it and rewrote the whole
thing to read the market instead of the screen. That's what this is. The OCR attempt is still in the
git history on the `feat/currency-exchange-helper` branch if you're curious what didn't work.

**Made for my own use.** It does one thing and it does it fine, but don't expect wonders — no
polish pass, no support promises, and it'll break whenever poe.ninja changes their API or a new
league needs adding. Full attribution and which files carried over from the original are in
[NOTICE.md](NOTICE.md).

> **No licence yet.** Neither this repo nor the one it was forked from has a LICENCE file, so
> strictly there's no permission to redistribute it. Fine for personal use; see
> [NOTICE.md](NOTICE.md) before sharing builds around.

## Acknowledgements

- **[Velopack](https://github.com/velopack/velopack)** — installer & auto-update framework,
  © Caelan Sayler / Velopack Ltd., [MIT License](https://github.com/velopack/velopack/blob/develop/LICENSE).
- **[WPF UI](https://github.com/lepoco/wpfui)** (lepoco) — MIT License.
- **[Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)** — MIT License.
- Market data from **[poe.ninja](https://poe.ninja/poe2)** (unofficial API). This project is not
  affiliated with poe.ninja or Grinding Gear Games.
