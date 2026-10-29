namespace Nesting2D.Core;

/// <summary>Hộp bao (bounding box) cộng dồn.</summary>
public struct Bbox
{
    public double MinX, MinY, MaxX, MaxY;

    public static Bbox Empty => new()
    {
        MinX = double.PositiveInfinity, MinY = double.PositiveInfinity,
        MaxX = double.NegativeInfinity, MaxY = double.NegativeInfinity
    };

    public readonly bool IsEmpty => MinX > MaxX || MinY > MaxY;
    public readonly double Width => IsEmpty ? 0 : MaxX - MinX;
    public readonly double Height => IsEmpty ? 0 : MaxY - MinY;

    public void Add(double x, double y)
    {
        if (x < MinX) MinX = x;
        if (x > MaxX) MaxX = x;
        if (y < MinY) MinY = y;
        if (y > MaxY) MaxY = y;
    }

    public void Add(Bbox o)
    {
        if (o.IsEmpty) return;
        Add(o.MinX, o.MinY);
        Add(o.MaxX, o.MaxY);
    }

    public readonly bool Contains(Bbox o, double tol) =>
        o.IsEmpty || (!IsEmpty &&
                      o.MinX >= MinX - tol && o.MaxX <= MaxX + tol &&
                      o.MinY >= MinY - tol && o.MaxY <= MaxY + tol);
}

/// <summary>Hình học 2D thuần: LINE, CIRCLE, ARC (CCW), đa tuyến (đã rời rạc hóa).</summary>
public abstract record Shape(string Layer)
{
    public abstract void Extend(ref Bbox b);

    public Bbox Bounds()
    {
        var b = Bbox.Empty;
        Extend(ref b);
        return b;
    }
}

public sealed record LineShape(string Layer, double X1, double Y1, double X2, double Y2) : Shape(Layer)
{
    public override void Extend(ref Bbox b)
    {
        b.Add(X1, Y1);
        b.Add(X2, Y2);
    }
}

public sealed record CircleShape(string Layer, double Cx, double Cy, double R) : Shape(Layer)
{
    public override void Extend(ref Bbox b)
    {
        b.Add(Cx - R, Cy - R);
        b.Add(Cx + R, Cy + R);
    }
}

/// <summary>Cung tròn ngược chiều kim đồng hồ từ StartDeg, quét SweepDeg (0..360].</summary>
public sealed record ArcShape(string Layer, double Cx, double Cy, double R, double StartDeg, double SweepDeg) : Shape(Layer)
{
    public override void Extend(ref Bbox b)
    {
        double a0 = StartDeg * Math.PI / 180.0;
        double a1 = (StartDeg + SweepDeg) * Math.PI / 180.0;
        b.Add(Cx + R * Math.Cos(a0), Cy + R * Math.Sin(a0));
        b.Add(Cx + R * Math.Cos(a1), Cy + R * Math.Sin(a1));

        // Các điểm cực trị (0°, 90°, 180°, 270°) nằm trong cung.
        for (int k = 0; k < 4; k++)
        {
            double d = Mat.Norm360(90.0 * k - StartDeg);
            if (d > SweepDeg + 1e-9) continue;
            switch (k)
            {
                case 0: b.Add(Cx + R, Cy); break;
                case 1: b.Add(Cx, Cy + R); break;
                case 2: b.Add(Cx - R, Cy); break;
                default: b.Add(Cx, Cy - R); break;
            }
        }
    }
}

public sealed record PolyShape(string Layer, (double X, double Y)[] Pts, bool Closed) : Shape(Layer)
{
    public override void Extend(ref Bbox b)
    {
        foreach (var (x, y) in Pts) b.Add(x, y);
    }
}

