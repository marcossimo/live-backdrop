using Xunit;

namespace LiveBackdrop.Tests;

public class RectMathTests
{
    private static readonly Native.Rect Window = new(0, 0, 100, 100);

    [Fact]
    public void NothingToCutLeavesTheWhole()
    {
        Assert.Equal([Window], RectMath.Subtract(Window, []));
        Assert.Equal([Window], RectMath.Subtract(Window, [new Native.Rect(200, 200, 300, 300)]));
    }

    [Fact]
    public void ARightPanelLeavesTheLeftPart()
    {
        Assert.Equal([new Native.Rect(0, 0, 60, 100)], RectMath.Subtract(Window, [new Native.Rect(60, 0, 100, 100)]));
    }

    [Fact]
    public void AHoleInTheMiddleLeavesFourBands()
    {
        var pieces = RectMath.Subtract(Window, [new Native.Rect(40, 40, 60, 60)]);

        Assert.Equal(4, pieces.Count);
        Assert.Equal((100 * 100) - (20 * 20), pieces.Sum(piece => piece.Width * piece.Height));
        Assert.DoesNotContain(pieces, piece => !piece.Intersect(new Native.Rect(40, 40, 60, 60)).IsEmpty());
    }

    [Fact]
    public void CoveringEverythingLeavesNothing()
    {
        Assert.Empty(RectMath.Subtract(Window, [new Native.Rect(-10, -10, 110, 110)]));
    }

    [Fact]
    public void ContainsAndIntersect()
    {
        Assert.True(Window.Contains(new Native.Rect(10, 10, 20, 20)));
        Assert.False(Window.Contains(new Native.Rect(90, 90, 110, 110)));
        Assert.Equal(new Native.Rect(90, 90, 100, 100), Window.Intersect(new Native.Rect(90, 90, 110, 110)));
        Assert.True(Window.Intersect(new Native.Rect(200, 0, 300, 10)).IsEmpty());
    }
}
