using ExplorerAlternative.Models;

namespace ExplorerAlternative.Tests.Models;

// 仕様書19章：分割ペインの境界（スプリッター）のドラッグによる、ペインの大きさの計算。
public sealed class SplitLayoutTests
{
    private const double Thickness = SplitLayout.SplitterThickness;

    // ===== 比率の範囲 =====

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(0.3, 0.3)]
    [InlineData(0.0, SplitLayout.MinRatio)]
    [InlineData(-5, SplitLayout.MinRatio)]
    [InlineData(1.0, SplitLayout.MaxRatio)]
    [InlineData(42, SplitLayout.MaxRatio)]
    public void ClampRatio_KeepsTheRatioWithinTheAllowedRange(double input, double expected)
    {
        Assert.Equal(expected, SplitLayout.ClampRatio(input), precision: 10);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ClampRatio_InvalidNumbers_FallBackToTheDefault(double input)
    {
        Assert.Equal(SplitLayout.DefaultRatio, SplitLayout.ClampRatio(input));
    }

    // ===== ペインの大きさの計算 =====

    [Fact]
    public void Calculate_SplitsTheSpaceExcludingTheSplitter()
    {
        var layout = SplitLayout.Calculate(1006, 0.5);

        Assert.Equal(500, layout.First, precision: 6);
        Assert.Equal(500, layout.Second, precision: 6);
        Assert.Equal(500, layout.SplitterOffset, precision: 6);
        Assert.Equal(506, layout.SecondOffset, precision: 6);
        // ペイン2つと境界で、全体をちょうど使い切る。
        Assert.Equal(1006, layout.First + Thickness + layout.Second, precision: 6);
    }

    [Fact]
    public void Calculate_UnevenRatio_GivesTheFirstPaneItsShare()
    {
        var layout = SplitLayout.Calculate(1006, 0.7);

        Assert.Equal(700, layout.First, precision: 6);
        Assert.Equal(300, layout.Second, precision: 6);
    }

    [Fact]
    public void Calculate_OutOfRangeRatio_IsClampedSoNeitherPaneVanishes()
    {
        var tooSmall = SplitLayout.Calculate(1006, 0.0);
        var tooLarge = SplitLayout.Calculate(1006, 1.0);

        Assert.True(tooSmall.First > 0);
        Assert.True(tooLarge.Second > 0);
        Assert.Equal(100, tooSmall.First, precision: 6);
        Assert.Equal(100, tooLarge.Second, precision: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    public void Calculate_NoSpace_ReturnsZeros(double total)
    {
        var layout = SplitLayout.Calculate(total, 0.5);

        Assert.Equal((0.0, 0.0, 0.0, 0.0), layout);
    }

    [Fact]
    public void Calculate_SpaceSmallerThanTheSplitter_ShrinksTheSplitterAndGivesPanesNothing()
    {
        var layout = SplitLayout.Calculate(4, 0.5);

        Assert.Equal(0, layout.First);
        Assert.Equal(0, layout.Second);
        Assert.Equal(4, layout.SecondOffset); // 境界が全体を占める
    }

    // ===== ドラッグ位置から比率へ =====

    [Fact]
    public void RatioFromPosition_AtTheMiddle_IsHalf()
    {
        // 全体1006のとき、境界の中心（=500+3）が真ん中。
        Assert.Equal(0.5, SplitLayout.RatioFromPosition(503, 1006), precision: 6);
    }

    [Fact]
    public void RatioFromPosition_RoundTripsWithCalculate()
    {
        foreach (var ratio in new[] { 0.2, 0.35, 0.5, 0.65, 0.8 })
        {
            var layout = SplitLayout.Calculate(1206, ratio);
            var splitterCenter = layout.SplitterOffset + (Thickness / 2);

            Assert.Equal(ratio, SplitLayout.RatioFromPosition(splitterCenter, 1206), precision: 6);
        }
    }

    [Theory]
    [InlineData(-100)]
    [InlineData(0)]
    public void RatioFromPosition_DraggedBeyondTheStart_StopsAtTheMinimum(double position)
    {
        Assert.Equal(SplitLayout.MinRatio, SplitLayout.RatioFromPosition(position, 1000));
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(5000)]
    public void RatioFromPosition_DraggedBeyondTheEnd_StopsAtTheMaximum(double position)
    {
        Assert.Equal(SplitLayout.MaxRatio, SplitLayout.RatioFromPosition(position, 1000));
    }

    [Fact]
    public void RatioFromPosition_NoSpace_ReturnsTheDefault()
    {
        Assert.Equal(SplitLayout.DefaultRatio, SplitLayout.RatioFromPosition(10, 0));
        Assert.Equal(SplitLayout.DefaultRatio, SplitLayout.RatioFromPosition(10, 3));
    }

    // ===== 境界の上かどうか =====

    [Fact]
    public void IsOnSplitter_TrueOnlyInsideTheSplitterStrip()
    {
        // 全体1006・半分ずつ：境界は 500〜506。
        Assert.False(SplitLayout.IsOnSplitter(499, 1006, 0.5));
        Assert.True(SplitLayout.IsOnSplitter(500, 1006, 0.5));
        Assert.True(SplitLayout.IsOnSplitter(503, 1006, 0.5));
        Assert.True(SplitLayout.IsOnSplitter(506, 1006, 0.5));
        Assert.False(SplitLayout.IsOnSplitter(507, 1006, 0.5));
    }

    [Fact]
    public void IsOnSplitter_FollowsTheRatio()
    {
        // 全体1006・0.7：境界は 700〜706。
        Assert.False(SplitLayout.IsOnSplitter(503, 1006, 0.7));
        Assert.True(SplitLayout.IsOnSplitter(703, 1006, 0.7));
    }
}
