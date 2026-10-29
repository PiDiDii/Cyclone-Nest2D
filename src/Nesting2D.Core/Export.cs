using System.Globalization;
using System.Text;

namespace Nesting2D.Core;

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
    /// Ghi 1 tấm ra DXF R12 (ASCII, chỉ LINE/ARC/CIRCLE/TEXT nên ZWCAD, AutoCAD, mọi phần mềm CAD đều đọc được).
    /// Layer: SHEET (biên tấm), CUT (đường cắt thật), PARTS (ô chữ nhật cho chi tiết chữ nhật), LABELS (tên chi tiết).
    /// Khi cắt, tắt/bỏ qua SHEET và LABELS.
    /// </summary>
    public static void Write(SheetResult sheet, SheetDefinition def, string path, bool labels = true)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        w.NewLine = "\r\n";

        void G(int code, string value)
        {
            w.WriteLine(code.ToString(Inv).PadLeft(3));
            w.WriteLine(value);
        }
        void N(int code, double v) => G(code, v.ToString("0.######", Inv));

        // HEADER
        G(0, "SECTION"); G(2, "HEADER"); G(9, "$ACADVER"); G(1, "AC1009");
        G(0, "ENDSEC"); // R12 không có $INSUNITS: bản vẽ xuất ra luôn tính bằng mm

        // TABLES
        var layers = new[] { ("SHEET", 5), (CutLayer, 7), ("PARTS", 7), ("LABELS", 3) };
        G(0, "SECTION"); G(2, "TABLES");
        G(0, "TABLE"); G(2, "LTYPE"); G(70, "1");
        G(0, "LTYPE"); G(2, "CONTINUOUS"); G(70, "0"); G(3, "Solid line"); G(72, "65"); G(73, "0"); N(40, 0);
        G(0, "ENDTAB");
        G(0, "TABLE"); G(2, "LAYER"); G(70, layers.Length.ToString(Inv));
        foreach (var (name, color) in layers)
        {
            G(0, "LAYER"); G(2, name); G(70, "0"); G(62, color.ToString(Inv)); G(6, "CONTINUOUS");
        }
        G(0, "ENDTAB");
        G(0, "ENDSEC");

        // ENTITIES
        G(0, "SECTION"); G(2, "ENTITIES");

        void Line(string layer, double x1, double y1, double x2, double y2)
        {
            G(0, "LINE"); G(8, layer);
            N(10, x1); N(20, y1); N(30, 0);
            N(11, x2); N(21, y2); N(31, 0);
        }
        void Box(string layer, double x, double y, double bw, double bh)
        {
            Line(layer, x, y, x + bw, y);
            Line(layer, x + bw, y, x + bw, y + bh);
            Line(layer, x + bw, y + bh, x, y + bh);
            Line(layer, x, y + bh, x, y);
        }
        void Text(string layer, double x, double y, double height, string text)
        {
            G(0, "TEXT"); G(8, layer);
            N(10, x); N(20, y); N(30, 0);
            N(40, height); G(1, text);
        }

        Box("SHEET", 0, 0, def.Width, def.Height);

        foreach (var p in sheet.Placements)
        {
            if (p.Shape != null)
            {
                foreach (var seg in p.Shape.Place(p.X, p.Y, p.Rotated))
                {
                    switch (seg)
                    {
                        case LineSeg l:
                            Line(CutLayer, l.X1, l.Y1, l.X2, l.Y2);
                            break;
                        case CircleSeg c:
                            G(0, "CIRCLE"); G(8, CutLayer);
                            N(10, c.Cx); N(20, c.Cy); N(30, 0); N(40, c.R);
                            break;
                        case ArcSeg a:
                        {
                            // DXF: cung luôn ngược chiều kim đồng hồ từ góc đầu đến góc cuối.
                            double s0 = a.SweepRad >= 0 ? a.StartRad : a.StartRad + a.SweepRad;
                            double sw = Math.Abs(a.SweepRad);
                            double d0 = Mat.Norm360(s0 * 180 / Math.PI);
                            double d1 = Mat.Norm360((s0 + sw) * 180 / Math.PI);
                            G(0, "ARC"); G(8, CutLayer);
                            N(10, a.Cx); N(20, a.Cy); N(30, 0); N(40, a.R);
                            N(50, d0); N(51, d1);
                            break;
                        }
                    }
                }
            }
            else
            {
                Box("PARTS", p.X, p.Y, p.Width, p.Height);
            }

            if (labels)
            {
                double th = Math.Max(1.0, Math.Min(Math.Min(p.Width, p.Height) / 5, 20));
                string label = ToAscii(p.PartName) + (p.IsRotated ? " (R)" : "");
                Text("LABELS", p.X + th * 0.5, p.Y + p.Height / 2 - th / 2, th, label);
            }
        }

        G(0, "ENDSEC");
        G(0, "EOF");
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
