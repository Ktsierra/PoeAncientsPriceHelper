namespace PoeCurrencyHelper;

// Pure ratio + volume math/formatting for the Currency Exchange helper. Presentation mirrors the
// game's own: ratios render as "want : have" with the dearer side normalized to 1 (the in-game
// "Market Ratio 73 : 1" shape); volumes abbreviate into the league's primary currency. Ratios are
// market AGGREGATES derived from poe.ninja values — approximations, never executable quotes.
internal static class ExchangeRates
{
    // poe.ninja's maxVolumeCurrency ids → the normalized item keys they correspond to. The API only
    // ever uses its core currencies here; an unknown id simply skips the direct-rate preference.
    private static readonly Dictionary<string, string> CoreIdToKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["divine"] = "divine orb",
        ["exalted"] = "exalted orb",
        ["chaos"] = "chaos orb",
    };

    // Formatted ratio for completing the pair with `cell` on the open picker's side and `opposite`
    // (key `oppositeKey`) settled on the other side; cellIsWant is true when the open picker is
    // "I Want". Null when either side has no usable market value — the caller shows no badge, never
    // a guess (same spirit as the uncut-gem rule).
    public static string? RatioFor(ExchangeEntry cell, ExchangeEntry opposite, string oppositeKey, bool cellIsWant)
    {
        decimal r;   // wants per 1 have
        // Prefer poe.ninja's observed rate for the exact pair when the opposite IS the cell's
        // highest-volume counter-currency (maxVolumeRate = cells per 1 counter). The base is usually
        // Divine/Exalted — almost always the max-volume counter — so most badges get real pair data.
        if (cell.MaxVolumeRate is { } pairRate && pairRate > 0m &&
            cell.MaxVolumeCurrency is { } coreId && CoreIdToKey.TryGetValue(coreId, out var counterKey) &&
            counterKey == oppositeKey)
        {
            r = cellIsWant ? pairRate : 1m / pairRate;
        }
        else
        {
            if (!cell.HasMarketData || !opposite.HasMarketData) return null;
            if (cell.PrimaryValue <= 0m || opposite.PrimaryValue <= 0m) return null;
            // Count ratio inverts the value ratio: 1 have buys value(have)/value(want) wants.
            r = cellIsWant ? opposite.PrimaryValue / cell.PrimaryValue
                           : cell.PrimaryValue / opposite.PrimaryValue;
        }
        return FormatRatioWithBase(r, oppositeKey, cellIsWant);
    }

    // r = wants per 1 have → "N : 1" when wants are the cheap side, else "1 : N".
    internal static string? FormatRatio(decimal wantsPerHave)
    {
        if (wantsPerHave <= 0m) return null;
        return wantsPerHave >= 1m
            ? $"{FormatSide(wantsPerHave)} : 1"
            : $"1 : {FormatSide(1m / wantsPerHave)}";
    }

    // The ratio with the BASE side named — "188 : 1 div", "1 : 6k div".
    //
    // The bare ratio is correctly normalized (cheaper side always 1, so a mirror reads "1 : 6k" the way
    // players actually quote it, never "0.05"), but it never said WHICH side was which. The cell's own
    // name sits right beside the pill in the grid, so naming just the opposite side is enough to make it
    // unambiguous, and it costs ~3 characters. The base is whatever the player has selected — never
    // assumed to be Divine.
    //
    // Side placement follows RatioFor's "want : have" ordering: when the open picker is the WANT side the
    // cell is on the left and the base on the right, and vice versa.
    public static string? FormatRatioWithBase(decimal wantsPerHave, string baseKey, bool cellIsWant)
    {
        if (FormatRatio(wantsPerHave) is not { } ratio) return null;
        var unit = ShortName(baseKey);
        if (unit.Length == 0) return ratio;
        int sep = ratio.IndexOf(" : ", StringComparison.Ordinal);
        if (sep < 0) return ratio;
        return cellIsWant
            ? $"{ratio} {unit}"                                      // base is the right-hand side
            : $"{ratio[..sep]} {unit}{ratio[sep..]}";                // base is the left-hand side
    }

    // Short display name for a currency key. The three core currencies get the abbreviations players
    // actually use; anything else falls back to its distinctive word ("mirror of kalandra" → "mirror",
    // "orb of alchemy" → "alchemy"), which reads better than a truncated full name.
    internal static string ShortName(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        switch (key)
        {
            case "divine orb": return "div";
            case "exalted orb": return "ex";
            case "chaos orb": return "chaos";
        }
        var name = key;
        if (name.StartsWith("orb of ", StringComparison.Ordinal)) name = name[7..];
        else if (name.EndsWith(" orb", StringComparison.Ordinal)) name = name[..^4];
        int of = name.IndexOf(" of ", StringComparison.Ordinal);
        if (of > 0) name = name[..of];
        return name;
    }

    // One side of a ratio, always ≥ 1: <10 keeps up to two decimals (trimmed, like the game's
    // "1 : 3.20"), <1000 rounds to an integer, ≥1000 uses the game's "2.1k" style.
    internal static string FormatSide(decimal n)
    {
        if (n >= 1000m)
        {
            decimal k = Math.Round(n / 1000m, 1);
            return k == decimal.Truncate(k) ? $"{k:0}k" : $"{k:0.0}k";
        }
        if (n >= 10m) return Math.Round(n).ToString("0");
        decimal two = Math.Round(n, 2);
        return two == decimal.Truncate(two) ? two.ToString("0") : two.ToString("0.##");
    }

    // How many UNITS of this item the market moved, derived rather than reported.
    //
    // volumePrimaryValue is a VALUE traded, denominated in the league's primary currency, and
    // primaryValue is one unit's price in that same currency — so the count is simply value / price.
    // Worked example from the live API (Runes of Aldur, 2026-07-28):
    //   Chaos Orb     49,116 div traded / 0.1144 div each  = ~429k chaos moved
    //   Mirror        80,126 div traded / 4,856  div each  = ~16.5 mirrors moved
    // Both move a similar amount of divines; the stock actually changing hands differs by four orders
    // of magnitude, which the div-denominated volume alone completely hides.
    //
    // Honest caveat: poe.ninja exposes no transaction count, so this is units moved, NOT the number of
    // trades — one bulk purchase of 400 chaos and 400 separate trades look identical here. It is also
    // fractional for expensive items (16.5 mirrors), because it is derived from a value aggregate.
    public static decimal? UnitsMoved(ExchangeEntry entry) =>
        entry.HasMarketData && entry.PrimaryValue > 0m
        && entry.VolumePrimaryValue is { } volume && volume > 0m
            ? volume / entry.PrimaryValue
            : null;

    // Units-moved text ("429k", "16.5"); null when it can't be derived. Deliberately unit-less — the
    // number counts the item on that row, which the row already names.
    public static string? FormatUnits(decimal? units) =>
        units is { } u && u > 0m ? Abbrev(u) : null;

    // Volume pill text ("82k div", "1.8k ex"); null when the API had no volume for the item.
    public static string? FormatVolume(decimal? volumePrimaryValue, string primaryCurrency)
    {
        if (volumePrimaryValue is not { } v || v <= 0m) return null;
        string unit = primaryCurrency.Equals("exalted", StringComparison.OrdinalIgnoreCase) ? "ex" : "div";
        return $"{Abbrev(v)} {unit}";
    }

    // Compact quantity: 82025 → "82k", 1759 → "1.8k", 180.7 → "181", 10.77 → "11", 0.5445 → "0.5".
    internal static string Abbrev(decimal v)
    {
        if (v >= 100_000m) return $"{Math.Round(v / 1000m):0}k";
        if (v >= 1000m)
        {
            decimal k = Math.Round(v / 1000m, 1);
            return k == decimal.Truncate(k) ? $"{k:0}k" : $"{k:0.0}k";
        }
        if (v >= 10m) return Math.Round(v).ToString("0");
        return Math.Round(v, 1).ToString("0.#");
    }
}
