namespace AEPControl;

// Pixel alignment only: never infer or delete passengers based on similar names.
public sealed class VisionPageStitcher : IDisposable
{
    public const int MaxJoinedHeight = 2400;
    public List<Bitmap> Pages { get; } = new();
    public int JoinedPairs { get; private set; }
    public int UncertainPairs { get; private set; }
    public string Summary(int originalCount) => $"{originalCount} capturas → {Pages.Count} imágenes para Gemini. {JoinedPairs} uniones con repetición recortada." +
        (UncertainPairs > 0 ? $" {UncertainPairs} uniones dudosas: se conservaron sin recortar." : "");

    private sealed record Strip(Bitmap Image, Rectangle Area, bool NewPage);
    private sealed record Rows(ulong[] Hash, bool[] Ink);
    private sealed record Match(int PreviousEnd, int CurrentEnd, int Score);

    public static VisionPageStitcher Build(IReadOnlyList<Bitmap> originals, CancellationToken token = default)
    {
        var result = new VisionPageStitcher();
        try
        {
            var strips = new List<Strip>();
            Rows? previousRows = null;
            for (var i = 0; i < originals.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var current = originals[i];
                var rows = Describe(current, token);
                Match? match = null;
                if (i > 0 && current.Size == originals[i - 1].Size)
                    match = FindOverlap(previousRows!, rows, token);
                var start = 0;
                if (match is not null && match.PreviousEnd > strips[^1].Area.Top)
                {
                    var last = strips[^1];
                    strips[^1] = last with { Area = new Rectangle(0, last.Area.Top, last.Area.Width, match.PreviousEnd - last.Area.Top) };
                    start = match.CurrentEnd;
                    result.JoinedPairs++;
                }
                else if (i > 0) result.UncertainPairs++;
                strips.Add(new Strip(current, new Rectangle(0, start, current.Width, current.Height - start), i == 0 || start == 0));
                previousRows = rows;
            }

            var group = new List<Strip>();
            var height = 0;
            void Flush()
            {
                if (group.Count == 0) return;
                var image = new Bitmap(group[0].Area.Width, height);
                try
                {
                    using var g = Graphics.FromImage(image);
                    var y = 0;
                    foreach (var strip in group)
                    {
                        // Copy at native resolution; do not shrink names or SSR codes.
                        g.DrawImage(strip.Image, new Rectangle(0, y, strip.Area.Width, strip.Area.Height), strip.Area, GraphicsUnit.Pixel);
                        y += strip.Area.Height;
                    }
                    result.Pages.Add(image);
                }
                catch { image.Dispose(); throw; }
                group.Clear(); height = 0;
            }
            foreach (var strip in strips)
            {
                token.ThrowIfCancellationRequested();
                if (strip.NewPage || (group.Count > 0 && height + strip.Area.Height > MaxJoinedHeight)) Flush();
                group.Add(strip); height += strip.Area.Height;
            }
            Flush();
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static Rows Describe(Bitmap image, CancellationToken token)
    {
        var hashes = new ulong[image.Height];
        var ink = new bool[image.Height];
        // Ignore the vertical scrollbar, which moves independently of table text.
        var endX = Math.Max(1, image.Width - 24);
        for (var y = 0; y < image.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            ulong hash = 14695981039346656037UL;
            var dark = 0; var count = 0;
            for (var x = 0; x < endX; x += 2)
            {
                var c = image.GetPixel(x, y);
                hash = unchecked((hash ^ (uint)(c.ToArgb() & 0xffffff)) * 1099511628211UL);
                if (c.R + c.G + c.B < 480) dark++;
                count++;
            }
            hashes[y] = hash;
            ink[y] = dark >= 3 && dark < count * 0.65;
        }
        return new Rows(hashes, ink);
    }

    private static Match? FindOverlap(Rows previous, Rows current, CancellationToken token)
    {
        var height = previous.Hash.Length;
        var candidates = new List<Match>();
        // Require a substantial, unique text-bearing band near the bottom/top.
        for (var shift = 12; shift <= height - 60; shift++)
        {
            token.ThrowIfCancellationRequested();
            var runStart = shift;
            for (var y = shift; y <= height; y++)
            {
                if (y < height && previous.Hash[y] == current.Hash[y - shift]) continue;
                var length = y - runStart;
                if (length >= 60 && height - y <= Math.Max(40, height / 12) && runStart - shift <= Math.Max(80, height / 5))
                {
                    var uniqueInk = new HashSet<ulong>();
                    for (var row = runStart; row < y; row++)
                        if (previous.Ink[row]) uniqueInk.Add(previous.Hash[row]);
                    if (uniqueInk.Count >= 12) candidates.Add(new Match(y, y - shift, uniqueInk.Count));
                }
                runStart = y + 1;
            }
        }
        var ranked = candidates.OrderByDescending(m => m.Score).ToList();
        if (ranked.Count == 0 || (ranked.Count > 1 && ranked[1].Score >= ranked[0].Score * 0.8)) return null;
        return ranked[0];
    }

    public void Dispose()
    {
        foreach (var page in Pages) page.Dispose();
        Pages.Clear();
    }
}
