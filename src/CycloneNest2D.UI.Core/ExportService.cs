using System.Globalization;
using System.Text;
using CycloneNest2D.Core;

namespace CycloneNest2D.UI;

public static class ExportService
{
    public static List<string> ExportPatterns(IReadOnlyList<NestingPatternItem> patterns, SheetDefinition definition,
        string folder, DxfExportOptions options)
    {
        Directory.CreateDirectory(folder);
        var files = new List<string>(patterns.Count);
        foreach (var pattern in patterns)
        {
            string stem = Path.Combine(folder, SafeFileName(pattern.Name));
            var sheet = pattern.Sheet;
            var sheetDefinition = new SheetDefinition(sheet.Width, sheet.Height, definition.Margin, definition.Spacing);
            DxfWriter.Write(sheet, sheetDefinition, stem + ".dxf", options);
            files.Add(stem + ".dxf");
        }
        return files;
    }

    public static string SafeFileName(string name)
    {
        const string windowsInvalid = "<>:\"/\\|?*";
        char[] invalid = Path.GetInvalidFileNameChars().Concat(windowsInvalid).Distinct().ToArray();
        string safe = string.Concat(name.Select(character => Array.IndexOf(invalid, character) >= 0 ? '_' : character))
            .TrimEnd(' ', '.');
        return string.IsNullOrWhiteSpace(safe) ? "nest" : safe;
    }

    /// <summary>Xuất một DXF gộp nhiều nest hoặc từng tấm theo lựa chọn người dùng.</summary>
    public static List<string> ExportSheets(NestResult result, SheetDefinition def, string destination, DxfExportOptions options)
    {
        var files = new List<string>();
        if (options.FileLayout == DxfFileLayout.SingleFile)
        {
            string path = destination.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) ? destination : destination + ".dxf";
            DxfWriter.Write(result.Sheets, path, options, def.Margin);
            files.Add(path);
            return files;
        }

        Directory.CreateDirectory(destination);
        foreach (var sheet in result.Sheets)
        {
            string stem = Path.Combine(destination, $"tam_{sheet.Index:00}");
            DxfWriter.Write(sheet, new SheetDefinition(sheet.Width, sheet.Height, def.Margin, def.Spacing), stem + ".dxf", options);
            files.Add(stem + ".dxf");
        }
        return files;
    }

    /// <summary>Mỗi tấm 1 file DXF. Trả về danh sách file đã ghi.</summary>
    public static List<string> ExportSheets(NestResult result, SheetDefinition def, string folder, bool labels, string prefix = "tam")
    {
        Directory.CreateDirectory(folder);
        var files = new List<string>();
        foreach (var s in result.Sheets)
        {
            string stem = Path.Combine(folder, $"{prefix}_{s.Index:00}");
            var sheetDef = new SheetDefinition(s.Width, s.Height, def.Margin, def.Spacing);
            DxfWriter.Write(s, sheetDef, stem + ".dxf", labels);
            files.Add(stem + ".dxf");
        }
        return files;
    }
}

public static class ReportBuilder
{
    private static string F(double v, string fmt = "0.##") => v.ToString(fmt, CultureInfo.InvariantCulture);

    public static string Summary(NestResult r, SheetDefinition def)
    {
        var sb = new StringBuilder();
        if (r.Sheets.Count == 1)
            sb.AppendLine($"Tấm {F(r.Sheets[0].Width)} × {F(r.Sheets[0].Height)} mm, lề {F(def.Margin)}, khe hở {F(def.Spacing)}");
        else if (r.Sheets.Count > 1)
        {
            int sizeKinds = r.Sheets.Select(s => F(s.Width) + "×" + F(s.Height)).Distinct().Count();
            sb.AppendLine($"Kết quả dùng {sizeKinds} loại khổ sheet, lề {F(def.Margin)}, khe hở {F(def.Spacing)}");
        }
        else
            sb.AppendLine($"Khổ tham chiếu {F(def.Width)} × {F(def.Height)} mm, lề {F(def.Margin)}, khe hở {F(def.Spacing)}");
        sb.AppendLine($"Số tấm: {r.Sheets.Count}, hiệu suất chung {r.Utilization:P1}");
        foreach (var s in r.Sheets)
            if (!string.IsNullOrWhiteSpace(s.Material))
            {
                string thickness = F(s.Thickness, "0.###");
                sb.AppendLine($"Tấm {s.Index}: {s.Material} · {thickness} mm · {s.StockName} · {F(s.Width)} × {F(s.Height)} mm");
            }
        if (r.Strategy.Length > 0) sb.AppendLine($"Phương án: {r.Strategy}");
        if (r.Unplaced.Count > 0) sb.AppendLine($"Part còn chưa xếp: {r.Unplaced.Count} dòng (xem số lượng cụ thể trong ghi chú)");
        return sb.ToString().TrimEnd();
    }
}
