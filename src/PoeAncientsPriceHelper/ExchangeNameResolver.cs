namespace PoeAncientsPriceHelper;

// Resolves raw OCR'd text (a picker cell, a main-view slot name) to an exchange price key:
// normalize → localized→English translate → exact → digit-fold → fuzzy (Levenshtein). Mirrors the
// remnant pipeline's steps but runs against the EXCHANGE snapshot, so the remnant matching surface
// stays untouched (CLAUDE.md). No prefix step: picker names are complete, unlike remnant rows.
// Results are memoized per instance; the engine builds a fresh resolver whenever the price
// generation changes. Total: garbage in → null out, never throws.
internal sealed class ExchangeNameResolver
{
    private const double FuzzyThreshold = 0.84;   // same bar as the remnant matcher
    private const int MinFuzzyLength = 6;
    private const int MinExactLength = 4;

    private readonly ExchangeSnapshot _snapshot;
    private readonly NameTranslator _translator;
    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);

    public ExchangeNameResolver(ExchangeSnapshot snapshot, NameTranslator translator)
    {
        _snapshot = snapshot;
        _translator = translator;
    }

    public string? Resolve(string rawText)
    {
        var normalized = NameNormalizer.Normalize(rawText);
        if (normalized.Length < MinExactLength) return null;
        if (_cache.TryGetValue(normalized, out var cached)) return cached;
        var key = ResolveCore(_translator.Translate(normalized));
        _cache[normalized] = key;
        return key;
    }

    private string? ResolveCore(string name)
    {
        var items = _snapshot.Items;
        if (items.ContainsKey(name)) return name;

        // OCR reads look-alike digits for letters ("D1vine"); keys hold no digits, so folding is safe.
        string lookup = name.Any(char.IsDigit) ? NameNormalizer.DigitFold(name) : name;
        if (!ReferenceEquals(lookup, name) && items.ContainsKey(lookup)) return lookup;
        if (lookup.Length < MinFuzzyLength) return null;

        string? best = null;
        double bestScore = FuzzyThreshold;   // must strictly exceed to win
        for (int len = Math.Max(0, lookup.Length - 3); len <= lookup.Length + 3; len++)
        {
            if (!_snapshot.KeysByLength.TryGetValue(len, out var keys)) continue;
            foreach (var key in keys)
            {
                int dist = ScanEngine.Levenshtein(lookup, key);
                double score = 1.0 - (double)dist / Math.Max(lookup.Length, key.Length);
                if (score > bestScore) { bestScore = score; best = key; }
            }
        }
        return best;
    }
}
