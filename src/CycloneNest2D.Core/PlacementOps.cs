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
        => Rotated90(p, p.X + p.Width / 2, p.Y + p.Height / 2);

    /// <summary>Rotates 90 degrees while keeping the world point under the cursor fixed.</summary>
    public static Placement Rotated90(Placement p, double anchorX, double anchorY)
    {
        var source = Oriented(p);
        var shape = source is null ? null : ShapeOps.Rotate(source, 90);
        double w = shape?.Width ?? p.Height;
        double h = shape?.Height ?? p.Width;
        double localX = anchorX - p.X, localY = anchorY - p.Y;
        double sourceHeight = source?.Height ?? p.Height;
        double angle = Mat.Norm360(p.AngleDeg + (p.Rotated ? 90 : 0) + 90);
        return p with
        {
            Shape = shape,
            Rotated = false,
            AngleDeg = angle,
            Width = w,
            Height = h,
            X = anchorX - (sourceHeight - localY),
            Y = anchorY - localX
        };
    }

    /// <summary>Mirrors horizontally while keeping the world point under the cursor fixed.</summary>
    public static Placement Mirrored(Placement p, double anchorX, double anchorY)
        => p with
        {
            Shape = p.Shape == null ? null : ShapeOps.MirrorHorizontal(p.Shape),
            X = anchorX - (p.Width - (anchorX - p.X))
        };
}
