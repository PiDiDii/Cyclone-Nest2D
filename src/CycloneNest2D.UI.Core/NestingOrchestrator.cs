using CycloneNest2D.Core;

namespace CycloneNest2D.UI;

/// <summary>
/// Coordinates the preflight checks and material grouping used before a nest run.
/// </summary>
public sealed class NestingOrchestrator
{
    public IReadOnlyList<IGrouping<(string Material, double Thickness), PartItem>> GroupMaterialThicknesses(IEnumerable<PartItem> parts)
    {
        return parts.Where(p => p.Quantity > 0 && p.IsValid)
            .GroupBy(p => (Material: MaterialKey(p.Material), Thickness: Math.Round(p.Thickness, 4)))
            .OrderBy(g => g.Key.Material, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.Thickness)
            .ToList();
    }

    public List<string> BuildPreflightIssues(IEnumerable<PartItem> parts, NestSettings settings, IEnumerable<SheetPreset>? validSheets = null)
    {
        var preflight = new List<string>();
        var usable = parts.Where(p => p.IsValid && p.Quantity > 0).ToList();
        var sheets = (validSheets ?? settings.SheetPresets.Where(s => !string.IsNullOrWhiteSpace(s.Material) &&
                !string.Equals(s.Material, "Chưa phân loại", StringComparison.OrdinalIgnoreCase) && s.Thickness > 0 &&
                s.Width > 2 * settings.Margin && s.Height > 2 * settings.Margin)).ToList();

        foreach (var p in usable)
        {
            if (string.IsNullOrWhiteSpace(p.Material))
                preflight.Add($"{p.Name}: hãy chọn vật liệu/độ dày từ Danh sách khổ tấm.");
            if (!(p.Thickness > 0))
                preflight.Add($"{p.Name}: cấu hình đã chọn chưa có độ dày hợp lệ.");
            if (p.IsDxf)
            {
                if (p.Geometry == null)
                    preflight.Add($"{p.Name}: không có geometry DXF hợp lệ; không thể nesting.");
                else
                {
                    var contour = ShapeOps.Build(p.Geometry);
                    if (!contour.Reliable)
                        preflight.Add($"{p.Name}: contour DXF lỗi — {contour.Problem ?? "không tìm thấy đường biên kín"}. Sửa DXF hoặc chọn lại layer trước khi nesting.");
                }
            }
        }

        foreach (var group in GroupMaterialThicknesses(usable))
        {
            if (string.IsNullOrWhiteSpace(group.First().Material) || group.Key.Thickness <= 0) continue;
            bool hasSheet = sheets.Any(sh => MaterialKey(sh.Material) == group.Key.Material && Math.Round(sh.Thickness, 4) == group.Key.Thickness);
            if (!hasSheet)
                preflight.Add($"{group.First().Material} · {group.Key.Thickness:0.####} mm: chưa có khổ tấm khả dụng tương ứng trong Danh sách khổ tấm.");
        }

        return preflight;
    }

    public static string MaterialKey(string? material) => (material ?? string.Empty).Trim().ToUpperInvariant();
}
