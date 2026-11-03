namespace CycloneNest2D.Core;

/// <summary>Đoạn thẳng thuộc loop đa giác với hộp bao và vector được tính sẵn để tăng tốc kiểm tra va chạm.</summary>
public readonly record struct LoopSegment(
    double X1, double Y1, double X2, double Y2,
    double MinX, double MaxX, double MinY, double MaxY,
    double Vx, double Vy, double Len2);

/// <summary>
/// Các đường biên kín của một chi tiết (đã rời rạc hóa thành đa giác), kèm diện tích vật liệu thật.
/// Loop ngoài + loop lỗ được phân biệt bằng quy tắc even-odd (độ sâu lồng nhau).
/// </summary>
public sealed class LoopSet
{
    public readonly List<(double[] X, double[] Y)> Loops = new();

    /// <summary>true nếu các loop kín và biên ngoài khớp kích thước dự kiến; false thì contour không đủ tin cậy để nesting.</summary>
    public bool Reliable { get; internal set; }

    /// <summary>Số chuỗi đường hở (không tạo thành loop kín) bị bỏ qua.</summary>
    public int OpenChains { get; internal set; }

    public string? Problem { get; internal set; }

    /// <summary>Diện tích vật liệu từ các loop; giá trị chẩn đoán của contour lỗi không được dùng để nesting.</summary>
    public double Area { get; internal set; }

    internal double[] LMinX = Array.Empty<double>(), LMinY = Array.Empty<double>(),
                      LMaxX = Array.Empty<double>(), LMaxY = Array.Empty<double>();
    internal LoopSegment[][] LoopSegments = Array.Empty<LoopSegment[]>();

    /// <summary>Tạo contour chữ nhật thực sự cho part chữ nhật đơn giản.</summary>
    public static LoopSet FromRect(double w, double h, string? problem = null)
    {
        var ls = new LoopSet { Reliable = true, Area = w * h, Problem = problem };
        ls.Loops.Add((new[] { 0.0, w, w, 0.0 }, new[] { 0.0, 0.0, h, h }));
        ls.Finish();
        return ls;
    }

    internal void Finish()
    {
        int n = Loops.Count;
        LMinX = new double[n]; LMinY = new double[n]; LMaxX = new double[n]; LMaxY = new double[n];
        LoopSegments = new LoopSegment[n][];
        for (int i = 0; i < n; i++)
        {
            var (xs, ys) = Loops[i];
            int count = xs.Length;
            double a = double.MaxValue, b = double.MaxValue, c = double.MinValue, d = double.MinValue;
            var segs = new LoopSegment[count];
            for (int k = 0, prev = count - 1; k < count; prev = k++)
            {
                double x1 = xs[prev], y1 = ys[prev];
                double x2 = xs[k], y2 = ys[k];
                if (x2 < a) a = x2;
                if (x2 > c) c = x2;
                if (y2 < b) b = y2;
                if (y2 > d) d = y2;

                double vx = x2 - x1, vy = y2 - y1;
                double len2 = vx * vx + vy * vy;
                segs[k] = new LoopSegment(
                    x1, y1, x2, y2,
                    Math.Min(x1, x2), Math.Max(x1, x2),
                    Math.Min(y1, y2), Math.Max(y1, y2),
                    vx, vy, len2);
            }
            LMinX[i] = a; LMinY[i] = b; LMaxX[i] = c; LMaxY[i] = d;
            LoopSegments[i] = segs;
        }
    }

    /// <summary>Điểm (x, y) có nằm trong vật liệu không (even-odd), loop dịch chuyển (dx, dy).</summary>
    public bool ContainsMaterial(double x, double y, double dx = 0, double dy = 0)
    {
        bool inside = false;
        foreach (var (xs, ys) in Loops)
        {
            int n = xs.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double yi = ys[i] + dy, yj = ys[j] + dy;
                if ((yi > y) != (yj > y))
                {
                    double xi = xs[i] + dx, xj = xs[j] + dx;
                    if (x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
                }
            }
        }
        return inside;
    }
}

public static class ShapeOps
{
    private const double ChordTol = 0.05; // sai số dây cung khi rời rạc hóa cung tròn (mm)
    private const double JoinTol = 0.02;  // hai đầu mút gần hơn mức này được coi là nối nhau (mm)

