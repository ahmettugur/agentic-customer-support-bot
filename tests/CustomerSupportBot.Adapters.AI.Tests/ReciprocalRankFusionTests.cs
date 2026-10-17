// Hibrit aramada dense ve sparse sıralamalarının birleştirilmesi (Reciprocal Rank Fusion).

using CustomerSupportBot.Adapters.AI.Qdrant;

namespace CustomerSupportBot.Adapters.AI.Tests;

public class ReciprocalRankFusionTests
{
    [Fact]
    public void Fuse_RewardsDocumentsFoundByBothSignals()
    {
        // a: 1. ve 2.  c: 3. ve 1.  b: yalnızca 2.
        var fused = ReciprocalRankFusion.Fuse([["a", "b", "c"], ["c", "a"]], k: 60, take: 10);

        Assert.Equal(["a", "c", "b"], fused.Select(f => f.Id));
    }

    [Fact]
    public void Fuse_ScoresFollowTheRrfFormula()
    {
        var fused = ReciprocalRankFusion.Fuse([["a"], ["a"]], k: 60, take: 10);

        Assert.Equal(2d / 61d, fused[0].Score, precision: 10);
    }

    [Fact]
    public void Fuse_TakesOnlyRequestedCount()
    {
        var fused = ReciprocalRankFusion.Fuse([["a", "b", "c", "d"]], k: 60, take: 2);

        Assert.Equal(["a", "b"], fused.Select(f => f.Id));
    }

    [Fact]
    public void Fuse_CountsADuplicateWithinOneListOnlyOnce()
    {
        var withDup = ReciprocalRankFusion.Fuse([["a", "a", "b"]], k: 60, take: 10);

        Assert.Equal(1d / 61d, withDup.First(f => f.Id == "a").Score, precision: 10);
        Assert.Equal(["a", "b"], withDup.Select(f => f.Id));
    }

    [Fact]
    public void Fuse_BreaksTiesByFirstListOrder_Deterministically()
    {
        var fused = ReciprocalRankFusion.Fuse([["a"], ["b"]], k: 60, take: 10);

        Assert.Equal(["a", "b"], fused.Select(f => f.Id));
    }

    [Fact]
    public void Fuse_WithEmptyInput_ReturnsEmpty()
    {
        Assert.Empty(ReciprocalRankFusion.Fuse([[], []], k: 60, take: 5));
        Assert.Empty(ReciprocalRankFusion.Fuse([], k: 60, take: 5));
    }

    [Fact]
    public void MaxScore_IsTheScoreOfAFirstPlaceInEverySignal()
    {
        Assert.Equal(2d / 61d, ReciprocalRankFusion.MaxScore(signals: 2, k: 60), precision: 10);
    }
}
