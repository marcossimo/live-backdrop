using Xunit;

namespace LiveBackdrop.Tests;

public class WebPageScannerTests
{
    private static readonly Native.Rect Window = new(100, 50, 1100, 850);

    [Fact]
    public void APageBecomesItsPanelRelativeToTheWindow()
    {
        // The browser's panel (address bar included) around its page, and the main pane around both.
        var page = new Native.Rect(705, 140, 1095, 845);
        Native.Rect[] regions = [new(100, 86, 1100, 850), new(700, 86, 1100, 850), page];

        var panels = WebPageScanner.ToPanels(Window, [page], regions);

        Assert.Equal([new Native.Rect(600, 36, 1000, 800)], panels);
    }

    [Fact]
    public void WithoutAPanelThePageItselfCounts()
    {
        var page = new Native.Rect(705, 140, 1095, 845);

        Assert.Equal([page.Offset(-100, -50)], WebPageScanner.ToPanels(Window, [page], []));
    }

    [Fact]
    public void ThePanelIsClippedToTheWindowAndKeptOnce()
    {
        var page = new Native.Rect(900, 100, 1300, 900);

        var panels = WebPageScanner.ToPanels(Window, [page, page], []);

        Assert.Equal([new Native.Rect(800, 50, 1000, 800)], panels);
    }

    [Fact]
    public void NoPagesNoPanels()
    {
        Assert.Empty(WebPageScanner.ToPanels(Window, [], [new Native.Rect(0, 0, 10, 10)]));
    }
}