    // ---------- Xoay ----------

    /// <summary>Xoay hình quanh gốc rồi dời để góc dưới-trái hộp bao về (0,0).</summary>
    public static DxfShape Rotate(DxfShape s, double deg)
    {
        double a = Mat.Norm360(deg);
        if (a < 1e-12 || a > 360 - 1e-12) return s;

        double c, sn;
        if (Math.Abs(a - 90) < 1e-12) { c = 0; sn = 1; }
        else if (Math.Abs(a - 180) < 1e-12) { c = -1; sn = 0; }
        else if (Math.Abs(a - 270) < 1e-12) { c = 0; sn = -1; }
        else { c = Math.Cos(a * Math.PI / 180); sn = Math.Sin(a * Math.PI / 180); }
        double rad = a * Math.PI / 180;

        (double X, double Y) R(double x, double y) => (c * x - sn * y, sn * x + c * y);

        var rotated = new List<Seg>(s.Segments.Count);
        foreach (var seg in s.Segments)
        {
            switch (seg)
            {
                case LineSeg l:
                    {
                        var p = R(l.X1, l.Y1); var q = R(l.X2, l.Y2);
                        rotated.Add(new LineSeg(p.X, p.Y, q.X, q.Y, l.Layer));
                        break;
                    }
                case CircleSeg ci:
                    {
                        var p = R(ci.Cx, ci.Cy);
                        rotated.Add(new CircleSeg(p.X, p.Y, ci.R, ci.Layer));
                        break;
                    }
                case ArcSeg ar:
                    {
                        var p = R(ar.Cx, ar.Cy);
                        rotated.Add(new ArcSeg(p.X, p.Y, ar.R, ar.StartRad + rad, ar.SweepRad, ar.Layer));
                        break;
                    }
            }
        }

        var tmp = new DxfShape(s.Name, rotated, s.UnitScale, s.Layers, s.Warnings, s.ApproximatedEntities, s.LayerColors);
        double ox = -tmp.MinX, oy = -tmp.MinY;
        var moved = new List<Seg>(rotated.Count);
        foreach (var seg in rotated)
        {
            moved.Add(seg switch
            {
                LineSeg l => new LineSeg(l.X1 + ox, l.Y1 + oy, l.X2 + ox, l.Y2 + oy, l.Layer),
                CircleSeg ci => new CircleSeg(ci.Cx + ox, ci.Cy + oy, ci.R, ci.Layer),
                ArcSeg ar => new ArcSeg(ar.Cx + ox, ar.Cy + oy, ar.R, ar.StartRad, ar.SweepRad, ar.Layer),
                _ => seg
            });
        }
        var texts = s.Texts.Select(text =>
        {
            var point = R(text.X, text.Y);
            double? alignmentX = null, alignmentY = null;
            if (text.AlignmentX is { } alignX && text.AlignmentY is { } alignY)
            {
                var alignment = R(alignX, alignY);
                alignmentX = alignment.X + ox;
                alignmentY = alignment.Y + oy;
            }
            return text with
            {
                X = point.X + ox,
                Y = point.Y + oy,
                Rotation = text.Rotation + a,
                AlignmentX = alignmentX,
                AlignmentY = alignmentY
            };
        }).ToList();
        return new DxfShape(s.Name, moved, s.UnitScale, s.Layers, s.Warnings, s.ApproximatedEntities,
            s.LayerColors, texts);
    }

    /// <summary>Phản chiếu chi tiết trái-phải quanh trục dọc đi qua tâm hộp bao.</summary>
    public static DxfShape MirrorHorizontal(DxfShape shape)
    {
        double axis = shape.MinX + shape.MaxX;
        var mirrored = new List<Seg>(shape.Segments.Count);
        foreach (var seg in shape.Segments)
        {
            mirrored.Add(seg switch
            {
                LineSeg l => new LineSeg(axis - l.X1, l.Y1, axis - l.X2, l.Y2, l.Layer),
                CircleSeg c => new CircleSeg(axis - c.Cx, c.Cy, c.R, c.Layer),
                ArcSeg a => new ArcSeg(axis - a.Cx, a.Cy, a.R, Math.PI - a.StartRad, -a.SweepRad, a.Layer),
                _ => seg
            });
        }
        var texts = shape.Texts.Select(text => text with
        {
            X = axis - text.X,
            AlignmentX = text.AlignmentX is { } alignX ? axis - alignX : null
        }).ToList();
        return new DxfShape(shape.Name, mirrored, shape.UnitScale, shape.Layers, shape.Warnings,
            shape.ApproximatedEntities, shape.LayerColors, texts);
    }

