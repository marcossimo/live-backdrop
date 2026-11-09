using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace LiveBackdrop;

/// <summary>
/// Finds the panels of the Claude window that show an embedded web page (the built-in browser,
/// a preview), so the backdrop can leave them clear. It reads the app's accessibility tree
/// through UI Automation, by role and structure rather than by name, so it works in every UI
/// language, and skips the parts that never hold a page (the conversation, side panels,
/// toolbars) to stay light. Runs on its own thread.
/// </summary>
internal sealed class WebPageScanner : IDisposable
{
    private const int MaxDepth = 40;

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

    // Parts of the app that never hold an embedded page; the walk does not go into them.
    private static readonly HashSet<string> SkippedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "feed", "log", "complementary", "toolbar", "navigation", "banner", "article",
    };

    private readonly Thread thread;
    private readonly AutoResetEvent wake = new(false);
    private volatile bool stopping;
    private IntPtr target;

    public WebPageScanner()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "WebPageScanner" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    /// <summary>
    /// Raised on the scanner's thread when the pages changed, in pixels relative to the
    /// window's top-left corner.
    /// </summary>
    public event EventHandler<IReadOnlyList<Native.Rect>>? PagesChanged;

    /// <summary>The window to scan, or zero to stop scanning.</summary>
    public IntPtr Target
    {
        get => Interlocked.CompareExchange(ref target, IntPtr.Zero, IntPtr.Zero);
        set
        {
            Interlocked.Exchange(ref target, value);
            wake.Set();
        }
    }

    /// <summary>Asks for a scan now, for example after the window was resized.</summary>
    public void ScanSoon() => wake.Set();

    public void Dispose()
    {
        stopping = true;
        wake.Set();
        thread.Join(TimeSpan.FromSeconds(2));
        wake.Dispose();
    }

    /// <summary>
    /// The panel around each page, clipped to the window and made relative to it: the smallest of
    /// the app's regions strictly around the page (so its address bar is included), or the page
    /// itself when there is none. The views hosting a page share its exact bounds.
    /// </summary>
    internal static List<Native.Rect> ToPanels(Native.Rect window, IEnumerable<Native.Rect> pages, IReadOnlyList<Native.Rect> regions)
    {
        var panels = new List<Native.Rect>();
        foreach (var page in pages)
        {
            var panel = regions
                .Where(region => region.Contains(page) && region != page && region != window)
                .OrderBy(Area)
                .FirstOrDefault(page);
            var clipped = panel.Intersect(window);
            if (!clipped.IsEmpty())
            {
                var relative = clipped.Offset(-window.Left, -window.Top);
                if (!panels.Contains(relative))
                {
                    panels.Add(relative);
                }
            }
        }

        return panels;
    }

    private static long Area(Native.Rect rect) => (long)rect.Width * rect.Height;

    private static List<Native.Rect> Scan(UIA3Automation automation, IntPtr hwnd)
    {
        var scan = new ScanState(automation, Native.VisibleFrame(hwnd));
        Walk(scan, automation.FromHandle(hwnd), 0);
        return ToPanels(scan.Window, scan.Pages, scan.Regions);
    }

    private static void Walk(ScanState scan, AutomationElement parent, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        for (var child = scan.Walker.GetFirstChild(parent); child is not null; child = scan.Walker.GetNextSibling(child))
        {
            var role = child.Properties.AriaRole.ValueOrDefault ?? string.Empty;
            if (SkippedRoles.Contains(role))
            {
                continue;
            }

            if (role == "document" && depth > 0)
            {
                // The app itself is a document filling the window; a smaller one is a web page
                // embedded in it. A closed browser leaves its page in the tree, so the page must
                // be what is shown at its own centre.
                var page = Bounds(child);
                if (!page.IsEmpty() && Area(page) < Area(scan.Window) * 9 / 10)
                {
                    if (IsShown(scan, child, page))
                    {
                        scan.Pages.Add(page);
                    }

                    continue;
                }
            }

            if (role == "region")
            {
                scan.Regions.Add(Bounds(child));
            }

            Walk(scan, child, depth + 1);
        }
    }

    private static bool IsShown(ScanState scan, AutomationElement element, Native.Rect bounds)
    {
        if (bounds.IsEmpty() || element.Properties.IsOffscreen.ValueOrDefault)
        {
            return false;
        }

        var center = new System.Drawing.Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
        for (var hit = scan.Automation.FromPoint(center); hit is not null; hit = scan.Walker.GetParent(hit))
        {
            if (hit.Equals(element))
            {
                return true;
            }
        }

        return false;
    }

    private static Native.Rect Bounds(AutomationElement element)
    {
        var bounds = element.Properties.BoundingRectangle.ValueOrDefault;
        return new Native.Rect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    private void Run()
    {
        IReadOnlyList<Native.Rect> applied = [];
        List<Native.Rect>? previous = null;
        var failed = false;
        using var automation = new UIA3Automation();
        while (!stopping)
        {
            var hwnd = Target;
            if (hwnd != IntPtr.Zero && Native.IsWindow(hwnd))
            {
                try
                {
                    // Panels open and close with an animation; a result counts once two scans in
                    // a row agree, so one caught halfway through is never applied.
                    var pages = Scan(automation, hwnd);
                    if (previous is not null && pages.SequenceEqual(previous) && !pages.SequenceEqual(applied))
                    {
                        applied = pages;
                        PagesChanged?.Invoke(this, pages);
                    }

                    previous = pages;
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TimeoutException or InvalidOperationException)
                {
                    // The tree changes under us while the app updates; the next scan catches up.
                }
                catch (Exception ex)
                {
                    // Anything else (a fault in UI Automation or the app's accessibility) must not
                    // take the overlay down: it is logged once and the next scan tries again.
                    if (!failed)
                    {
                        Log.Write($"web page scan failed: {ex.GetType().Name}: {ex.Message}");
                        failed = true;
                    }
                }
            }

            wake.WaitOne(ScanInterval);
        }
    }

    private sealed class ScanState(UIA3Automation automation, Native.Rect window)
    {
        public UIA3Automation Automation { get; } = automation;

        public ITreeWalker Walker { get; } = automation.TreeWalkerFactory.GetControlViewWalker();

        public Native.Rect Window { get; } = window;

        public List<Native.Rect> Pages { get; } = [];

        public List<Native.Rect> Regions { get; } = [];
    }
}
