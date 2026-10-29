namespace Nesting2D.Core;

/// <summary>Đoạn hình học cơ sở của một chi tiết (đơn vị mm). Polyline/ellipse/spline đều được đổi về LINE/ARC.</summary>
public abstract record Seg;

public sealed record LineSeg(double X1, double Y1, double X2, double Y2) : Seg;

/// <summary>Cung tròn: bắt đầu tại StartRad, quét SweepRad (có dấu: dương = ngược chiều kim đồng hồ).</summary>
public sealed record ArcSeg(double Cx, double Cy, double R, double StartRad, double SweepRad) : Seg;

public sealed record CircleSeg(double Cx, double Cy, double R) : Seg;

internal static class Geo
{
    public const double TwoPi = Math.PI * 2;

    public static double Mod2Pi(double a)
    {
        a %= TwoPi;
        if (a < 0) a += TwoPi;
        return a;
    }

    public static (double MinX, double MinY, double MaxX, double MaxY) ArcBounds(ArcSeg a)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

        void Add(double ang)
        {
            double x = a.Cx + a.R * Math.Cos(ang);
            double y = a.Cy + a.R * Math.Sin(ang);
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        Add(a.StartRad);
        Add(a.StartRad + a.SweepRad);

        // Các điểm cực trị theo trục (0°, 90°, 180°, 270°) nằm trong góc quét
        for (int k = 0; k < 4; k++)
        {
            double ang = k * Math.PI / 2;
            double delta = a.SweepRad >= 0 ? Mod2Pi(ang - a.StartRad) : Mod2Pi(a.StartRad - ang);
            if (delta <= Math.Abs(a.SweepRad)) Add(ang);
        }

        return (minX, minY, maxX, maxY);
    }
}

/// <summary>Hình học thật của 1 chi tiết đọc từ DXF, kèm khung bao (bounding box).</summary>
public sealed class DxfShape
{
    public string Name { get; }
    public IReadOnlyList<Seg> Segments { get; }
    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;

    /// <summary>Hệ số đổi đơn vị gốc của file sang mm đã áp dụng.</summary>
    public double UnitScale { get; }

    /// <summary>Số đối tượng hình học theo layer (trước khi lọc layer) để người dùng chọn layer.</summary>
    public IReadOnlyDictionary<string, int> Layers { get; }

    public List<string> Warnings { get; }

    /// <summary>Số đối tượng (ellipse/spline) được xấp xỉ bằng nhiều đoạn thẳng.</summary>
    public int ApproximatedEntities { get; }

    public DxfShape(string name, IReadOnlyList<Seg> segments, double unitScale,
                    IReadOnlyDictionary<string, int> layers, IEnumerable<string> warnings, int approximated)
    {
        if (segments.Count == 0) throw new ArgumentException("Chi tiết không có hình học.", nameof(segments));

        Name = name;
        Segments = segments;
        UnitScale = unitScale;
        Layers = layers;
        Warnings = warnings.ToList();
        ApproximatedEntities = approximated;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

        void Inc(double x, double y)
        {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        foreach (var s in segments)
        {
            switch (s)
            {
                case LineSeg l:
                    Inc(l.X1, l.Y1);
                    Inc(l.X2, l.Y2);
                    break;
                case CircleSeg c:
                    Inc(c.Cx - c.R, c.Cy - c.R);
                    Inc(c.Cx + c.R, c.Cy + c.R);
                    break;
                case ArcSeg a:
                    var b = Geo.ArcBounds(a);
                    Inc(b.MinX, b.MinY);
                    Inc(b.MaxX, b.MaxY);
                    break;
            }
        }

        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    /// <summary>
    /// Đặt chi tiết lên tấm: góc dưới-trái của khung bao nằm tại (x, y).
    /// rotated = xoay 90° ngược chiều kim đồng hồ quanh góc đó (khung bao sau xoay rộng = Height, cao = Width).
    /// </summary>
    public IEnumerable<Seg> Place(double x, double y, bool rotated)
    {
        double h = Height;
        double dAng = rotated ? Math.PI / 2 : 0;

        (double X, double Y) T(double px, double py)
        {
            double lx = px - MinX;
            double ly = py - MinY;
            return rotated ? (x + h - ly, y + lx) : (x + lx, y + ly);
        }

        foreach (var s in Segments)
        {
            switch (s)
            {
                case LineSeg l:
                {
                    var p1 = T(l.X1, l.Y1);
                    var p2 = T(l.X2, l.Y2);
                    yield return new LineSeg(p1.X, p1.Y, p2.X, p2.Y);
                    break;
                }
                case CircleSeg c:
                {
                    var p = T(c.Cx, c.Cy);
                    yield return new CircleSeg(p.X, p.Y, c.R);
                    break;
                }
                case ArcSeg a:
                {
                    var p = T(a.Cx, a.Cy);
                    yield return new ArcSeg(p.X, p.Y, a.R, a.StartRad + dAng, a.SweepRad);
                    break;
                }
            }
        }
    }
}