/// <summary>Biến đổi affine 2D: x' = A x + B y + Tx, y' = C x + D y + Ty.</summary>
public readonly record struct Mat(double A, double B, double C, double D, double Tx, double Ty)
{
    public static Mat Identity => new(1, 0, 0, 1, 0, 0);
    public static Mat MirrorX => new(-1, 0, 0, 1, 0, 0);

    public static Mat Translate(double dx, double dy) => new(1, 0, 0, 1, dx, dy);
    public static Mat Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    public static Mat Rotate(double deg)
    {
        double a = Norm360(deg);
        double c, s;
        if (a == 0) { c = 1; s = 0; }
        else if (a == 90) { c = 0; s = 1; }
        else if (a == 180) { c = -1; s = 0; }
        else if (a == 270) { c = 0; s = -1; }
        else
        {
            double r = a * Math.PI / 180.0;
            c = Math.Cos(r);
            s = Math.Sin(r);
        }
        return new Mat(c, -s, s, c, 0, 0);
    }

    public (double X, double Y) Apply(double x, double y) => (A * x + B * y + Tx, C * x + D * y + Ty);

    /// <summary>(m * n).Apply(p) = m.Apply(n.Apply(p)).</summary>
    public static Mat operator *(Mat m, Mat n) => new(
        m.A * n.A + m.B * n.C, m.A * n.B + m.B * n.D,
        m.C * n.A + m.D * n.C, m.C * n.B + m.D * n.D,
        m.A * n.Tx + m.B * n.Ty + m.Tx, m.C * n.Tx + m.D * n.Ty + m.Ty);

    public double Det => A * D - B * C;

    public static double Norm360(double deg)
    {
        deg %= 360.0;
        if (deg < 0) deg += 360.0;
        return deg;
    }

    /// <summary>Phép đồng dạng (xoay + tỉ lệ đều + có thể lật) giữ nguyên được cung tròn.</summary>
    private bool IsSimilarity(out double scale, out double rotDeg)
    {
        double s1 = A * A + C * C, s2 = B * B + D * D, dot = A * B + C * D;
        scale = Math.Sqrt(s1);
        rotDeg = Math.Atan2(C, A) * 180.0 / Math.PI;
        if (s1 < 1e-24) return false;
        return Math.Abs(s1 - s2) <= 1e-9 * s1 && Math.Abs(dot) <= 1e-9 * s1;
    }

    /// <summary>Biến đổi một hình. Cung/tròn bị kéo giãn không đều sẽ được rời rạc hóa thành đa tuyến.</summary>
    public Shape? Transform(Shape s)
    {
        switch (s)
        {
            case LineShape l:
            {
                var (x1, y1) = Apply(l.X1, l.Y1);
                var (x2, y2) = Apply(l.X2, l.Y2);
                return l with { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
            }
            case PolyShape p:
            {
                var pts = new (double X, double Y)[p.Pts.Length];
                for (int i = 0; i < pts.Length; i++) pts[i] = Apply(p.Pts[i].X, p.Pts[i].Y);
                return p with { Pts = pts };
            }
            case CircleShape c:
            {
                if (IsSimilarity(out double sc, out _))
                {
                    var (cx, cy) = Apply(c.Cx, c.Cy);
                    return c with { Cx = cx, Cy = cy, R = c.R * sc };
                }
                var pts = Discretize(c.Cx, c.Cy, c.R, 0, 360);
                return new PolyShape(c.Layer, pts[..^1], true);
            }
            case ArcShape a:
            {
                if (IsSimilarity(out double sc, out double rot))
                {
                    var (cx, cy) = Apply(a.Cx, a.Cy);
                    double start = Det > 0 ? a.StartDeg + rot : rot - (a.StartDeg + a.SweepDeg);
                    return a with { Cx = cx, Cy = cy, R = a.R * sc, StartDeg = Norm360(start) };
                }
                return new PolyShape(a.Layer, Discretize(a.Cx, a.Cy, a.R, a.StartDeg, a.SweepDeg), false);
            }
            default:
                return null;
        }
    }

    private (double X, double Y)[] Discretize(double cx, double cy, double r, double startDeg, double sweepDeg)
    {
        int n = Math.Max(8, (int)Math.Ceiling(sweepDeg / 1.0));
        var pts = new (double X, double Y)[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double ang = (startDeg + sweepDeg * i / n) * Math.PI / 180.0;
            pts[i] = Apply(cx + r * Math.Cos(ang), cy + r * Math.Sin(ang));
        }
        return pts;
    }
}
