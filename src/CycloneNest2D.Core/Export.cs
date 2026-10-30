using System.Globalization;
using System.Text;

namespace CycloneNest2D.Core;

public static class DxfWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Layer chứa đường cắt thật của chi tiết đọc từ DXF.</summary>
    public const string CutLayer = "CUT";

    /// <summary>Ghi DXF theo cấu hình xuất nâng cao.</summary>
    public static void Write(SheetResult sheet, SheetDefinition def, string path, DxfExportOptions options) =>
        DxfExportWriter.Write(new[] { sheet }, path, options, def.Margin);

    /// <summary>Ghi nhiều nest trong cùng một tệp DXF.</summary>
    public static void Write(IReadOnlyList<SheetResult> sheets, string path, DxfExportOptions options, double margin = 0) =>
        DxfExportWriter.Write(sheets, path, options, margin);

    /// <summary>
    /// Ghi 1 tấm ra DXF R12 theo layer và màu nguồn của chi tiết DXF.
    /// Layer PARTS chứa ô chữ nhật tạo trong ứng dụng; SHEET và LABELS có thể tắt/bỏ qua khi cắt.
    /// </summary>
    public static void Write(SheetResult sheet, SheetDefinition def, string path, bool labels = true)
    {
        DxfExportWriter.Write(new[] { sheet }, path, new DxfExportOptions
        {
            AddPartLabels = labels,
            OutputSheets = true,
            TargetVersion = DxfTargetVersion.R12
        }, def.Margin);
    }

    /// <summary>DXF R12 dùng ANSI: bỏ dấu tiếng Việt để nhãn không bị lỗi font.</summary>
    internal static string ToAscii(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c < 128 ? c : '_');
        }
        return sb.ToString();
    }
}
