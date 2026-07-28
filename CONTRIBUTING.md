# Contributing

Thanks for helping improve Poe Currency Helper. A few ground rules so
contributions are safe to accept and sustainable to maintain.

## Source only — no binaries

Contributions must be **source code or data files**, submitted as a pull
request. Prebuilt DLLs, EXEs, or other binaries will not be reviewed or
merged — the maintainer only ships artifacts rebuilt from reviewed source.
This is a security floor, not a judgment of intent: the app makes network
calls on the user's behalf, and a binary nobody can diff is a binary nobody
can vouch for.

## Pull requests, not issue attachments

Code changes go through a PR so they can be reviewed, built, and run through
CI. Pasting a patch or linking an external fork in an issue is a fine way to
*start* a conversation, but the change itself lands as a PR.

## Keep the app off the screen

v1.0 deliberately has **no OCR, no screen capture and no global input hooks**.
That is not an accident of scope — the previous version had all three and they
were removed because reading a scrolling, dynamic panel could not be made
reliable (see the 1.0.0 entry in `CHANGELOG.md`).

Changes that reintroduce screen reading or global hooks need a strong case made
in an issue *before* the code is written. Anything that watches the screen or
hooks input also changes the app's security profile, which is a separate
conversation from whether it works.

## New leagues and market data

The league list is a plain array in `AppConfig.AvailableLeagues`, and the
exchange categories are `ExchangeTypes` in `ExchangeRepository`. Both are sent
verbatim to poe.ninja's API, so adding a league or a newly introduced category
is a one-line change — please verify it against the live API first and say so in
the PR.

Anything that changes how a ratio is derived or displayed needs a test. The
maths in `ExchangeRates` is the part users trust without being able to check it,
so it is the part most worth guarding. Note in particular that ratios are
normalized so the **cheaper side is always 1** (a mirror reads `1 : 4.9k div`,
never `0.05`) because that is how players actually quote prices — changing that
convention is a product decision, not a formatting tweak.

## Attribution

If your change ports code from another project, say so in the PR and keep the
original licence and copyright intact. See `NOTICE.md` for this project's own
lineage.
