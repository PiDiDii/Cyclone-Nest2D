namespace CycloneNest2D.Core;

/// <summary>Hình chữ nhật trục song song, gốc ở góc dưới-trái.</summary>
public readonly record struct Rect(double X, double Y, double W, double H)
{
    private const double Eps = 1e-9;

    public double Right => X + W;
    public double Top => Y + H;
    public double Area => W * H;

    public bool Contains(Rect o) =>
        o.X >= X - Eps && o.Y >= Y - Eps && o.Right <= Right + Eps && o.Top <= Top + Eps;

    public bool Intersects(Rect o) =>
        o.X < Right - Eps && o.Right > X + Eps && o.Y < Top - Eps && o.Top > Y + Eps;
}

/// <summary>Một loại chi tiết cần cắt (đơn vị mm).</summary>
public sealed record PartDefinition(string Name, double Width, double Height, int Quantity, bool CanRotate = true,
                                    DxfShape? Shape = null);

/// <summary>
/// Tấm vật liệu. Margin = lề không cắt quanh mép tấm.
/// Spacing = khoảng cách tối thiểu giữa 2 chi tiết.
/// </summary>
public sealed record SheetDefinition(double Width, double Height, double Margin, double Spacing)
{
    public double UsableWidth => Width - 2 * Margin;
    public double UsableHeight => Height - 2 * Margin;
}

/// <summary>
/// Vị trí đặt 1 chi tiết trên tấm. X, Y là góc dưới-trái hộp bao của chi tiết (đã cộng lề); Width/Height là kích thước hộp bao
/// SAU khi xoay.
/// Shape mang hình học đã chuẩn hóa theo hướng đặt; AngleDeg lưu góc xoay để hiển thị/xuất báo cáo.
/// Rotated chỉ là cờ tương thích cho thao tác xoay 90° thủ công. Area là diện tích vật liệu thật (mm²).
/// </summary>
public sealed record Placement(string PartName, int Instance, double X, double Y, double Width, double Height, bool Rotated,
                               DxfShape? Shape = null, double AngleDeg = 0, double Area = 0)
{
    public bool IsRotated => Rotated || Math.Abs(AngleDeg) > 1e-9;
    public double NetArea => Area > 0 ? Area : Width * Height;
}

public sealed class SheetResult
{
    public int Index { get; set; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string Material { get; init; } = "";
    public double Thickness { get; init; }
    public string StockName { get; init; } = "";
    public List<Placement> Placements { get; } = new();

    /// <summary>Tổng diện tích vật liệu thật; part hình chữ nhật dùng diện tích chữ nhật của chính part đó.</summary>
    public double UsedArea => Placements.Sum(p => p.NetArea);
    public double Utilization => UsedArea / (Width * Height);

    /// <summary>Cạnh trên cao nhất của chi tiết (mm tính từ đáy tấm): càng thấp thì phần dư phía trên càng lớn.</summary>
    public double TopY => Placements.Count == 0 ? 0 : Placements.Max(p => p.Y + p.Height);
}

public sealed class NestResult
{
    public List<SheetResult> Sheets { get; } = new();

    /// <summary>Các chi tiết không thể đặt (quá khổ tấm).</summary>
    public List<string> Unplaced { get; } = new();

    /// <summary>Ghi chú người dùng về hướng lấp, hiệu suất, giới hạn thời gian và dữ liệu contour.</summary>
    public List<string> Notes { get; } = new();

    public string Strategy { get; set; } = "";

    /// <summary>Độ cao đã dùng của tấm cuối (mm). Dùng để so sánh 2 phương án có cùng số tấm.</summary>
    public double LastSheetTop => Sheets.Count == 0 ? 0 : Sheets[^1].TopY;

    public double TotalPartArea => Sheets.Sum(s => s.UsedArea);
    public double TotalSheetArea => Sheets.Sum(s => s.Width * s.Height);
    public double Utilization => TotalSheetArea > 0 ? TotalPartArea / TotalSheetArea : 0;
}
