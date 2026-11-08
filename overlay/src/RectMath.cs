namespace LiveBackdrop;

/// <summary>Rectangle arithmetic for the overlay's areas.</summary>
internal static class RectMath
{
    public static bool IsEmpty(this Native.Rect rect) => rect.Width <= 0 || rect.Height <= 0;

    public static Native.Rect Intersect(this Native.Rect a, Native.Rect b) => new(
        Math.Max(a.Left, b.Left),
        Math.Max(a.Top, b.Top),
        Math.Min(a.Right, b.Right),
        Math.Min(a.Bottom, b.Bottom));

    public static bool Contains(this Native.Rect outer, Native.Rect inner) =>
        outer.Left <= inner.Left && outer.Top <= inner.Top && outer.Right >= inner.Right && outer.Bottom >= inner.Bottom;

    public static Native.Rect Offset(this Native.Rect rect, int dx, int dy) =>
        new(rect.Left + dx, rect.Top + dy, rect.Right + dx, rect.Bottom + dy);

    /// <summary>
    /// What is left of <paramref name="area"/> once every hole is cut out, as non-overlapping
    /// rectangles (each cut leaves at most four bands: above, below, left and right).
    /// </summary>
    public static List<Native.Rect> Subtract(Native.Rect area, IEnumerable<Native.Rect> holes)
    {
        var pieces = new List<Native.Rect> { area };
        foreach (var hole in holes)
        {
            var next = new List<Native.Rect>();
            foreach (var piece in pieces)
            {
                var cut = piece.Intersect(hole);
                if (cut.IsEmpty())
                {
                    next.Add(piece);
                    continue;
                }

                AddIfAny(next, new Native.Rect(piece.Left, piece.Top, piece.Right, cut.Top));
                AddIfAny(next, new Native.Rect(piece.Left, cut.Bottom, piece.Right, piece.Bottom));
                AddIfAny(next, new Native.Rect(piece.Left, cut.Top, cut.Left, cut.Bottom));
                AddIfAny(next, new Native.Rect(cut.Right, cut.Top, piece.Right, cut.Bottom));
            }

            pieces = next;
        }

        return pieces;
    }

    private static void AddIfAny(List<Native.Rect> list, Native.Rect rect)
    {
        if (!rect.IsEmpty())
        {
            list.Add(rect);
        }
    }
}
