namespace CycloneNest2D.Core;

/// <summary>Chỉnh tay một chi tiết đã đặt (xoay 90°, dời chỗ) mà vẫn giữ biểu diễn nhất quán giữa 2 bộ nesting.</summary>
public static class PlacementOps
{
    /// <summary>Hình đã xoay đúng hướng đặt (góc dưới-trái hộp bao ở gốc); null nếu chi tiết chỉ là hộp chữ nhật.</summary>
    public static DxfShape? Oriented(Placement p) =>
        p.Shape == null ? null : p.Rotated ? ShapeOps.Rotate(p.Shape, 90) : p.Shape;

    public static Placement Moved(Placement p, double x, double y) => p with { X = x, Y = y };

    /// <summary>Xoay thêm 90° ngược chiều kim đồng hồ quanh tâm hộp bao. Kết quả có Shape đã xoay sẵn, Rotated=false.</summary>
    public static Placement Rotated90(Placement p)
    {
        var shape = Oriented(p) is { } o ? ShapeOps.Rotate(o, 90) : null;
        double w = shape?.Width ?? p.Height;
        double h = shape?.Height ?? p.Width;
        double cx = p.X + p.Width / 2, cy = p.Y + p.Height / 2;
        double angle = Mat.Norm360(p.AngleDeg + (p.Rotated ? 90 : 0) + 90);
        return p with { Shape = shape, Rotated = false, AngleDeg = angle, Width = w, Height = h, X = cx - w / 2, Y = cy - h / 2 };
    }
}