    public static DxfShape RectShape(string name, double w, double h) =>
        new(name, new Seg[]
        {
            new LineSeg(0, 0, w, 0), new LineSeg(w, 0, w, h), new LineSeg(w, h, 0, h), new LineSeg(0, h, 0, 0)
        }, 1.0, new Dictionary<string, int>(), Array.Empty<string>(), 0);

    // ---------- Dựng contour kín ----------

    private sealed class Edge
    {
        public double[] Px = Array.Empty<double>(), Py = Array.Empty<double>();
        public int N0, N1;
    }

    /// <summary>Số đoạn để rời rạc hóa cung bán kính r, góc quét sweep (rad) sao cho sai số dây cung &lt;= ChordTol.</summary>
    private static int ArcSteps(double r, double sweepAbs)
    {
        double dTheta = r <= ChordTol ? Math.PI / 2 : 2 * Math.Acos(Math.Max(-1, 1 - ChordTol / r));
        dTheta = Math.Clamp(dTheta, 0.01, Math.PI / 2);
        return Math.Max(2, (int)Math.Ceiling(sweepAbs / dTheta));
    }

    public static LoopSet Build(DxfShape shape) => Build(shape.Segments, shape.Width, shape.Height);

    /// <summary>
    /// Nối các đoạn thành loop kín. Kết quả không tin cậy (Reliable=false) nếu có điểm giao &gt; 2 đường,
    /// hoặc các loop ngoài không khớp hộp bao mong đợi (biên ngoài bị hở).
    /// </summary>
    public static LoopSet Build(IEnumerable<Seg> segs, double expectW, double expectH)
    {
        var result = new LoopSet();
        var edges = new List<Edge>();
        var nodes = new List<(double X, double Y)>();
        var grid = new Dictionary<(long, long), List<int>>();

        int NodeId(double x, double y)
        {
            long gx = (long)Math.Floor(x / JoinTol), gy = (long)Math.Floor(y / JoinTol);
            for (long ix = gx - 1; ix <= gx + 1; ix++)
                for (long iy = gy - 1; iy <= gy + 1; iy++)
                {
                    if (!grid.TryGetValue((ix, iy), out var lst)) continue;
                    foreach (int id in lst)
                        if (Math.Abs(nodes[id].X - x) <= JoinTol && Math.Abs(nodes[id].Y - y) <= JoinTol) return id;
                }
            int nid = nodes.Count;
            nodes.Add((x, y));
            if (!grid.TryGetValue((gx, gy), out var list)) grid[(gx, gy)] = list = new List<int>();
            list.Add(nid);
            return nid;
        }

        foreach (var seg in segs)
        {
            switch (seg)
            {
                case LineSeg l:
                    {
                        if (Math.Abs(l.X1 - l.X2) < JoinTol && Math.Abs(l.Y1 - l.Y2) < JoinTol) break; // đoạn suy biến
                        edges.Add(new Edge
                        {
                            Px = new[] { l.X1, l.X2 },
                            Py = new[] { l.Y1, l.Y2 },
                            N0 = NodeId(l.X1, l.Y1),
                            N1 = NodeId(l.X2, l.Y2)
                        });
                        break;
                    }
                case CircleSeg c:
                    {
                        int n = Math.Max(8, ArcSteps(c.R, 2 * Math.PI));
                        var xs = new double[n]; var ys = new double[n];
                        for (int i = 0; i < n; i++)
                        {
                            double t = 2 * Math.PI * i / n;
                            xs[i] = c.Cx + c.R * Math.Cos(t);
                            ys[i] = c.Cy + c.R * Math.Sin(t);
                        }
                        result.Loops.Add((xs, ys));
                        break;
                    }
                case ArcSeg a:
                    {
                        double sweep = a.SweepRad;
                        if (Math.Abs(sweep) >= 2 * Math.PI - 1e-9)
                        {
                            int n = Math.Max(8, ArcSteps(a.R, 2 * Math.PI));
                            var xs = new double[n]; var ys = new double[n];
                            for (int i = 0; i < n; i++)
                            {
                                double t = a.StartRad + 2 * Math.PI * i / n * Math.Sign(sweep);
                                xs[i] = a.Cx + a.R * Math.Cos(t);
                                ys[i] = a.Cy + a.R * Math.Sin(t);
                            }
                            result.Loops.Add((xs, ys));
                            break;
                        }
                        int steps = ArcSteps(a.R, Math.Abs(sweep));
                        var px = new double[steps + 1]; var py = new double[steps + 1];
                        for (int i = 0; i <= steps; i++)
                        {
                            double t = a.StartRad + sweep * i / steps;
                            px[i] = a.Cx + a.R * Math.Cos(t);
                            py[i] = a.Cy + a.R * Math.Sin(t);
                        }
                        int n0 = NodeId(px[0], py[0]), n1 = NodeId(px[steps], py[steps]);
                        edges.Add(new Edge { Px = px, Py = py, N0 = n0, N1 = n1 });
                        break;
                    }
            }
        }

        // Danh sách kề: mỗi nút -> (cạnh, đầu 0/1)
        var adj = new List<(int Edge, int End)>[nodes.Count];
        for (int i = 0; i < adj.Length; i++) adj[i] = new List<(int, int)>();
        for (int e = 0; e < edges.Count; e++)
        {
            adj[edges[e].N0].Add((e, 0));
            adj[edges[e].N1].Add((e, 1));
        }

        bool junction = adj.Any(a => a.Count > 2);
        if (junction) result.Problem = "có điểm giao của hơn 2 đường (đường chồng nhau hoặc cắt nhau)";

        var used = new bool[edges.Count];
        int open = 0;
        for (int e0 = 0; e0 < edges.Count && !junction; e0++)
        {
            if (used[e0]) continue;
            var e = edges[e0];
            used[e0] = true;
            var xs = new List<double>(e.Px);
            var ys = new List<double>(e.Py);
            int start = e.N0, cur = e.N1, prev = e0;
            bool ok = true;

            while (cur != start)
            {
                int pick = -1, pickEnd = 0;
                foreach (var (ed, end) in adj[cur])
                {
                    if (ed == prev) continue;
                    pick = ed; pickEnd = end;
                    break;
                }
                if (pick < 0 || used[pick]) { ok = false; break; }

                used[pick] = true;
                var f = edges[pick];
                if (pickEnd == 0)
                {
                    for (int i = 1; i < f.Px.Length; i++) { xs.Add(f.Px[i]); ys.Add(f.Py[i]); }
                    cur = f.N1;
                }
                else
                {
                    for (int i = f.Px.Length - 2; i >= 0; i--) { xs.Add(f.Px[i]); ys.Add(f.Py[i]); }
                    cur = f.N0;
                }
                prev = pick;
            }

            if (ok && xs.Count >= 4)
            {
                xs.RemoveAt(xs.Count - 1); // điểm cuối trùng điểm đầu
                ys.RemoveAt(ys.Count - 1);
                result.Loops.Add((xs.ToArray(), ys.ToArray()));
            }
            else open++;
        }
        result.OpenChains = open;

        // Phân loại độ sâu lồng nhau + diện tích even-odd
        int nl = result.Loops.Count;
        double area = 0;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i < nl; i++)
        {
            var (xs, ys) = result.Loops[i];
            int depth = 0;
            for (int j = 0; j < nl; j++)
                if (j != i && PointInPoly(xs[0], ys[0], result.Loops[j].X, result.Loops[j].Y)) depth++;

            double a = PolyArea(xs, ys);
            area += depth % 2 == 0 ? a : -a;

            if (depth == 0)
            {
                foreach (double x in xs) { if (x < minX) minX = x; if (x > maxX) maxX = x; }
                foreach (double y in ys) { if (y < minY) minY = y; if (y > maxY) maxY = y; }
            }
        }

