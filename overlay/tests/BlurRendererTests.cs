using Xunit;

namespace LiveBackdrop.Tests;

public class BlurRendererTests
{
    [Theory]
    [InlineData(0, 4)]
    [InlineData(2, 4)]
    [InlineData(12, 9)]
    [InlineData(40, 30)]
    public void TheFadeGrowsWithTheBlurWithinBounds(float blur, float feather)
    {
        Assert.Equal(feather, BlurRenderer.FeatherFor(blur));
    }
}
