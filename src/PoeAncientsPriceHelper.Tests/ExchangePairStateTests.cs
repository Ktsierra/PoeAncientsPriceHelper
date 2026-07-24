using PoeAncientsPriceHelper;

namespace PoeAncientsPriceHelper.Tests;

public class ExchangePairStateTests
{
    // Rule: both sides empty → first picker shows nothing (opposite unknown either way).
    [Fact]
    public void FreshState_NoOppositeForEitherSide()
    {
        var s = new ExchangePairState();
        Assert.Null(s.OppositeOf(ExchangeSide.Want));
        Assert.Null(s.OppositeOf(ExchangeSide.Have));
    }

    // Rule: "1 screen sets i want/i have" — the pick is read off the main view; then opening the
    // OPPOSITE side shows ratios against it, while reopening the SAME side still shows nothing.
    [Fact]
    public void OneSideSet_ShowsOnlyWhenOpeningTheEmptySide()
    {
        var s = new ExchangePairState();
        s.ApplyMainView("divine orb", null);            // user set I Want = Divine
        Assert.Equal("divine orb", s.OppositeOf(ExchangeSide.Have));  // open I Have → show vs divine
        Assert.Null(s.OppositeOf(ExchangeSide.Want));   // reopen I Want → user is changing it → nothing
    }

    // Rule: both sides set → either picker shows, against the OTHER side's currency.
    [Fact]
    public void BothSidesSet_EitherPickerShowsAgainstTheOther()
    {
        var s = new ExchangePairState();
        s.ApplyMainView("divine orb", "vaal orb");
        Assert.Equal("vaal orb", s.OppositeOf(ExchangeSide.Want));
        Assert.Equal("divine orb", s.OppositeOf(ExchangeSide.Have));
    }

    // Empty → set applies in ONE read (a resolved read is high-confidence, like exact price matches).
    [Fact]
    public void UnknownToKnown_AppliesImmediately()
    {
        var s = new ExchangePairState();
        s.ApplyMainView("divine orb", null);
        Assert.Equal("divine orb", s.WantKey);
    }

    // Known → different needs 2 CONSECUTIVE identical reads: one misread can't corrupt good state.
    [Fact]
    public void KnownToDifferent_NeedsTwoConsecutiveReads()
    {
        var s = new ExchangePairState();
        s.ApplyMainView("divine orb", null);
        s.ApplyMainView("chaos orb", null);
        Assert.Equal("divine orb", s.WantKey);          // first differing read: not yet
        s.ApplyMainView("chaos orb", null);
        Assert.Equal("chaos orb", s.WantKey);           // second consecutive: adopted
    }

    // Known → empty (slot cleared / OCR missed the name) also needs 2 consecutive reads.
    [Fact]
    public void KnownToEmpty_NeedsTwoConsecutiveReads()
    {
        var s = new ExchangePairState();
        s.ApplyMainView("divine orb", "vaal orb");
        s.ApplyMainView("divine orb", null);
        Assert.Equal("vaal orb", s.HaveKey);            // one dropout: hold
        s.ApplyMainView("divine orb", null);
        Assert.Null(s.HaveKey);                          // confirmed empty: cleared
    }

    // An agreeing read in between resets the pending change — alternating reads never flip state.
    [Fact]
    public void AlternatingReads_NeverAdopt()
    {
        var s = new ExchangePairState();
        s.ApplyMainView("divine orb", null);
        s.ApplyMainView("chaos orb", null);
        s.ApplyMainView("divine orb", null);
        s.ApplyMainView("chaos orb", null);
        Assert.Equal("divine orb", s.WantKey);
    }

    // The in-game swap-sides button: both sides change; after 2 consecutive swapped reads the state
    // follows (self-correction — no event tracking needed).
    [Fact]
    public void SwapSides_FollowsAfterTwoReads()
    {
        var s = new ExchangePairState();
        s.ApplyMainView("divine orb", "vaal orb");
        s.ApplyMainView("vaal orb", "divine orb");
        s.ApplyMainView("vaal orb", "divine orb");
        Assert.Equal("vaal orb", s.WantKey);
        Assert.Equal("divine orb", s.HaveKey);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var s = new ExchangePairState();
        s.ApplyMainView("divine orb", "vaal orb");
        s.Reset();
        Assert.Null(s.WantKey);
        Assert.Null(s.HaveKey);
    }
}
