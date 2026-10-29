namespace Nesting2D.Core;

/// <summary>Hướng ưu tiên lấp khổ: giảm chiều dài đã dùng hoặc giảm bề rộng đã dùng.</summary>
public enum NestDirection
{
    /// <summary>Ưu tiên xếp ngang qua bề rộng hữu dụng, nhằm hạn chế chiều dài vật liệu phải cấp.</summary>
    FillWidth,
    /// <summary>Ưu tiên xếp dọc theo chiều dài hữu dụng, nhằm hạn chế bề rộng khổ bị chiếm.</summary>
    FillLength
}

public sealed class NestOptions
{
    /// <summary>Trục ưu tiên. FillWidth giảm chiều dài đã dùng; FillLength giảm bề rộng đã dùng.</summary>
    public NestDirection Direction { get; set; } = NestDirection.FillWidth;

    /// <summary>Cho phép xoay nếu từng chi tiết cũng cho phép.</summary>
    public bool AllowRotation { get; set; } = true;

    /// <summary>Giới hạn thời gian tìm kiếm, tính bằng giây.</summary>
    public double MaxSeconds { get; set; } = 30;

    /// <summary>Hiệu suất mục tiêu theo phần trăm; 0 tắt điều kiện dừng theo mục tiêu.</summary>
    public double TargetUtilizationPercent { get; set; } = 85;

    /// <summary>Bước góc xoay (độ).</summary>
    public double RotationStep { get; set; } = 90;

    public CancellationToken Cancel { get; set; }
    public Action<string>? Report { get; set; }
}

/// <summary>
/// Engine nesting hình dạng thật. Xếp theo lưới bảo thủ và xác minh lại va chạm hình học; không sử dụng hộp bao
/// như phương pháp xếp hoặc phương án thay thế khi contour không hợp lệ.
/// </summary>
public static class NestEngine
{
    public static NestResult Run(IReadOnlyList<PartDefinition> parts, SheetDefinition sheet, NestOptions? options = null)
    {
        options ??= new NestOptions();
        var invalid = new List<string>();
        foreach (var part in parts)
        {
            if (part.Shape == null || part.Quantity <= 0) continue;
            var loops = ShapeOps.Build(part.Shape);
            if (!loops.Reliable)
                invalid.Add($"{part.Name}: contour không hợp lệ — {loops.Problem ?? "không tìm thấy đường biên kín"}.");
        }

        if (invalid.Count > 0)
        {
            var rejected = new NestResult { Strategy = "Không thể nesting: có contour DXF lỗi." };
            rejected.Unplaced.AddRange(invalid);
            rejected.Notes.Add("Đã dừng trước khi xếp vì có chi tiết không thể xác minh contour. Hãy sửa DXF hoặc chọn lại layer rồi chạy lại.");
            return rejected;
        }

        return RasterNester.Run(parts, sheet, options);
    }
}
