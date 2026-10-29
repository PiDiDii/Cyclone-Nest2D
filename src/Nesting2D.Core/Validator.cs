namespace Nesting2D.Core;

/// <summary>
/// Kiểm tra độc lập kết quả nesting: trong tấm, không chồng, đủ khe hở.
/// Chi tiết có hình thật được kiểm tra bằng hình học chính xác (khoảng cách giữa các đường biên và quan hệ chứa nhau),
/// chi tiết chữ nhật kiểm tra bằng hộp bao. Khe hở cho phép sai số <see cref="RasterNester.GapTol"/> (do rời rạc hóa cung tròn).
/// </summary>
public static class Validator
{
    public static List<string> Check(NestResult result, SheetDefinition sheet)
    {
        const double tol = 1e-6;
        var errors = new List<string>();

        foreach (var s in result.Sheets)
        {
            var ps = s.Placements;

            double sheetW = s.Width > 0 ? s.Width : sheet.Width;
            double sheetH = s.Height > 0 ? s.Height : sheet.Height;
            foreach (var p in ps)
            {
                if (p.X < sheet.Margin - tol || p.Y < sheet.Margin - tol ||
                    p.X + p.Width > sheetW - sheet.Margin + tol ||
                    p.Y + p.Height > sheetH - sheet.Margin + tol)
                {
                    errors.Add($"Tấm {s.Index}: {p.PartName} #{p.Instance} vượt vùng cho phép.");
                }
                if (p.Shape != null && !ShapeOps.Build(p.Shape.Place(p.X, p.Y, p.Rotated), p.Width, p.Height).Reliable)
                    errors.Add($"Tấm {s.Index}: {p.PartName} #{p.Instance} có contour không hợp lệ; không thể xác minh an toàn.");
            }

            double half = sheet.Spacing / 2 - tol;
            double gap = Math.Max(0, sheet.Spacing - RasterNester.GapTol);
            var loops = new Dictionary<Placement, LoopSet>();

            for (int i = 0; i < ps.Count; i++)
            {
                for (int j = i + 1; j < ps.Count; j++)
                {
                    var a = ps[i];
                    var b = ps[j];
                    bool near = !(a.X >= b.X + b.Width + sheet.Spacing || b.X >= a.X + a.Width + sheet.Spacing ||
                                  a.Y >= b.Y + b.Height + sheet.Spacing || b.Y >= a.Y + a.Height + sheet.Spacing);
                    if (!near) continue;

                    bool bad;
                    if (a.Shape == null && b.Shape == null)
                        bad = Inflate(a, half).Intersects(Inflate(b, half));
                    else
                    {
                        var la = LoopsOf(a, loops);
                        var lb = LoopsOf(b, loops);
                        if (!la.Reliable || !lb.Reliable) continue;
                        bad = ShapeOps.Conflict(la, 0, 0, lb, 0, 0, gap);
                    }

                    if (bad)
                    {
                        errors.Add($"Tấm {s.Index}: {a.PartName} #{a.Instance} và " +
                                   $"{b.PartName} #{b.Instance} quá gần/chồng nhau.");
                    }
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Kiểm tra một vị trí ứng viên trên tấm (bỏ qua chi tiết có chỉ số ignoreIndex, thường là chính nó khi kéo/xoay).
    /// Trả về null nếu hợp lệ, ngược lại là lý do bằng tiếng Việt.
    /// </summary>
    public static string? CheckPlacement(SheetResult sheetResult, int ignoreIndex, Placement cand, SheetDefinition sheet)
    {
        const double tol = 1e-6;
        if (cand.X < sheet.Margin - tol || cand.Y < sheet.Margin - tol ||
            cand.X + cand.Width > sheet.Width - sheet.Margin + tol ||
            cand.Y + cand.Height > sheet.Height - sheet.Margin + tol)
            return "vượt ra ngoài vùng cho phép của tấm (lề)";

        var cache = new Dictionary<Placement, LoopSet>();
        var candidateLoops = LoopsOf(cand, cache);
        if (!candidateLoops.Reliable)
            return $"{cand.PartName}: contour không hợp lệ, không thể kiểm tra vị trí.";

        double half = sheet.Spacing / 2 - tol;
        double gap = Math.Max(0, sheet.Spacing - RasterNester.GapTol);
        var ps = sheetResult.Placements;

        for (int i = 0; i < ps.Count; i++)
        {
            if (i == ignoreIndex) continue;
            var o = ps[i];
            bool near = !(cand.X >= o.X + o.Width + sheet.Spacing || o.X >= cand.X + cand.Width + sheet.Spacing ||
                          cand.Y >= o.Y + o.Height + sheet.Spacing || o.Y >= cand.Y + cand.Height + sheet.Spacing);
            if (!near) continue;

            bool bad;
            if (cand.Shape == null && o.Shape == null)
                bad = Inflate(cand, half).Intersects(Inflate(o, half));
            else
            {
                var otherLoops = LoopsOf(o, cache);
                if (!otherLoops.Reliable) return $"{o.PartName} #{o.Instance}: contour không hợp lệ, không thể kiểm tra vị trí.";
                bad = ShapeOps.Conflict(candidateLoops, 0, 0, otherLoops, 0, 0, gap);
            }
            if (bad) return $"quá gần hoặc chồng lên {o.PartName} #{o.Instance}";
        }
        return null;
    }

    /// <summary>Đường biên thật của chi tiết đã đặt; nếu contour không tin cậy, caller phải từ chối phép kiểm tra.</summary>
    private static LoopSet LoopsOf(Placement p, Dictionary<Placement, LoopSet> cache)
    {
        if (cache.TryGetValue(p, out var ls)) return ls;

        if (p.Shape == null)
        {
            ls = LoopSet.FromRect(p.Width, p.Height);
            Shift(ls, p.X, p.Y);
        }
        else
        {
            ls = ShapeOps.Build(p.Shape.Place(p.X, p.Y, p.Rotated), p.Width, p.Height);
        }

        cache[p] = ls;
        return ls;
    }

    private static void Shift(LoopSet ls, double dx, double dy)
    {
        for (int i = 0; i < ls.Loops.Count; i++)
        {
            var (xs, ys) = ls.Loops[i];
            for (int k = 0; k < xs.Length; k++) { xs[k] += dx; ys[k] += dy; }
        }
        ls.Finish();
    }

    private static Rect Inflate(Placement p, double d) =>
        new(p.X - d, p.Y - d, p.Width + 2 * d, p.Height + 2 * d);
}
