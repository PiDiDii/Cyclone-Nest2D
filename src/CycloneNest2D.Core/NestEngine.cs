namespace CycloneNest2D.Core;

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
    public const int MaxPartInstances = 250_000;

    public static NestResult Run(IReadOnlyList<PartDefinition> parts, SheetDefinition sheet, NestOptions? options = null)
    {
        options ??= new NestOptions();
        if (options.Cancel.IsCancellationRequested)
            return Interrupted(parts, "Đã hủy nesting theo yêu cầu.");

        string? validationError = ValidateRequest(sheet, options);
        long totalInstances = 0;
        foreach (var part in parts)
            if (part.Quantity > 0) totalInstances += part.Quantity;
        if (validationError == null && totalInstances > MaxPartInstances)
            validationError = $"Tổng số chi tiết vượt giới hạn {MaxPartInstances:N0} cho mỗi lần nesting.";
        if (validationError != null)
            return Rejected(parts, validationError);

        var invalid = new List<string>();
        foreach (var part in parts)
        {
            if (options.Cancel.IsCancellationRequested)
                return Interrupted(parts, "Đã hủy nesting theo yêu cầu.");

            if (part.Quantity <= 0) continue;
            if (!double.IsFinite(part.Width) || !double.IsFinite(part.Height) ||
                part.Width <= 0 || part.Height <= 0)
            {
                invalid.Add($"{part.Name}: kích thước phải là số hữu hạn lớn hơn 0.");
                continue;
            }
            if (part.Shape == null) continue;

            var loops = ShapeOps.Build(part.Shape);
            if (options.Cancel.IsCancellationRequested)
                return Interrupted(parts, "Đã hủy nesting theo yêu cầu.");

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

    private static string? ValidateRequest(SheetDefinition sheet, NestOptions options)
    {
        if (!double.IsFinite(sheet.Width) || !double.IsFinite(sheet.Height) ||
            sheet.Width <= 0 || sheet.Height <= 0)
            return "Kích thước tấm phải là số hữu hạn lớn hơn 0.";
        if (!double.IsFinite(sheet.Width * sheet.Height))
            return "Diện tích tấm vượt giới hạn số học được hỗ trợ.";
        if (!double.IsFinite(sheet.Margin) || sheet.Margin < 0 ||
            !double.IsFinite(sheet.Spacing) || sheet.Spacing < 0)
            return "Lề và khoảng cách giữa các chi tiết phải là số hữu hạn không âm.";
        if (2 * sheet.Margin >= Math.Min(sheet.Width, sheet.Height))
            return "Khoảng cách lề quá lớn so với kích thước tấm.";
        if (!double.IsFinite(options.RotationStep) || options.RotationStep < 5 || options.RotationStep > 180)
            return "Bước xoay phải nằm trong 5 đến 180 độ.";
        if (!Enum.IsDefined(options.Direction))
            return "Hướng lấp đầy không hợp lệ.";
        return null;
    }

    private static NestResult Rejected(IReadOnlyList<PartDefinition> parts, string message)
    {
        var result = new NestResult { Strategy = "Không thể nesting: dữ liệu đầu vào không hợp lệ." };
        foreach (var part in parts.Where(part => part.Quantity > 0))
            result.Unplaced.Add($"{part.Name} ×{part.Quantity}: {message}");
        result.Notes.Add(message);
        return result;
    }

    private static NestResult Interrupted(IReadOnlyList<PartDefinition> parts, string message)
    {
        var result = new NestResult { Strategy = "Đã hủy nesting." };
        foreach (var part in parts.Where(part => part.Quantity > 0))
            result.Unplaced.Add($"{part.Name} ×{part.Quantity}: chưa xử lý vì nesting đã bị hủy.");
        result.Notes.Add(message);
        return result;
    }
}
