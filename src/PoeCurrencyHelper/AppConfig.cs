using Newtonsoft.Json;

namespace PoeCurrencyHelper;

internal sealed class AppConfig
{
    public string LeagueName { get; set; } = "Runes of Aldur";

    // Leagues offered in the dropdown. The string is used verbatim as poe.ninja's API league param, so
    // "HC Runes of Aldur" is the Hardcore variant of "Runes of Aldur". [JsonIgnore]: this is an
    // app-defined constant, not user data — persisting it makes Newtonsoft APPEND the saved list onto
    // this default on load (ObjectCreationHandling.Auto), duplicating every entry. Keep it code-only.
    [JsonIgnore]
    public List<string> AvailableLeagues { get; set; } = ["Runes of Aldur", "HC Runes of Aldur"];

    // Normalized exchange key everything is priced against ("divine orb"). Empty means "pick the
    // league's primary currency once data arrives" — the sane default for a first run.
    public string BaseCurrency { get; set; } = "";

    // Panel placement, so it reopens where the player left it beside the game. -1 = unset, centre it.
    public double WindowX { get; set; } = -1;
    public double WindowY { get; set; } = -1;
    public double WindowWidth { get; set; } = 460;
    public double WindowHeight { get; set; } = 620;

    // Keep the panel above the game window. On by default — it is a companion to a fullscreen-windowed
    // game and is useless behind it.
    public bool AlwaysOnTop { get; set; } = true;

    // Sort order for the table. "volume" puts the most-traded currencies first, which is what a player
    // scanning for a trade usually wants; "name" is alphabetical; "ratio" is cheapest-first.
    public string SortBy { get; set; } = "volume";

    // Hide currencies poe.ninja has no trading data for. On by default: a list full of "no data" rows
    // buries the ones you can actually act on.
    public bool HideNoData { get; set; } = true;
}
