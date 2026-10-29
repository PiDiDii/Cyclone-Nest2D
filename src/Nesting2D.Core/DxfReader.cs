using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nesting2D.Core;

public sealed class DxfReadOptions
{
    /// <summary>mm trên 1 đơn vị bản vẽ. null = đọc từ $INSUNITS (không khai báo thì giả định mm).</summary>
    public double? UnitScale { get; set; }

    /// <summary>Nếu đặt: chỉ lấy các layer này (không phân biệt hoa thường), bỏ qua mọi quy tắc tự nhận.</summary>
    public HashSet<string>? IncludeLayers { get; set; }

    /// <summary>Layer có tên chứa từ khóa này bị loại (đường uốn, tâm, kích thước, chữ...).</summary>
    public List<string> ExcludeKeywords { get; } = new()
    {
        "CENTER", "TANGENT", "DIM", "TEXT", "NOTE", "DEFPOINTS", "HIDDEN", "CONSTRUCTION", "UNCONSUMED", "BEND"
    };

    public int DefaultQuantity { get; set; } = 1;
}

/// <summary>Một chi tiết đọc từ file DXF, đã đưa về mm và dời góc dưới-trái của hộp bao về (0,0).</summary>
public sealed class DxfPart
{
    public string FileName { get; init; } = "";
    public string Name { get; set; } = "";
    public int Quantity { get; init; } = 1;
    public bool CanRotate { get; init; } = true;
    public double Width => Geometry.Width;
    public double Height => Geometry.Height;
    public double UnitScale { get; init; } = 1;
    public string UnitSource { get; init; } = "";

    /// <summary>Hình học thật (mm, góc dưới-trái hộp bao ở (0,0)) để nesting và xuất DXF.</summary>
    public DxfShape Geometry { get; init; } = null!;

    public IReadOnlyList<string> SizingLayers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OutputLayers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExcludedLayers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public PartDefinition ToDefinition() => new(Name, Width, Height, Quantity, CanRotate, Geometry);
}

/// <summary>Kết quả đọc thô: hình học theo đơn vị gốc của bản vẽ, chưa lọc layer.</summary>
public sealed record DxfRaw(List<Shape> Shapes, double? UnitScale, string UnitText, List<string> Warnings, int Approximated);

