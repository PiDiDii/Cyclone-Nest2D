namespace Nesting2D.Core;

/// <summary>Creates uncut bridges by omitting short intervals from the outer contour during DXF export.</summary>
internal static class MicroJointProcessor
{
    private sealed record Edge(int Index, double Length, int Capacity);

    public static List<Seg> Apply(Placement placement, IReadOnlyList<Seg> placedSegments, double widthMm, int requestedCount)
    {
        if (widthMm <= 0 || requestedCount <= 0 || !double.IsFinite(widthMm))
            return placedSegments.ToList();

        var loops = ShapeOps.Build(placedSegments, placement.Width, placement.Height);
        if (!loops.Reliable || loops.Loops.Count == 0) return placedSegments.ToList();
        var depths = Enumerable.Range(0, loops.Loops.Count).Select(i => Depth(loops, i)).ToArray();
        var candidates = new List<Edge>();
        for (int i = 0; i < placedSegments.Count; i++)
        {
            var seg = placedSegments[i];
            double length = Length(seg);
            if (length < widthMm * 1.5) continue;
            if (placement.Shape != null)
            {
                int loopIndex = NearestLoop(loops, Midpoint(seg));
                if (loopIndex < 0 || depths[loopIndex] % 2 != 0) continue;
            }
            int capacity = Math.Max(1, (int)Math.Floor(length / (widthMm * 1.5)));
            candidates.Add(new Edge(i, length, capacity));
        }
        if (candidates.Count == 0) return placedSegments.ToList();

        double totalLength = candidates.Sum(e => e.Length);
        var allocations = candidates.ToDictionary(e => e.Index,
            e => Math.Min(e.Capacity, (int)Math.Floor(requestedCount * e.Length / totalLength)));
        int remaining = Math.Max(0, requestedCount - allocations.Values.Sum());
        foreach (var edge in candidates.OrderByDescending(e => requestedCount * e.Length / totalLength - Math.Floor(requestedCount * e.Length / totalLength)))
        {
            if (remaining == 0) break;
            if (allocations[edge.Index] >= edge.Capacity) continue;
            allocations[edge.Index]++;
            remaining--;
        }
        // When the requested count exceeds each segment's safe capacity, distribute extras in passes.
        while (remaining > 0)
        {
            bool added = false;
            foreach (var edge in candidates.OrderByDescending(e => e.Length))
            {
                if (remaining == 0) break;
                if (allocations[edge.Index] >= edge.Capacity) continue;
                allocations[edge.Index]++;
                remaining--;
                added = true;
            }
            if (!added) break;
        }

        var result = new List<Seg>(placedSegments.Count + requestedCount * 2);
        for (int i = 0; i < placedSegments.Count; i++)
        {
            int count = allocations.GetValueOrDefault(i);
            if (count == 0) result.Add(placedSegments[i]);
            else result.AddRange(Split(placedSegments[i], widthMm, count));
        }
        return result;
    }

    private static IEnumerable<Seg> Split(Seg seg, double width, int count)
    {
        double length = Length(seg);
        count = Math.Min(count, Math.Max(1, (int)Math.Floor(length / (width * 1.5))));
        var gaps = new List<(double Start, double End)>(count);
        for (int i = 0; i < count; i++)
        {
            double center = (i + 0.5) * length / count;
            double half = width / 2;
            gaps.Add((Math.Max(0, center - half), Math.Min(length, center + half)));
        }
        double cursor = 0;
        foreach (var gap in gaps)
        {
            if (gap.Start > cursor + 1e-9) yield return Portion(seg, cursor, gap.Start, length);
            cursor = Math.Max(cursor, gap.End);
        }
        if (cursor < length - 1e-9) yield return Portion(seg, cursor, length, length);
    }

    private static Seg Portion(Seg seg, double from, double to, double total)
    {
        double a = from / total, b = to / total;
        return seg switch
        {
            LineSeg l => new LineSeg(l.X1 + (l.X2 - l.X1) * a, l.Y1 + (l.Y2 - l.Y1) * a,
                                     l.X1 + (l.X2 - l.X1) * b, l.Y1 + (l.Y2 - l.Y1) * b),
            ArcSeg arc => new ArcSeg(arc.Cx, arc.Cy, arc.R, arc.StartRad + arc.SweepRad * a, arc.SweepRad * (b - a)),
            CircleSeg circle => new ArcSeg(circle.Cx, circle.Cy, circle.R, 2 * Math.PI * a, 2 * Math.PI * (b - a)),
            _ => seg
        };
    }

    private static double Length(Seg seg) => seg switch
    {
        LineSeg l => Math.Sqrt((l.X2 - l.X1) * (l.X2 - l.X1) + (l.Y2 - l.Y1) * (l.Y2 - l.Y1)),
        ArcSeg a => Math.Abs(a.SweepRad) * a.R,
        CircleSeg c => 2 * Math.PI * c.R,
        _ => 0
    };

    private static (double X, double Y) Midpoint(Seg seg) => seg switch
    {
        LineSeg l => ((l.X1 + l.X2) / 2, (l.Y1 + l.Y2) / 2),
        ArcSeg a => (a.Cx + a.R * Math.Cos(a.StartRad + a.SweepRad / 2), a.Cy + a.R * Math.Sin(a.StartRad + a.SweepRad / 2)),
        CircleSeg c => (c.Cx + c.R, c.Cy),
        _ => (0, 0)
    };

    private static int NearestLoop(LoopSet loops, (double X, double Y) point)
    {
        int best = -1;
        double bestDistance = double.PositiveInfinity;
        for (int i = 0; i < loops.Loops.Count; i++)
        {
            var (xs, ys) = loops.Loops[i];
            for (int j = 0; j < xs.Length; j++)
            {
                int k = (j + 1) % xs.Length;
                double distance = PointSegmentDistanceSquared(point.X, point.Y, xs[j], ys[j], xs[k], ys[k]);
                if (distance < bestDistance) { bestDistance = distance; best = i; }
            }
        }
        return best;
    }

    private static int Depth(LoopSet loops, int index)
    {
        var (xs, ys) = loops.Loops[index];
        int depth = 0;
        for (int j = 0; j < loops.Loops.Count; j++)
            if (j != index && Contains(loops.Loops[j].X, loops.Loops[j].Y, xs[0], ys[0])) depth++;
        return depth;
    }

    private static bool Contains(double[] xs, double[] ys, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = xs.Length - 1; i < xs.Length; j = i++)
            if ((ys[i] > y) != (ys[j] > y) && x < (xs[j] - xs[i]) * (y - ys[i]) / (ys[j] - ys[i]) + xs[i]) inside = !inside;
        return inside;
    }

    private static double PointSegmentDistanceSquared(double px, double py, double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1, dy = y2 - y1;
        double length2 = dx * dx + dy * dy;
        double t = length2 <= 1e-18 ? 0 : Math.Clamp(((px - x1) * dx + (py - y1) * dy) / length2, 0, 1);
        double x = x1 + t * dx, y = y1 + t * dy;
        return (px - x) * (px - x) + (py - y) * (py - y);
    }
}