        result.Area = area;
        result.Finish();

        if (junction) { result.Reliable = false; return result; }
        if (nl == 0) { result.Problem ??= "không tìm thấy contour kín nào"; result.Reliable = false; return result; }

        double dw = Math.Abs((maxX - minX) - expectW), dh = Math.Abs((maxY - minY) - expectH);
        if (dw > 0.5 || dh > 0.5)
        {
            result.Problem = "biên ngoài không kín (hở) nên không khớp hộp bao";
            result.Reliable = false;
            return result;
        }

        result.Reliable = true;
        return result;
    }

    /// <summary>Diện tích vật liệu thật của chi tiết (mm²); caller phải từ chối contour không tin cậy.</summary>
    public static double NetArea(DxfShape s)
    {
        var ls = Build(s);
        return ls.Reliable && ls.Area > 0 ? ls.Area : s.Width * s.Height;
    }

    private static double PolyArea(double[] xs, double[] ys)
    {
        double a = 0;
        for (int i = 0, j = xs.Length - 1; i < xs.Length; j = i++) a += (xs[j] * ys[i]) - (xs[i] * ys[j]);
        return Math.Abs(a) / 2;
    }

    private static bool PointInPoly(double x, double y, double[] xs, double[] ys)
    {
        bool inside = false;
        for (int i = 0, j = xs.Length - 1; i < xs.Length; j = i++)
            if ((ys[i] > y) != (ys[j] > y) && x < (xs[j] - xs[i]) * (y - ys[i]) / (ys[j] - ys[i]) + xs[i])
                inside = !inside;
        return inside;
    }

    // ---------- Xung đột giữa 2 chi tiết ----------

    /// <summary>
    /// true nếu 2 chi tiết (loop a dịch (ax,ay), loop b dịch (bx,by)) chồng nhau, chứa nhau trong phần vật liệu,
    /// hoặc có khoảng cách nhỏ hơn minGap giữa các đường biên.
    /// </summary>
    public static bool Conflict(LoopSet a, double ax, double ay, LoopSet b, double bx, double by, double minGap)
    {
        double g2 = minGap * minGap;
        double dx = bx - ax, dy = by - ay;

        for (int i = 0; i < a.Loops.Count; i++)
        {
            for (int j = 0; j < b.Loops.Count; j++)
            {
                if (a.LMaxX[i] + minGap < b.LMinX[j] + dx || b.LMaxX[j] + dx + minGap < a.LMinX[i] ||
                    a.LMaxY[i] + minGap < b.LMinY[j] + dy || b.LMaxY[j] + dy + minGap < a.LMinY[i])
                    continue;

                var segsA = a.LoopSegments[i];
                var segsB = b.LoopSegments[j];
                int na = segsA.Length, nb = segsB.Length;

                for (int p = 0; p < na; p++)
                {
                    ref readonly var sa = ref segsA[p];
                    double sMinX = sa.MinX - minGap, sMaxX = sa.MaxX + minGap;
                    double sMinY = sa.MinY - minGap, sMaxY = sa.MaxY + minGap;

                    for (int q = 0; q < nb; q++)
                    {
                        ref readonly var sb = ref segsB[q];
                        if (sb.MaxX + dx < sMinX || sb.MinX + dx > sMaxX ||
                            sb.MaxY + dy < sMinY || sb.MinY + dy > sMaxY) continue;
                        if (SegIntersect(sa.X1, sa.Y1, sa.X2, sa.Y2,
                            sb.X1 + dx, sb.Y1 + dy, sb.X2 + dx, sb.Y2 + dy))
                            return true;
                        if (minGap > 1e-9 && SegDist2(sa, sb, dx, dy) <= g2) return true;
                    }
                }
            }
        }

        return HasMaterialOverlap(a, ax, ay, b, bx, by) ||
               HasMaterialOverlap(b, bx, by, a, ax, ay);
    }

    private static bool HasMaterialOverlap(LoopSet source, double sx, double sy, LoopSet other, double ox, double oy)
    {
        foreach (var (xs, ys) in source.Loops)
        {
            for (int i = 0, j = xs.Length - 1; i < xs.Length; j = i++)
            {
                double dx = xs[i] - xs[j], dy = ys[i] - ys[j];
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= 1e-12) continue;
                double epsilon = Math.Max(1e-9, Math.Min(1e-4, length * 1e-6));
                double mx = (xs[i] + xs[j]) / 2 + sx;
                double my = (ys[i] + ys[j]) / 2 + sy;
                double nx = -dy / length * epsilon, ny = dx / length * epsilon;
                if (ContainsMaterialSample(mx + nx, my + ny, xs, ys, source, sx, sy, other, ox, oy) ||
                    ContainsMaterialSample(mx - nx, my - ny, xs, ys, source, sx, sy, other, ox, oy))
                    return true;
            }
        }
        return false;
    }

    private static bool ContainsMaterialSample(double x, double y, double[] xs, double[] ys,
        LoopSet source, double sx, double sy, LoopSet other, double ox, double oy) =>
        PointInPoly(x - sx, y - sy, xs, ys) &&
        source.ContainsMaterial(x, y, sx, sy) &&
        other.ContainsMaterial(x - ox, y - oy);

    public static bool ContainsInnerContour(DxfShape shape, double x, double y)
        => ContainsInnerContour(Build(shape), x, y);

    public static bool ContainsInnerContour(LoopSet loops, double x, double y)
    {
        if (loops.Loops.Count < 2) return false;
        var areas = loops.Loops.Select(loop =>
        {
            double area = 0;
            var (xs, ys) = loop;
            for (int i = 0, j = xs.Length - 1; i < xs.Length; j = i++)
                area += xs[j] * ys[i] - xs[i] * ys[j];
            return Math.Abs(area / 2);
        }).ToArray();
        double largest = areas.Max();
        double tolerance = Math.Max(1e-8, largest * 1e-10);
        for (int i = 0; i < areas.Length; i++)
        {
            if (areas[i] >= largest - tolerance) continue;
            var (xs, ys) = loops.Loops[i];
            if (PointInPoly(x, y, xs, ys)) return true;
        }
        return false;
    }

    private static double SegDist2(in LoopSegment sa, in LoopSegment sb, double dx, double dy)
    {
        double bx1 = sb.X1 + dx, by1 = sb.Y1 + dy;
        double bx2 = sb.X2 + dx, by2 = sb.Y2 + dy;
        if (SegIntersect(sa.X1, sa.Y1, sa.X2, sa.Y2, bx1, by1, bx2, by2)) return 0;
        return Math.Min(
            Math.Min(PtSeg2Shifted(sa.X1, sa.Y1, sb, dx, dy), PtSeg2Shifted(sa.X2, sa.Y2, sb, dx, dy)),
            Math.Min(PtSeg2(bx1, by1, sa), PtSeg2(bx2, by2, sa)));
    }

    private static double PtSeg2(double px, double py, in LoopSegment seg)
    {
        double len2 = seg.Len2;
        double t = len2 < 1e-18 ? 0 : Math.Clamp(((px - seg.X1) * seg.Vx + (py - seg.Y1) * seg.Vy) / len2, 0, 1);
        double qx = seg.X1 + t * seg.Vx - px, qy = seg.Y1 + t * seg.Vy - py;
        return qx * qx + qy * qy;
    }

    private static double PtSeg2Shifted(double px, double py, in LoopSegment seg, double dx, double dy)
    {
        double sx1 = seg.X1 + dx, sy1 = seg.Y1 + dy;
        double len2 = seg.Len2;
        double t = len2 < 1e-18 ? 0 : Math.Clamp(((px - sx1) * seg.Vx + (py - sy1) * seg.Vy) / len2, 0, 1);
        double qx = sx1 + t * seg.Vx - px, qy = sy1 + t * seg.Vy - py;
        return qx * qx + qy * qy;
    }

    private static bool SegIntersect(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        double d1 = Orient(cx, cy, dx, dy, ax, ay), d2 = Orient(cx, cy, dx, dy, bx, by);
        double d3 = Orient(ax, ay, bx, by, cx, cy), d4 = Orient(ax, ay, bx, by, dx, dy);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static double Orient(double ax, double ay, double bx, double by, double px, double py) =>
        (bx - ax) * (py - ay) - (by - ay) * (px - ax);
}