/// <summary>
/// Đọc DXF ASCII (R12 đến 2018+) mà không cần thư viện ngoài.
/// Hỗ trợ: LINE, CIRCLE, ARC, LWPOLYLINE (kể cả cung bulge), POLYLINE/VERTEX, ELLIPSE, SPLINE (NURBS), INSERT (block lồng nhau, mảng).
/// </summary>
public static class DxfReader
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---------- API chính ----------

    public static DxfPart Read(string path, DxfReadOptions? options = null)
    {
        options ??= new DxfReadOptions();
        var raw = ReadRaw(path);
        var warnings = new List<string>(raw.Warnings);

        // 1) Phân loại layer
        var present = raw.Shapes.Select(s => s.Layer).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        List<string> output, excluded;
        if (options.IncludeLayers is { Count: > 0 })
        {
            output = present.Where(l => options.IncludeLayers.Contains(l)).ToList();
            excluded = present.Except(output, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var want in options.IncludeLayers)
                if (!present.Contains(want, StringComparer.OrdinalIgnoreCase))
                    warnings.Add($"Layer '{want}' không có trong file.");
        }
        else
        {
            excluded = present.Where(l => options.ExcludeKeywords.Any(k => l.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();
            output = present.Except(excluded, StringComparer.OrdinalIgnoreCase).ToList();
        }

        if (output.Count == 0)
            throw new InvalidDataException(present.Count == 0
                ? "File không có hình học nào đọc được (LINE/ARC/CIRCLE/POLYLINE/SPLINE...)."
                : $"Mọi layer đều bị loại ({string.Join(", ", present)}). Dùng --layers để chỉ định layer cần lấy.");

        // 2) Layer quyết định kích thước: Inventor có IV_OUTER_PROFILE thì chỉ lấy biên ngoài.
        var sizing = output.Where(l => l.Contains("OUTER_PROFILE", StringComparison.OrdinalIgnoreCase)).ToList();
        if (sizing.Count == 0) sizing = output;

        var outSet = new HashSet<string>(output, StringComparer.OrdinalIgnoreCase);
        var sizeSet = new HashSet<string>(sizing, StringComparer.OrdinalIgnoreCase);

        var sizeBox = Bbox.Empty;
        foreach (var s in raw.Shapes)
            if (sizeSet.Contains(s.Layer)) s.Extend(ref sizeBox);

        if (sizeBox.IsEmpty || sizeBox.Width <= 0 || sizeBox.Height <= 0)
            throw new InvalidDataException("Hộp bao của chi tiết có kích thước bằng 0 (chỉ có 1 đường thẳng hoặc điểm?).");

        // 3) Hình ở layer ngoài layer kích thước mà nằm ngoài biên chi tiết (ghi chú vẽ nhầm layer...) bị loại khỏi file xuất,
        //    vì hộp bao không còn khớp với kích thước nesting.
        double boxTol = 1e-6 * Math.Max(sizeBox.Width, sizeBox.Height);
        var droppedLayers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        bool InsideSizeBox(Shape s) => sizeSet.Contains(s.Layer) || sizeBox.Contains(s.Bounds(), boxTol);
        foreach (var s in raw.Shapes)
            if (outSet.Contains(s.Layer) && !InsideSizeBox(s)) droppedLayers.Add(s.Layer);
        foreach (var l in droppedLayers)
            warnings.Add($"Layer '{l}' có hình nằm ngoài biên chi tiết: đã loại phần đó khỏi file xuất.");

        // 4) Đơn vị
        double scale;
        string unitSource;
        if (options.UnitScale is { } us) { scale = us; unitSource = "chỉ định thủ công"; }
        else if (raw.UnitScale is { } hs) { scale = hs; unitSource = $"$INSUNITS = {raw.UnitText}"; }
        else
        {
            scale = 1.0;
            unitSource = "không khai báo đơn vị, giả định mm";
            warnings.Add("DXF không khai báo đơn vị ($INSUNITS); đang giả định đơn vị là mm.");
        }

        double w = sizeBox.Width * scale;
        double h = sizeBox.Height * scale;
        if (Math.Max(w, h) < 5)
            warnings.Add($"Kích thước rất nhỏ ({w:0.###} x {h:0.###} mm): kiểm tra lại đơn vị (có thể bản vẽ đang là m hoặc inch).");
        else if (Math.Max(w, h) > 12000)
            warnings.Add($"Kích thước rất lớn ({w:0.###} x {h:0.###} mm): kiểm tra lại đơn vị (có thể bản vẽ đang là micron).");

        // 5) Chuẩn hóa: đổi sang mm, dời góc hộp bao về (0,0), chỉ giữ hình được xuất, rồi đổi sang LINE/ARC/CIRCLE
        var norm = new Mat(scale, 0, 0, scale, -sizeBox.MinX * scale, -sizeBox.MinY * scale);
        var segs = new List<Seg>(raw.Shapes.Count);
        foreach (var s in raw.Shapes)
        {
            if (!outSet.Contains(s.Layer) || !InsideSizeBox(s)) continue;
            if (norm.Transform(s) is { } t) AppendSegments(t, segs);
        }

        var layerCounts = raw.Shapes.GroupBy(s => s.Layer, StringComparer.OrdinalIgnoreCase)
                                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var (name, qty, noRot) = ParseFileName(Path.GetFileNameWithoutExtension(path), options.DefaultQuantity);
        var geometry = new DxfShape(name, segs, scale, layerCounts, warnings, raw.Approximated);

        return new DxfPart
        {
            FileName = path,
            Name = name,
            Quantity = qty,
            CanRotate = !noRot,
            Geometry = geometry,
            UnitScale = scale,
            UnitSource = unitSource,
            SizingLayers = sizing,
            OutputLayers = output,
            ExcludedLayers = excluded,
            Warnings = warnings
        };
    }

    private static void AppendSegments(Shape s, List<Seg> dst)
    {
        const double deg = Math.PI / 180.0;
        switch (s)
        {
            case LineShape l:
                dst.Add(new LineSeg(l.X1, l.Y1, l.X2, l.Y2));
                break;
            case CircleShape c:
                dst.Add(new CircleSeg(c.Cx, c.Cy, c.R));
                break;
            case ArcShape a:
                dst.Add(new ArcSeg(a.Cx, a.Cy, a.R, a.StartDeg * deg, a.SweepDeg * deg));
                break;
            case PolyShape p:
                for (int i = 0; i + 1 < p.Pts.Length; i++)
                    dst.Add(new LineSeg(p.Pts[i].X, p.Pts[i].Y, p.Pts[i + 1].X, p.Pts[i + 1].Y));
                if (p.Closed && p.Pts.Length > 2)
                    dst.Add(new LineSeg(p.Pts[^1].X, p.Pts[^1].Y, p.Pts[0].X, p.Pts[0].Y));
                break;
        }
    }

    /// <summary>
    /// Tên file -> (tên, số lượng, cấm xoay). Quy ước: "Ten_x5.dxf" = 5 cái, hậu tố "_norot" = không cho xoay (vân thớ).
    /// Ví dụ "Gia_do_A_x12_norot".
    /// </summary>
    public static (string Name, int Quantity, bool NoRotate) ParseFileName(string baseName, int defaultQty = 1)
    {
        string s = baseName.Trim();
        bool noRot = false;

        var m = Regex.Match(s, @"^(?<n>.+?)[_\- ]norot$", RegexOptions.IgnoreCase);
        if (m.Success) { noRot = true; s = m.Groups["n"].Value; }

        int qty = defaultQty;
        m = Regex.Match(s, @"^(?<n>.+?)[_\- ][xX](?<q>\d+)$");
        if (m.Success && int.TryParse(m.Groups["q"].Value, out int q) && q > 0)
        {
            qty = q;
            s = m.Groups["n"].Value;
        }

        // norot có thể đứng trước _xN
        m = Regex.Match(s, @"^(?<n>.+?)[_\- ]norot$", RegexOptions.IgnoreCase);
        if (m.Success) { noRot = true; s = m.Groups["n"].Value; }

        return (s.Length == 0 ? baseName : s, qty, noRot);
    }

    // ---------- Đọc thô ----------

    public static DxfRaw ReadRaw(string path)
    {
        var doc = Load(path);
        var b = new Builder(doc);
        b.Walk(doc.Entities, Mat.Identity, null, 0);

        var (scale, text) = UnitsFromHeader(doc.Header);
        var warnings = b.Warnings.ToList();
        foreach (var kv in b.Unsupported.OrderBy(k => k.Key))
            warnings.Add($"Bỏ qua {kv.Value} đối tượng {kv.Key} (chưa hỗ trợ).");
        return new DxfRaw(b.Shapes, scale, text, warnings, b.Approximated);
    }

    private static (double? Scale, string Text) UnitsFromHeader(Dictionary<string, string> h)
    {
        if (h.TryGetValue("$INSUNITS", out var v) && int.TryParse(v, NumberStyles.Integer, Inv, out int u))
        {
            switch (u)
            {
                case 1: return (25.4, "1 (inch)");
                case 2: return (304.8, "2 (feet)");
                case 4: return (1.0, "4 (mm)");
                case 5: return (10.0, "5 (cm)");
                case 6: return (1000.0, "6 (m)");
                case 9: return (0.0254, "9 (mil)");
                case 10: return (914.4, "10 (yard)");
                case 14: return (100.0, "14 (dm)");
            }
        }
        return (null, "");
    }

    // ---------- Phân tích cấu trúc file ----------

    private sealed class RawEntity
    {
        public string Type = "";
        public readonly List<(int Code, string Value)> Data = new();
        public readonly List<RawEntity> Vertices = new();

        public string? Str(int code)
        {
            foreach (var d in Data) if (d.Code == code) return d.Value;
            return null;
        }

        public double Dbl(int code, double def = 0)
        {
            var s = Str(code);
            return s != null && double.TryParse(s, NumberStyles.Float, Inv, out var v) && double.IsFinite(v) ? v : def;
        }

        public int Int(int code, int def = 0)
        {
            var s = Str(code);
            return s != null && int.TryParse(s, NumberStyles.Integer, Inv, out var v) ? v : def;
        }

        public double NormalZ => Dbl(230, 1);
    }

    private sealed class BlockDef
    {
        public double Bx, By;
        public readonly List<RawEntity> Entities = new();
    }

    private sealed class Doc
    {
        public readonly Dictionary<string, string> Header = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<RawEntity> Entities = new();
        public readonly Dictionary<string, BlockDef> Blocks = new(StringComparer.OrdinalIgnoreCase);
    }

    private static Doc Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 18 && Encoding.ASCII.GetString(bytes, 0, 18) == "AutoCAD Binary DXF")
            throw new NotSupportedException("File DXF dạng nhị phân (binary). Hãy lưu lại dạng ASCII (trong ZWCAD/Inventor chọn DXF thường).");

        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(bytes); }
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        var lines = text.Split('\n');

        // Bước 1: tách thành các bản ghi bắt đầu bằng group code 0
        var recs = new List<RawEntity>();
        RawEntity? cur = null;
        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            if (!int.TryParse(lines[i].Trim(), NumberStyles.Integer, Inv, out int code))
            {
                if (string.IsNullOrWhiteSpace(lines[i])) { i--; continue; } // dòng trống thừa
                throw new InvalidDataException($"File DXF lỗi cấu trúc ở dòng {i + 1}: '{lines[i].Trim()}'.");
            }

            string val = lines[i + 1].Trim();
            if (code == 0)
            {
                cur = new RawEntity { Type = val };
                recs.Add(cur);
                if (val == "EOF") break;
            }
            else cur?.Data.Add((code, val));
        }

        if (recs.Count == 0) throw new InvalidDataException("Không phải file DXF hợp lệ.");

        // Bước 2: gom theo SECTION
        var doc = new Doc();
        string? section = null;
        BlockDef? block = null;
        RawEntity? poly = null;

        foreach (var r in recs)
        {
            switch (r.Type)
            {
                case "SECTION":
                    section = r.Str(2);
                    if (section == "HEADER")
                    {
                        string? var = null;
                        foreach (var (code, val) in r.Data)
                        {
                            if (code == 9) var = val;
                            else if (var != null && !doc.Header.ContainsKey(var)) doc.Header[var] = val;
                        }
                    }
                    break;
                case "ENDSEC": section = null; block = null; poly = null; break;
                case "BLOCK":
                    if (section == "BLOCKS")
                    {
                        block = new BlockDef { Bx = r.Dbl(10), By = r.Dbl(20) };
                        string? bn = r.Str(2);
                        if (bn != null) doc.Blocks[bn] = block;
                    }
                    poly = null;
                    break;
                case "ENDBLK": block = null; poly = null; break;
                case "EOF": break;
                default:
                    if (section == "ENTITIES" || (section == "BLOCKS" && block != null))
                    {
                        var list = section == "ENTITIES" ? doc.Entities : block!.Entities;
                        if (r.Type == "VERTEX" && poly != null) poly.Vertices.Add(r);
                        else if (r.Type == "SEQEND") poly = null;
                        else
                        {
                            list.Add(r);
                            poly = r.Type == "POLYLINE" ? r : null;
                        }
                    }
                    break;
            }
        }

        return doc;
    }

    // ---------- Chuyển entity thành hình ----------

    private static readonly HashSet<string> WarnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SOLID", "3DFACE", "TRACE", "MLINE", "REGION", "3DSOLID", "BODY", "XLINE", "RAY", "SURFACE", "MESH"
    };

    private sealed class Builder
    {
        private readonly Doc _doc;
        public readonly List<Shape> Shapes = new();
        public readonly HashSet<string> Warnings = new();
        public readonly Dictionary<string, int> Unsupported = new();

        /// <summary>Số ellipse/spline phải xấp xỉ bằng nhiều đoạn thẳng.</summary>
        public int Approximated { get; private set; }

        public Builder(Doc doc) => _doc = doc;

        private void Add(Shape? s)
        {
            if (s != null) Shapes.Add(s);
        }

        public void Walk(List<RawEntity> ents, Mat m, string? inheritLayer, int depth)
        {
            foreach (var e in ents)
            {
                string layer = e.Str(8) ?? "0";
                if (layer == "0" && inheritLayer != null) layer = inheritLayer;

                switch (e.Type)
                {
                    case "LINE":
                        Add(m.Transform(new LineShape(layer, e.Dbl(10), e.Dbl(20), e.Dbl(11), e.Dbl(21))));
                        break;

                    case "CIRCLE":
                    {
                        var mm = e.NormalZ < 0 ? m * Mat.MirrorX : m;
                        double r = e.Dbl(40);
                        if (r > 0) Add(mm.Transform(new CircleShape(layer, e.Dbl(10), e.Dbl(20), r)));
                        break;
                    }

                    case "ARC":
                    {
                        var mm = e.NormalZ < 0 ? m * Mat.MirrorX : m;
                        double r = e.Dbl(40);
                        double st = e.Dbl(50), en = e.Dbl(51);
                        double sweep = Mat.Norm360(en - st);
                        if (sweep <= 1e-12) sweep = 360;
                        if (r > 0) Add(mm.Transform(new ArcShape(layer, e.Dbl(10), e.Dbl(20), r, Mat.Norm360(st), sweep)));
                        break;
                    }

                    case "LWPOLYLINE":
                    {
                        var mm = e.NormalZ < 0 ? m * Mat.MirrorX : m;
                        var pts = new List<(double X, double Y, double B)>();
                        foreach (var (code, val) in e.Data)
                        {
                            switch (code)
                            {
                                case 10: pts.Add((P(val), 0, 0)); break;
                                case 20: if (pts.Count > 0) pts[^1] = (pts[^1].X, P(val), pts[^1].B); break;
                                case 42: if (pts.Count > 0) pts[^1] = (pts[^1].X, pts[^1].Y, P(val)); break;
                            }
                        }
                        EmitPolyline(pts, (e.Int(70) & 1) != 0, layer, mm);
                        break;
                    }

                    case "POLYLINE":
                    {
                        int pf = e.Int(70);
                        if ((pf & (16 | 64)) != 0)
                        {
                            Warnings.Add("Bỏ qua POLYLINE dạng lưới 3D/polyface.");
                            break;
                        }
                        var mm = e.NormalZ < 0 ? m * Mat.MirrorX : m;
                        var pts = new List<(double X, double Y, double B)>();
                        foreach (var v in e.Vertices)
                        {
                            int vf = v.Int(70);
                            if ((vf & 128) != 0) continue;
                            if ((pf & 4) != 0 && (vf & 16) != 0) continue; // điểm điều khiển của spline-fit polyline
                            pts.Add((v.Dbl(10), v.Dbl(20), v.Dbl(42)));
                        }
                        EmitPolyline(pts, (pf & 1) != 0, layer, mm);
                        break;
                    }

                    case "ELLIPSE":
                        EmitEllipse(e, layer, m);
                        break;

                    case "SPLINE":
                        EmitSpline(e, layer, m);
                        break;

                    case "INSERT":
                        EmitInsert(e, layer, m, depth);
                        break;

                    default:
                        if (WarnTypes.Contains(e.Type))
                            Unsupported[e.Type] = Unsupported.GetValueOrDefault(e.Type) + 1;
                        break;
                }
            }
        }

        private static double P(string s) =>
            double.TryParse(s, NumberStyles.Float, Inv, out var v) && double.IsFinite(v) ? v : 0;

        private void EmitInsert(RawEntity e, string layer, Mat m, int depth)
        {
            string? name = e.Str(2);
            if (name == null) return;
            if (depth >= 8) { Warnings.Add("Block lồng quá sâu (>8), bỏ qua."); return; }
            if (!_doc.Blocks.TryGetValue(name, out var blk))
            {
                Warnings.Add($"INSERT tham chiếu block không tồn tại: '{name}'.");
                return;
            }

            var mm = e.NormalZ < 0 ? m * Mat.MirrorX : m;
            double ix = e.Dbl(10), iy = e.Dbl(20);
            double sx = e.Dbl(41, 1), sy = e.Dbl(42, 1), rot = e.Dbl(50);
            int cols = Math.Max(1, e.Int(70, 1)), rows = Math.Max(1, e.Int(71, 1));
            double cs = e.Dbl(44), rs = e.Dbl(45);

            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var local = Mat.Translate(ix, iy) * Mat.Rotate(rot) * Mat.Translate(c * cs, r * rs)
                            * Mat.Scale(sx, sy) * Mat.Translate(-blk.Bx, -blk.By);
                Walk(blk.Entities, mm * local, layer, depth + 1);
            }
        }

        private void EmitPolyline(List<(double X, double Y, double B)> pts, bool closed, string layer, Mat m)
        {
            int n = pts.Count;
            if (n < 2) return;

            if (!pts.Any(p => Math.Abs(p.B) > 1e-12))
            {
                Add(m.Transform(new PolyShape(layer, pts.Select(p => (p.X, p.Y)).ToArray(), closed)));
                return;
            }

            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                var p1 = pts[i];
                var p2 = pts[(i + 1) % n];
                if (Math.Abs(p1.B) < 1e-12)
                    Add(m.Transform(new LineShape(layer, p1.X, p1.Y, p2.X, p2.Y)));
                else
                    Add(m.Transform(ArcFromBulge(layer, p1.X, p1.Y, p2.X, p2.Y, p1.B)));
            }
        }

        private static Shape ArcFromBulge(string layer, double x1, double y1, double x2, double y2, double b)
        {
            double dx = x2 - x1, dy = y2 - y1;
            double c = Math.Sqrt(dx * dx + dy * dy);
            if (c < 1e-12) return new LineShape(layer, x1, y1, x2, y2);

            double ux = dx / c, uy = dy / c;
            double nx = -uy, ny = ux; // pháp tuyến bên trái hướng dây cung
            double off = (c / 2) * (1 - b * b) / (2 * b);
            double cx = (x1 + x2) / 2 + nx * off;
            double cy = (y1 + y2) / 2 + ny * off;
            double r = c * (1 + b * b) / (4 * Math.Abs(b));

            double a1 = Math.Atan2(y1 - cy, x1 - cx) * 180 / Math.PI;
            double a2 = Math.Atan2(y2 - cy, x2 - cx) * 180 / Math.PI;
            double sweep = 4 * Math.Atan(Math.Abs(b)) * 180 / Math.PI;
            double start = b > 0 ? a1 : a2; // bulge âm: đi cùng chiều kim đồng hồ từ p1 đến p2 = CCW từ p2 đến p1
            return new ArcShape(layer, cx, cy, r, Mat.Norm360(start), sweep);
        }

        private void EmitEllipse(RawEntity e, string layer, Mat m)
        {
            double cx = e.Dbl(10), cy = e.Dbl(20);
            double mx = e.Dbl(11), my = e.Dbl(21);
            double ratio = e.Dbl(40, 1);
            double t0 = e.Dbl(41, 0), t1 = e.Dbl(42, 2 * Math.PI);
            double sweep = t1 - t0;
            if (sweep <= 1e-12) sweep += 2 * Math.PI;
            bool full = sweep >= 2 * Math.PI - 1e-9;

            double vx = -ratio * my, vy = ratio * mx; // trục phụ
            int n = Math.Max(32, (int)Math.Ceiling(sweep * 180 / Math.PI / 0.5));
            int count = full ? n : n + 1;
            var pts = new (double X, double Y)[count];
            for (int i = 0; i < count; i++)
            {
                double t = t0 + sweep * i / n;
                double ct = Math.Cos(t), st = Math.Sin(t);
                pts[i] = (cx + ct * mx + st * vx, cy + ct * my + st * vy);
            }
            Approximated++;
            Add(m.Transform(new PolyShape(layer, pts, full)));
        }

        private void EmitSpline(RawEntity e, string layer, Mat m)
        {
            int deg = e.Int(71, 3);
            var knots = new List<double>();
            var ws = new List<double>();
            var ctrl = new List<(double X, double Y)>();
            var fit = new List<(double X, double Y)>();
            foreach (var (code, val) in e.Data)
            {
                switch (code)
                {
                    case 40: knots.Add(P(val)); break;
                    case 41: ws.Add(P(val)); break;
                    case 10: ctrl.Add((P(val), 0)); break;
                    case 20: if (ctrl.Count > 0) ctrl[^1] = (ctrl[^1].X, P(val)); break;
                    case 11: fit.Add((P(val), 0)); break;
                    case 21: if (fit.Count > 0) fit[^1] = (fit[^1].X, P(val)); break;
                }
            }

            int n = ctrl.Count;
            bool valid = deg >= 1 && n > deg && knots.Count == n + deg + 1 && (ws.Count == 0 || ws.Count == n)
                         && knots[n] > knots[deg];
            if (valid || fit.Count >= 2 || ctrl.Count >= 2) Approximated++;
            if (valid)
            {
                var w = ws.Count == n ? ws.ToArray() : Enumerable.Repeat(1.0, n).ToArray();
                int steps = Math.Max(64, 16 * n);
                var pts = new (double X, double Y)[steps + 1];
                double u0 = knots[deg], u1 = knots[n];
                for (int i = 0; i <= steps; i++)
                    pts[i] = EvalNurbs(deg, knots, ctrl, w, u0 + (u1 - u0) * i / steps);
                Add(m.Transform(new PolyShape(layer, pts, false)));
            }
            else if (fit.Count >= 2)
            {
                Warnings.Add("SPLINE thiếu dữ liệu knot, dùng các điểm fit.");
                Add(m.Transform(new PolyShape(layer, fit.ToArray(), false)));
            }
            else if (ctrl.Count >= 2)
            {
                Warnings.Add("SPLINE không đủ dữ liệu, xấp xỉ bằng đa giác điều khiển (kích thước có thể lớn hơn thực tế).");
                Add(m.Transform(new PolyShape(layer, ctrl.ToArray(), false)));
            }
        }

        /// <summary>Cox–de Boor (The NURBS Book, A2.2), có trọng số.</summary>
        private static (double X, double Y) EvalNurbs(int p, List<double> U, List<(double X, double Y)> P, double[] W, double t)
        {
            int n = P.Count;
            int k = p;
            while (k < n - 1 && t >= U[k + 1]) k++;

            var N = new double[p + 1];
            var left = new double[p + 1];
            var right = new double[p + 1];
            N[0] = 1;
            for (int j = 1; j <= p; j++)
            {
                left[j] = t - U[k + 1 - j];
                right[j] = U[k + j] - t;
                double saved = 0;
                for (int r = 0; r < j; r++)
                {
                    double d = right[r + 1] + left[j - r];
                    double temp = d == 0 ? 0 : N[r] / d;
                    N[r] = saved + right[r + 1] * temp;
                    saved = left[j - r] * temp;
                }
                N[j] = saved;
            }

            double sx = 0, sy = 0, sw = 0;
            for (int i = 0; i <= p; i++)
            {
                int idx = k - p + i;
                double b = N[i] * W[idx];
                sx += b * P[idx].X;
                sy += b * P[idx].Y;
                sw += b;
            }
            return sw == 0 ? (P[k].X, P[k].Y) : (sx / sw, sy / sw);
        }
    }
}
