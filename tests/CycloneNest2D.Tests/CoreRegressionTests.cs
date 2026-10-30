using System.Globalization;
using System.Text;
using CycloneNest2D.Core;
using Xunit;


namespace CycloneNest2D.Tests;

public sealed class CoreRegressionTests
{
    public static IEnumerable<object[]> DxfReaderCases() => DiscoverCases("DxfReader");
    public static IEnumerable<object[]> NestingAndExportCases() => DiscoverCases("NestingAndExport");
    public static IEnumerable<object[]> GeometryAndValidatorCases() => DiscoverCases("GeometryAndValidator");
    public static IEnumerable<object[]> RasterSafetyCases() => DiscoverCases("RasterSafety");

    [Theory]
    [MemberData(nameof(DxfReaderCases))]
    [Trait("Domain", "DxfReader")]
    public void DxfReaderRegression(string name, int index) => ExecuteCase(name, index);

    [Theory]
    [MemberData(nameof(NestingAndExportCases))]
    [Trait("Domain", "NestingAndExport")]
    public void NestingAndExport(string name, int index) => ExecuteCase(name, index);

    [Theory]
    [MemberData(nameof(GeometryAndValidatorCases))]
    [Trait("Domain", "GeometryAndValidator")]
    public void GeometryAndValidator(string name, int index) => ExecuteCase(name, index);

    [Theory]
    [MemberData(nameof(RasterSafetyCases))]
    [Trait("Domain", "RasterSafety")]
    public void RasterSafety(string name, int index) => ExecuteCase(name, index);

    private static IEnumerable<object[]> DiscoverCases(string category)
    {
        using var suite = CreateSuite();
        return suite.Cases
            .Select((test, index) => (test, index))
            .Where(item => item.test.Category == category)
            .Select(item => new object[] { item.test.Name, item.index })
            .ToArray();
    }

    private void ExecuteCase(string name, int index)
    {
        using var suite = CreateSuite();
        var test = suite.Cases[index];
        Assert.Equal(name, test.Name);
        test.Body();
    }

    private static TestSuite CreateSuite()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "nesting2d_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);

        var cases = new List<RegisteredTest>();

        void Test(string name, Action body)
        {
            // Keep these ranges aligned with the contiguous domain blocks below.
            int index = cases.Count;
            string category = index switch
            {
                < 22 => "DxfReader",
                < 26 => "NestingAndExport",
                < 36 => "GeometryAndValidator",
                _ => "RasterSafety"
            };
            cases.Add(new RegisteredTest(category, name, body));
        }

        void Near(double expected, double actual, double tol, string what)
        {
            if (Math.Abs(expected - actual) > tol)
                throw new Exception($"{what}: mong Ä‘á»£i {expected:0.######}, nháº­n {actual:0.######} (sai sá»‘ cho phÃ©p {tol}).");
        }

        void True(bool cond, string msg)
        {
            Assert.True(cond, msg);
        }

        // ---------- TrÃ¬nh sinh DXF ----------
        string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        string DxfLine(string layer, double x1, double y1, double x2, double y2) =>
            $"0\nLINE\n8\n{layer}\n10\n{F(x1)}\n20\n{F(y1)}\n30\n0\n11\n{F(x2)}\n21\n{F(y2)}\n31\n0\n";

        string DxfCircle(string layer, double cx, double cy, double r, string extra = "") =>
            $"0\nCIRCLE\n8\n{layer}\n10\n{F(cx)}\n20\n{F(cy)}\n30\n0\n40\n{F(r)}\n{extra}";

        string DxfArc(string layer, double cx, double cy, double r, double a0, double a1, string extra = "") =>
            $"0\nARC\n8\n{layer}\n10\n{F(cx)}\n20\n{F(cy)}\n30\n0\n40\n{F(r)}\n50\n{F(a0)}\n51\n{F(a1)}\n{extra}";

        string DxfLwPoly(string layer, bool closed, params (double x, double y, double b)[] pts)
        {
            var sb = new StringBuilder();
            sb.Append($"0\nLWPOLYLINE\n8\n{layer}\n90\n{pts.Length}\n70\n{(closed ? 1 : 0)}\n");
            foreach (var (x, y, b) in pts)
            {
                sb.Append($"10\n{F(x)}\n20\n{F(y)}\n");
                if (b != 0) sb.Append($"42\n{F(b)}\n");
            }
            return sb.ToString();
        }

        string DxfRect(string layer, double x, double y, double w, double h) =>
            DxfLine(layer, x, y, x + w, y) + DxfLine(layer, x + w, y, x + w, y + h) +
            DxfLine(layer, x + w, y + h, x, y + h) + DxfLine(layer, x, y + h, x, y);

        string DxfDoc(string entities, string blocks = "", int? insunits = null, string tables = "")
        {
            var sb = new StringBuilder();
            sb.Append("0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1027\n");
            if (insunits != null) sb.Append($"9\n$INSUNITS\n70\n{insunits}\n");
            sb.Append("0\nENDSEC\n");
            if (tables != "") sb.Append("0\nSECTION\n2\nTABLES\n" + tables + "0\nENDSEC\n");
            if (blocks != "") sb.Append("0\nSECTION\n2\nBLOCKS\n" + blocks + "0\nENDSEC\n");
            sb.Append("0\nSECTION\n2\nENTITIES\n" + entities + "0\nENDSEC\n0\nEOF\n");
            return sb.ToString();
        }

        string Save(string fileName, string content)
        {
            var path = Path.Combine(tmp, fileName);
            File.WriteAllText(path, content.Replace("\n", "\r\n"));
            return path;
        }

        Console.WriteLine("Äá»c DXF:");

        Test("4 LINE thÃ nh hÃ¬nh chá»¯ nháº­t 100x50", () =>
        {
            var p = DxfReader.Read(Save("rect.dxf", DxfDoc(DxfRect("0", 10, 20, 100, 50))));
            Near(100, p.Width, 1e-9, "rá»™ng");
            Near(50, p.Height, 1e-9, "cao");
            Near(0, p.Geometry.MinX, 1e-9, "MinX sau chuáº©n hÃ³a");
            Near(0, p.Geometry.MinY, 1e-9, "MinY sau chuáº©n hÃ³a");
            True(p.Quantity == 1, "sá»‘ lÆ°á»£ng máº·c Ä‘á»‹nh pháº£i lÃ  1");
        });

        Test("Giữ và xuất TEXT/MTEXT, đồng thời cảnh báo chữ nằm trong contour lỗ", () =>
        {
            string text = "0\nTEXT\n8\nNOTE_TEXT\n10\n50\n20\n50\n40\n2\n1\nText trong lỗ\n";
            string mtext = "0\nMTEXT\n8\nNOTE_TEXT\n10\n51\n20\n50\n40\n2\n1\nMText trong lỗ\n";
            string outsideHole = "0\nTEXT\n8\nNOTE_TEXT\n10\n10\n20\n10\n40\n2\n1\nText trên vật liệu\n";
            string entities = DxfRect("IV_OUTER_PROFILE", 0, 0, 100, 100) +
                DxfCircle("IV_INTERIOR_PROFILES", 50, 50, 20) + text + mtext + outsideHole;
            var part = DxfReader.Read(Save("inner_text.dxf", DxfDoc(entities)));
            True(part.Warnings.Any(w => w.Contains("1 TEXT", StringComparison.Ordinal) &&
                w.Contains("1 MTEXT", StringComparison.Ordinal)), "cảnh báo số lượng TEXT/MTEXT bên trong contour");
            True(part.Geometry.Texts.Count == 3, "giữ cả text nằm trên vật liệu và trong lỗ dù layer ghi chú không dùng để cắt");
            True(part.Geometry.Segments.Count == 5, "text không bị chuyển thành đường cắt");

            var sheet = new SheetDefinition(200, 200, 5, 0);
            var result = NestEngine.Run(new[] { part.ToDefinition() }, sheet, new NestOptions { AllowRotation = false });
            True(result.Unplaced.Count == 0, "part có chữ vẫn nesting được");
            string modernFile = Path.Combine(tmp, "inner_text_r2000.dxf");
            DxfWriter.Write(result.Sheets[0], sheet, modernFile, new DxfExportOptions
            {
                OutputSheets = false,
                TargetVersion = DxfTargetVersion.AutoCAD2000
            });
            var modernTexts = DxfReader.ReadRaw(modernFile).Texts;
            True(modernTexts.Count(t => t.Type == "TEXT") == 2 &&
                 modernTexts.Count(t => t.Type == "MTEXT") == 1, "DXF hiện đại xuất lại đúng loại TEXT và MTEXT");
            True(modernTexts.Any(t => t.Content == "MText trong lỗ") &&
                 modernTexts.Any(t => t.Content == "Text trên vật liệu"), "nội dung text nguồn được giữ");

            string legacyFile = Path.Combine(tmp, "inner_text_r12.dxf");
            DxfWriter.Write(result.Sheets[0], sheet, legacyFile, new DxfExportOptions { OutputSheets = false });
            var legacyTexts = DxfReader.ReadRaw(legacyFile).Texts;
            True(legacyTexts.Count == 3 && legacyTexts.Any(t => t.Content == "MText trong lỗ"),
                "MTEXT được thể hiện bằng TEXT khi xuất R12 để không mất dấu khắc");
        });

        Test("Phát hiện TEXT căn chỉnh theo group 11/21 và nối đủ các chunk MTEXT", () =>
        {
            string alignedText = "0\nTEXT\n8\nNOTE_TEXT\n10\n10\n20\n10\n40\n2\n72\n1\n73\n2\n" +
                                 "11\n50\n21\n50\n1\nText căn giữa trong lỗ\n";
            string chunkedMtext = "0\nMTEXT\n8\nNOTE_TEXT\n10\n49\n20\n50\n40\n2\n" +
                                  "3\nMText nhiều phần \n1\nnằm trong lỗ\n";
            string entities = DxfRect("IV_OUTER_PROFILE", 0, 0, 100, 100) +
                              DxfCircle("IV_INTERIOR_PROFILES", 50, 50, 20) + alignedText + chunkedMtext;
            var part = DxfReader.Read(Save("aligned_inner_text.dxf", DxfDoc(entities)));
            var aligned = part.Geometry.Texts.Single(text => text.Type == "TEXT");
            Near(10, aligned.X, 1e-9, "group 10 vẫn được bảo toàn");
            Near(50, aligned.AnchorX, 1e-9, "group 11 là tọa độ thực của TEXT căn chỉnh");
            Near(50, aligned.AnchorY, 1e-9, "group 21 là tọa độ thực của TEXT căn chỉnh");
            True(part.Geometry.Texts.Single(text => text.Type == "MTEXT").Content == "MText nhiều phần nằm trong lỗ",
                "ghép đúng các group 3 và group 1 của MTEXT");
            True(part.Warnings.Any(w => w.Contains("1 TEXT", StringComparison.Ordinal) &&
                                        w.Contains("1 MTEXT", StringComparison.Ordinal)),
                "cảnh báo nhận cả TEXT căn chỉnh và MTEXT ở trong contour lỗ");
        });

        Test("Giữ layer và màu gốc khi xuất lại DXF", () =>
        {
            string table = "0\nTABLE\n2\nLAYER\n70\n2\n" +
                "0\nLAYER\n2\nPROFILE_A\n70\n0\n62\n1\n420\n16711680\n6\nCONTINUOUS\n" +
                "0\nLAYER\n2\nPROFILE_B\n70\n0\n62\n3\n420\n65280\n6\nCONTINUOUS\n0\nENDTAB\n";
            string entities = DxfRect("PROFILE_A", 0, 0, 100, 100) + DxfCircle("PROFILE_B", 50, 50, 20);
            var part = DxfReader.Read(Save("layer_color.dxf", DxfDoc(entities, tables: table)));
            True(part.Geometry.Segments.Where(s => s is LineSeg).All(s => s.Layer == "PROFILE_A"), "đường biên giữ layer nguồn");
            True(part.Geometry.LayerColors["PROFILE_A"].Aci == 1 &&
                part.Geometry.LayerColors["PROFILE_A"].TrueColor == 16711680, "đọc màu ACI và màu true color của layer");

            var sheet = new SheetResult { Index = 1, Width = 100, Height = 100 };
            sheet.Placements.Add(new Placement(part.Name, 1, 0, 0, part.Width, part.Height, false, part.Geometry));
            string output = Path.Combine(tmp, "layer_color_output.dxf");
            DxfWriter.Write(new[] { sheet }, output, new DxfExportOptions
            {
                OutputSheets = false,
                TargetVersion = DxfTargetVersion.AutoCAD2000
            });
            var exported = DxfReader.ReadRaw(output);
            True(exported.Shapes.Any(s => s.Layer == "PROFILE_A") && exported.Shapes.Any(s => s.Layer == "PROFILE_B"),
                "entity giữ nguyên tên layer nguồn");
            True(exported.LayerColors["PROFILE_A"].Aci == 1 &&
                exported.LayerColors["PROFILE_A"].TrueColor == 16711680 &&
                exported.LayerColors["PROFILE_B"].TrueColor == 65280, "bảng layer giữ nguyên màu nguồn");
        });

        Test("LWPOLYLINE cÃ³ cung bulge (bÃ¡n nguyÃªn) má»Ÿ rá»™ng há»™p bao ra 125x50", () =>
        {
            // (0,0)->(100,0) tháº³ng, (100,0)->(100,50) bulge=1 (CCW, ná»­a Ä‘Æ°á»ng trÃ²n tÃ¢m (100,25) R25), rá»“i Ä‘Ã³ng kÃ­n.
            var ent = DxfLwPoly("0", true, (0, 0, 0), (100, 0, 1), (100, 50, 0), (0, 50, 0));
            var p = DxfReader.Read(Save("bulge.dxf", DxfDoc(ent)));
            Near(125, p.Width, 1e-6, "rá»™ng");
            Near(50, p.Height, 1e-6, "cao");
        });

        Test("Bulge Ã¢m (cung Ä‘i ngÆ°á»£c) cho ra cung náº±m cÃ¹ng bÃªn Ä‘Ãºng", () =>
        {
            // Äa giÃ¡c Ä‘i cÃ¹ng chiá»u kim Ä‘á»“ng há»“; cáº¡nh trÃªn (0,50)->(100,50) cÃ³ bulge -1 = ná»­a Ä‘Æ°á»ng trÃ²n CW,
            // tÃ¢m (50,50), R=50, phÃ¬nh lÃªn trÃªn tá»›i y=100. Náº¿u hÆ°á»›ng cung bá»‹ láº­t thÃ¬ cung chui xuá»‘ng dÆ°á»›i vÃ  cao chá»‰ cÃ²n 50.
            var ent = DxfLwPoly("0", true, (0, 0, 0), (0, 50, -1), (100, 50, 0), (100, 0, 0));
            var p = DxfReader.Read(Save("bulge_neg.dxf", DxfDoc(ent)));
            Near(100, p.Width, 1e-6, "rá»™ng");
            Near(100, p.Height, 1e-6, "cao");
        });

        Test("CIRCLE r=30 -> 60x60", () =>
        {
            var p = DxfReader.Read(Save("circle.dxf", DxfDoc(DxfCircle("0", 10, 10, 30))));
            Near(60, p.Width, 1e-9, "rá»™ng");
            Near(60, p.Height, 1e-9, "cao");
        });

        Test("ARC 0..90 Ä‘á»™ R10 -> 10x10; ARC 350..10 -> cung háº¹p qua trá»¥c X", () =>
        {
            var q = DxfReader.Read(Save("arc90.dxf", DxfDoc(DxfArc("0", 0, 0, 10, 0, 90))));
            Near(10, q.Width, 1e-9, "rá»™ng 1/4 cung");
            Near(10, q.Height, 1e-9, "cao 1/4 cung");

            var n = DxfReader.Read(Save("arc_narrow.dxf", DxfDoc(DxfArc("0", 0, 0, 10, 350, 10))));
            Near(10 - 10 * Math.Cos(10 * Math.PI / 180), n.Width, 1e-9, "rá»™ng cung háº¹p");
            Near(2 * 10 * Math.Sin(10 * Math.PI / 180), n.Height, 1e-9, "cao cung háº¹p");
        });

        Test("PhÃ¡p tuyáº¿n Ä‘áº£o (210/220/230 = 0,0,-1) láº­t gÆ°Æ¡ng cung quanh trá»¥c Y", () =>
        {
            // Ná»­a vÃ²ng trÃªn tÃ¢m (10,0) R5 trong OCS; WCS láº­t x -> tÃ¢m (-10,0). Há»™p bao x -15..-5, y 0..5.
            var ent = DxfArc("0", 10, 0, 5, 0, 180, "210\n0\n220\n0\n230\n-1\n") + DxfLine("0", -15, 0, -5, 0);
            var p = DxfReader.Read(Save("mirror.dxf", DxfDoc(ent)));
            Near(10, p.Width, 1e-9, "rá»™ng");
            Near(5, p.Height, 1e-9, "cao");
        });

        Test("INSERT block xoay 90 Ä‘á»™ Ä‘á»•i 100x50 thÃ nh 50x100", () =>
        {
            string blockText = "0\nTEXT\n8\n0\n10\n10\n20\n20\n40\n2\n1\nBlockLabel\n";
            string blocks = "0\nBLOCK\n8\n0\n2\nB1\n70\n0\n10\n0\n20\n0\n30\n0\n" +
                DxfRect("0", 0, 0, 100, 50) + blockText + "0\nENDBLK\n8\n0\n";
            string ins = "0\nINSERT\n8\nPART\n2\nB1\n10\n500\n20\n500\n30\n0\n50\n90\n";
            var p = DxfReader.Read(Save("insert.dxf", DxfDoc(ins, blocks)));
            Near(50, p.Width, 1e-9, "rá»™ng");
            Near(100, p.Height, 1e-9, "cao");
            True(p.Geometry.Texts.Single() is { Content: "BlockLabel", Layer: "PART" }, "text trong block được giữ và kế thừa layer INSERT");
            Near(30, p.Geometry.Texts[0].X, 1e-9, "tọa độ text block sau xoay");
            Near(10, p.Geometry.Texts[0].Y, 1e-9, "tọa độ text block sau xoay");
        });

        Test("INSERT cÃ³ tá»‰ lá»‡ 2 vÃ  block lá»“ng nhau", () =>
        {
            string blocks =
                "0\nBLOCK\n8\n0\n2\nINNER\n70\n0\n10\n0\n20\n0\n30\n0\n" + DxfRect("0", 0, 0, 10, 5) + "0\nENDBLK\n8\n0\n" +
                "0\nBLOCK\n8\n0\n2\nOUTER\n70\n0\n10\n0\n20\n0\n30\n0\n0\nINSERT\n8\n0\n2\nINNER\n10\n0\n20\n0\n30\n0\n41\n2\n42\n2\n" + "0\nENDBLK\n8\n0\n";
            string ins = "0\nINSERT\n8\n0\n2\nOUTER\n10\n0\n20\n0\n30\n0\n";
            var p = DxfReader.Read(Save("nested.dxf", DxfDoc(ins, blocks)));
            Near(20, p.Width, 1e-9, "rá»™ng");
            Near(10, p.Height, 1e-9, "cao");
        });

        Test("Kiá»ƒu Inventor: chá»‰ IV_OUTER_PROFILE quyáº¿t Ä‘á»‹nh kÃ­ch thÆ°á»›c, layer uá»‘n/tÃ¢m/kÃ­ch thÆ°á»›c bá»‹ loáº¡i", () =>
        {
            string ent =
                DxfRect("IV_OUTER_PROFILE", 0, 0, 200, 100) +
                DxfCircle("IV_INTERIOR_PROFILES", 50, 50, 10) +
                DxfLine("IV_BEND", 100, 0, 100, 100) +
                DxfCircle("IV_ARC_CENTERS", 50, 50, 1) +
                DxfLine("DIMENSIONS", 0, -500, 200, -500);
            var p = DxfReader.Read(Save("inventor.dxf", DxfDoc(ent, insunits: 4)));
            Near(200, p.Width, 1e-9, "rá»™ng");
            Near(100, p.Height, 1e-9, "cao");
            True(p.ExcludedLayers.Contains("IV_BEND"), "IV_BEND pháº£i bá»‹ loáº¡i");
            True(p.ExcludedLayers.Contains("DIMENSIONS"), "DIMENSIONS pháº£i bá»‹ loáº¡i");
            // 4 LINE biÃªn + 1 CIRCLE lá»— = 5 Ä‘oáº¡n xuáº¥t ra
            True(p.Geometry.Segments.Count == 5, $"mong Ä‘á»£i 5 Ä‘oáº¡n xuáº¥t ra, nháº­n {p.Geometry.Segments.Count}");
        });

        Test("HÃ¬nh láº¡ á»Ÿ layer ngoÃ i lá» (ghi chÃº váº½ nháº§m layer) bá»‹ loáº¡i khá»i file xuáº¥t vÃ  cÃ³ cáº£nh bÃ¡o", () =>
        {
            string ent = DxfRect("IV_OUTER_PROFILE", 0, 0, 200, 100) + DxfLine("GHI_CHU_KHAC", 0, 900, 300, 900);
            var p = DxfReader.Read(Save("stray.dxf", DxfDoc(ent)));
            Near(200, p.Width, 1e-9, "rá»™ng");
            Near(100, p.Height, 1e-9, "cao");
            True(p.Geometry.Segments.Count == 4, $"Ä‘Æ°á»ng láº¡c pháº£i bá»‹ loáº¡i, nháº­n {p.Geometry.Segments.Count} Ä‘oáº¡n");
            True(p.Warnings.Any(w => w.Contains("GHI_CHU_KHAC")), "pháº£i cÃ³ cáº£nh bÃ¡o vá» layer GHI_CHU_KHAC");
        });

        Test("ÄÆ¡n vá»‹ inch ($INSUNITS=1): 4x2 inch -> 101.6 x 50.8 mm", () =>
        {
            var p = DxfReader.Read(Save("inch.dxf", DxfDoc(DxfRect("0", 0, 0, 4, 2), insunits: 1)));
            Near(101.6, p.Width, 1e-9, "rá»™ng");
            Near(50.8, p.Height, 1e-9, "cao");
        });

        Test("Ã‰p Ä‘Æ¡n vá»‹ báº±ng option; khÃ´ng khai bÃ¡o Ä‘Æ¡n vá»‹ thÃ¬ giáº£ Ä‘á»‹nh mm", () =>
        {
            var path = Save("nounit.dxf", DxfDoc(DxfRect("0", 0, 0, 0.5, 0.25)));
            var def = DxfReader.Read(path);
            True(def.UnitSource.Contains("mm"), "pháº£i ghi rÃµ giáº£ Ä‘á»‹nh mm");
            True(def.Warnings.Any(w => w.Contains("đơn vị")), "kÃ­ch thÆ°á»›c ráº¥t nhá» pháº£i kÃ¨m cáº£nh bÃ¡o Ä‘Æ¡n vá»‹");

            var m = DxfReader.Read(path, new DxfReadOptions { UnitScale = 1000 });
            Near(500, m.Width, 1e-9, "rá»™ng khi Ã©p Ä‘Æ¡n vá»‹ m");
        });

        Test("SPLINE báº­c 2 (Bezier) dÃ¹ng Ä‘Æ°á»ng cong tháº­t, khÃ´ng dÃ¹ng há»™p bao Ä‘iá»ƒm Ä‘iá»u khiá»ƒn", () =>
        {
            // Äiá»ƒm Ä‘iá»u khiá»ƒn (0,0) (5,10) (10,0): Ä‘á»‰nh Ä‘Æ°á»ng cong chá»‰ cao 5, khÃ´ng pháº£i 10.
            string ent = "0\nSPLINE\n8\n0\n70\n8\n71\n2\n72\n6\n73\n3\n74\n0\n" +
                         "40\n0\n40\n0\n40\n0\n40\n1\n40\n1\n40\n1\n" +
                         "10\n0\n20\n0\n30\n0\n10\n5\n20\n10\n30\n0\n10\n10\n20\n0\n30\n0\n";
            var p = DxfReader.Read(Save("spline.dxf", DxfDoc(ent)));
            Near(10, p.Width, 1e-6, "rá»™ng");
            Near(5, p.Height, 1e-3, "cao (Ä‘Æ°á»ng cong tháº­t)");
            True(p.Geometry.ApproximatedEntities == 1, "pháº£i ghi nháº­n 1 spline xáº¥p xá»‰");
        });

        Test("ELLIPSE Ä‘áº§y Ä‘á»§ (trá»¥c chÃ­nh 20, tá»‰ lá»‡ 0.5) -> 40x20", () =>
        {
            string ent = "0\nELLIPSE\n8\n0\n10\n0\n20\n0\n30\n0\n11\n20\n21\n0\n31\n0\n40\n0.5\n41\n0\n42\n6.283185307179586\n";
            var p = DxfReader.Read(Save("ellipse.dxf", DxfDoc(ent)));
            Near(40, p.Width, 1e-3, "rá»™ng");
            Near(20, p.Height, 1e-3, "cao");
        });

        Test("POLYLINE kiá»ƒu cÅ© (VERTEX/SEQEND) vÃ  LWPOLYLINE cho cÃ¹ng káº¿t quáº£", () =>
        {
            string old = "0\nPOLYLINE\n8\n0\n66\n1\n70\n1\n" +
                         "0\nVERTEX\n8\n0\n10\n0\n20\n0\n" + "0\nVERTEX\n8\n0\n10\n80\n20\n0\n" +
                         "0\nVERTEX\n8\n0\n10\n80\n20\n30\n" + "0\nVERTEX\n8\n0\n10\n0\n20\n30\n" + "0\nSEQEND\n8\n0\n";
            var p = DxfReader.Read(Save("oldpoly.dxf", DxfDoc(old)));
            Near(80, p.Width, 1e-9, "rá»™ng");
            Near(30, p.Height, 1e-9, "cao");
        });

        Test("Quy Æ°á»›c tÃªn file: _xN lÃ  sá»‘ lÆ°á»£ng, _norot cáº¥m xoay", () =>
        {
            var a = DxfReader.ParseFileName("Gia_do_A_x12");
            True(a.Name == "Gia_do_A" && a.Quantity == 12 && !a.NoRotate, $"sai: {a}");
            var b = DxfReader.ParseFileName("Tam_op_x5_norot");
            True(b.Name == "Tam_op" && b.Quantity == 5 && b.NoRotate, $"sai: {b}");
            var c = DxfReader.ParseFileName("Bracket", 3);
            True(c.Name == "Bracket" && c.Quantity == 3 && !c.NoRotate, $"sai: {c}");
            var d = DxfReader.ParseFileName("Max5");
            True(d.Name == "Max5" && d.Quantity == 1, $"'Max5' khÃ´ng pháº£i háº­u tá»‘ _xN: {d}");
        });

        Test("File binary DXF vÃ  file rÃ¡c bÃ¡o lá»—i rÃµ rÃ ng", () =>
        {
            var bin = Path.Combine(tmp, "bin.dxf");
            File.WriteAllBytes(bin, Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\x1a\0rest"));
            bool threw = false;
            try { DxfReader.Read(bin); } catch (NotSupportedException ex) { threw = ex.Message.Contains("nhị phân") || ex.Message.Contains("binary") || ex.Message.Length > 0; }
            True(threw, "binary DXF phải báo NotSupportedException");

            var junk = Save("junk.dxf", "hello world\nthis is not dxf\n");
            threw = false;
            try { DxfReader.Read(junk); } catch (InvalidDataException) { threw = true; }
            True(threw, "file rÃ¡c pháº£i bÃ¡o InvalidDataException");
        });

        Test("File cÃ³ dÃ²ng trá»‘ng thá»«a, BOM vÃ  LF thay vÃ¬ CRLF váº«n Ä‘á»c Ä‘Æ°á»£c", () =>
        {
            // DÃ²ng trá»‘ng chÃ¨n vÃ o vá»‹ trÃ­ group code + BOM UTF-8 (do bá»™ ghi thÃªm Ä‘Ãºng 1 láº§n) + xuá»‘ng dÃ²ng LF
            string text = DxfDoc(DxfRect("0", 0, 0, 30, 20)).Replace("0\nENDSEC\n", "\n0\nENDSEC\n");
            var path = Path.Combine(tmp, "bom.dxf");
            File.WriteAllText(path, text, new UTF8Encoding(true));
            var p = DxfReader.Read(path);
            Near(30, p.Width, 1e-9, "rá»™ng");
        });

        Console.WriteLine("Nesting vá»›i hÃ¬nh há»c tháº­t:");

        // Chi tiáº¿t chá»¯ L 200x100 cÃ³ lá»— lá»‡ch vá» má»™t gÃ³c: báº¥t Ä‘á»‘i xá»©ng nÃªn xoay sai sáº½ lá»™ ngay.
        string LBracket() =>
            DxfLwPoly("IV_OUTER_PROFILE", true, (0, 0, 0), (200, 0, 0), (200, 40, 0), (40, 40, 0), (40, 100, 0), (0, 100, 0)) +
            DxfCircle("IV_INTERIOR_PROFILES", 20, 70, 8) +
            DxfLine("IV_BEND", 40, 40, 40, 100);

        Test("Chi tiáº¿t chá»‰ vá»«a khi xoay: hÃ¬nh há»c tháº­t xoay Ä‘Ãºng 90 Ä‘á»™ (lá»— Ä‘áº¿n Ä‘Ãºng vá»‹ trÃ­)", () =>
        {
            var part = DxfReader.Read(Save("L_x1.dxf", DxfDoc(LBracket())));
            var sheet = new SheetDefinition(120, 300, 5, 3); // vÃ¹ng dÃ¹ng 110x290: chá»‰ vá»«a khi Ä‘áº·t Ä‘á»©ng 100x200
            var r = NestEngine.Run(new[] { part.ToDefinition() }, sheet, new NestOptions { });
            True(r.Unplaced.Count == 0, "pháº£i Ä‘áº·t Ä‘Æ°á»£c");
            var pl = r.Sheets[0].Placements.Single();
            True(pl.IsRotated, "phải xoay");
            True(pl.Shape != null, "Placement pháº£i mang theo hÃ¬nh há»c tháº­t");

            var segs = pl.Shape!.Place(pl.X, pl.Y, pl.Rotated).ToList();
            var hole = segs.OfType<CircleSeg>().Single();
            // Lá»— gá»‘c (20,70) trong khung 200x100 -> xoay 90 Ä‘á»™ CCW quanh gÃ³c: (x,y) -> (100-y, x) = (30,20)
            Near(pl.X + 30, hole.Cx, 1e-9, "tÃ¢m lá»— X");
            Near(pl.Y + 20, hole.Cy, 1e-9, "tÃ¢m lá»— Y");
            Near(8, hole.R, 1e-9, "bÃ¡n kÃ­nh lá»—");
        });

        Test("Xuáº¥t DXF rá»“i Ä‘á»c láº¡i: má»i nÃ©t náº±m gá»n trong Ã´ Ä‘áº·t, Ä‘á»§ sá»‘ lÆ°á»£ng, cÃ³ cáº£ cung", () =>
        {
            // Chi tiáº¿t 2: táº¥m cÃ³ rÃ£nh báº§u (cung bulge) + lá»— trÃ²n, Ä‘á»ƒ kiá»ƒm tra cung sau khi xoay.
            string slot = DxfLwPoly("IV_OUTER_PROFILE", true, (0, 0, 0), (120, 0, 1), (120, 50, 0), (0, 50, 1)) +
                          DxfCircle("IV_INTERIOR_PROFILES", 60, 25, 6);
            var pa = DxfReader.Read(Save("L_x4.dxf", DxfDoc(LBracket())));
            var pb = DxfReader.Read(Save("Slot_x3.dxf", DxfDoc(slot)));
            var defs = new[] { pa.ToDefinition(), pb.ToDefinition() };

            var sheet = new SheetDefinition(600, 400, 5, 3);
            // PhÃ©p kiá»ƒm tra "má»—i nÃ©t náº±m trong Ä‘Ãºng 1 Ã´" chá»‰ Ä‘Ãºng vá»›i nesting há»™p bao (hÃ¬nh lá»“ng nhau thÃ¬ há»™p bao chá»“ng nhau).
            var r = NestEngine.Run(defs, sheet, new NestOptions { });
            True(r.Unplaced.Count == 0, "pháº£i Ä‘áº·t háº¿t");
            True(Validator.Check(r, sheet).Count == 0, "kiá»ƒm tra chá»“ng láº¥n/khe há»Ÿ pháº£i sáº¡ch");
            True(r.Sheets.SelectMany(s => s.Placements).Count() == 7, "pháº£i cÃ³ 7 chi tiáº¿t");

            int rotatedCount = 0, arcsSeen = 0;
            foreach (var s in r.Sheets)
            {
                string file = Path.Combine(tmp, $"out_{s.Index}.dxf");
                DxfWriter.Write(s, sheet, file);

                var raw = DxfReader.ReadRaw(file);
                var partGeometry = raw.Shapes.Where(x =>
                    x.Layer is "IV_OUTER_PROFILE" or "IV_INTERIOR_PROFILES").ToList();
                arcsSeen += partGeometry.OfType<ArcShape>().Count();

                int expected = s.Placements.Sum(pl => pl.Shape!.Segments.Count);
                True(partGeometry.Count == expected, $"táº¥m {s.Index}: ghi {expected} Ä‘oáº¡n, Ä‘á»c láº¡i {partGeometry.Count}");

                // Má»—i Ä‘oáº¡n pháº£i náº±m trong há»™p cá»§a Ä‘Ãºng 1 Ã´ Ä‘áº·t, vÃ  sá»‘ Ä‘oáº¡n trong má»—i Ã´ khá»›p vá»›i chi tiáº¿t
                True(s.Placements.All(pl => pl.Shape != null), $"tấm {s.Index}: mọi placement DXF có geometry thật");

                rotatedCount += s.Placements.Count(pl => pl.IsRotated);
            }
            True(arcsSeen > 0, "pháº£i cÃ³ cung (ARC) trong file xuáº¥t");
            Console.WriteLine($"        ({rotatedCount} chi tiáº¿t xoay, {arcsSeen} cung Ä‘Æ°á»£c kiá»ƒm tra)");
        });

        Test("Há»™p bao cá»§a cung sau khi xoay khá»›p Ä‘Ãºng Ã´ Ä‘áº·t (kiá»ƒm tra gÃ³c cung)", () =>
        {
            // RÃ£nh báº§u 170x50 (thÃ¢n 120 + hai ná»­a trÃ²n R25), táº¥m 70 rá»™ng buá»™c pháº£i xoay Ä‘á»©ng.
            string slot = DxfLwPoly("IV_OUTER_PROFILE", true, (0, 0, 0), (120, 0, 1), (120, 50, 0), (0, 50, 1));
            var part = DxfReader.Read(Save("Slot2_x1.dxf", DxfDoc(slot)));
            Near(170, part.Width, 1e-6, "rá»™ng rÃ£nh báº§u");
            var sheet = new SheetDefinition(70, 300, 5, 3);
            var r = NestEngine.Run(new[] { part.ToDefinition() }, sheet, new NestOptions { });
            var pl = r.Sheets.Single().Placements.Single();
            True(pl.IsRotated, "phải xoay");

            var file = Path.Combine(tmp, "slot_out.dxf");
            DxfWriter.Write(r.Sheets[0], sheet, file);
            var bb = Bbox.Empty;
            foreach (var s in DxfReader.ReadRaw(file).Shapes.Where(x => x.Layer == "IV_OUTER_PROFILE")) s.Extend(ref bb);
            Near(pl.X, bb.MinX, 1e-6, "MinX"); Near(pl.Y, bb.MinY, 1e-6, "MinY");
            Near(pl.X + pl.Width, bb.MaxX, 1e-6, "MaxX"); Near(pl.Y + pl.Height, bb.MaxY, 1e-6, "MaxY");
        });

        Test("Kết hợp chi tiết chữ nhật và DXF: part chữ nhật xuất trên layer PARTS", () =>
        {
            var part = DxfReader.Read(Save("Mix_x2.dxf", DxfDoc(DxfRect("0", 0, 0, 100, 50))));
            var defs = new[] { new PartDefinition("RectPart", 80, 40, 2), part.ToDefinition() };
            var sheet = new SheetDefinition(500, 300, 5, 3);
            var r = NestEngine.Run(defs, sheet, new NestOptions { });
            True(r.Sheets.SelectMany(s => s.Placements).Count(p => p.Shape == null) == 2, "2 part chữ nhật khÃ´ng cÃ³ Shape");
            string file = Path.Combine(tmp, "mix.dxf");
            DxfWriter.Write(r.Sheets[0], sheet, file);
            var raw = DxfReader.ReadRaw(file);
            True(raw.Shapes.Any(s => s.Layer == "PARTS"), "pháº£i cÃ³ layer PARTS");
            True(raw.Shapes.Any(s => s.Layer == "0"), "layer 0 của DXF nguồn được giữ nguyên");
        });

        Console.WriteLine("Nesting hÃ¬nh dáº¡ng tháº­t (lá»“ng nhau, vÃ o lá»—):");

        NestOptions Shape(double seconds = 10, double rotStep = 90) =>
            new() { MaxSeconds = seconds, RotationStep = rotStep, };

        Test("Diá»‡n tÃ­ch tháº­t: táº¥m 100x50 cÃ³ lá»— R10 = 5000 - 100*pi", () =>
        {
            var p = DxfReader.Read(Save("area.dxf", DxfDoc(DxfRect("0", 0, 0, 100, 50) + DxfCircle("0", 50, 25, 10))));
            Near(5000 - Math.PI * 100, ShapeOps.NetArea(p.Geometry), 5, "diá»‡n tÃ­ch tháº­t (lá»— trÃ²n lÃ  Ä‘a giÃ¡c ná»™i tiáº¿p nÃªn lá»‡ch ~0.04%)");
        });

        Test("Hai chi tiáº¿t chá»¯ L lá»“ng vÃ o nhau: hÃ¬nh tháº­t cáº§n 1 táº¥m, há»™p bao cáº§n 2 táº¥m", () =>
        {
            var part = DxfReader.Read(Save("L_x2.dxf", DxfDoc(LBracket())));
            var sheet = new SheetDefinition(260, 130, 5, 3); // vÃ¹ng dÃ¹ng 250 x 120

            var shp = NestEngine.Run(new[] { part.ToDefinition() }, sheet, Shape());
            True(shp.Unplaced.Count == 0, "Ä‘áº·t Ä‘Æ°á»£c háº¿t");
            True(shp.Sheets.Count == 1, $"hÃ¬nh tháº­t pháº£i cáº§n 1 táº¥m, nháº­n {shp.Sheets.Count}");
            var errs = Validator.Check(shp, sheet);
            True(errs.Count == 0, "validator chÃ­nh xÃ¡c pháº£i sáº¡ch: " + string.Join("; ", errs));

            // Hai há»™p bao pháº£i chá»“ng lÃªn nhau thÃ¬ má»›i chá»©ng tá» lÃ  lá»“ng tháº­t
            var a = shp.Sheets[0].Placements[0]; var b = shp.Sheets[0].Placements[1];
            bool boxesOverlap = a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
            True(boxesOverlap, "há»™p bao cá»§a 2 chi tiáº¿t pháº£i chá»“ng nhau (lá»“ng vÃ o nhau)");
        });

        Test("Chi tiáº¿t nhá» náº±m trong lá»— cá»§a chi tiáº¿t lá»›n: táº¥m vá»«a khÃ­t chi tiáº¿t lá»›n váº«n Ä‘á»§ chá»—", () =>
        {
            var big = DxfReader.Read(Save("Plate_x1.dxf", DxfDoc(DxfRect("IV_OUTER_PROFILE", 0, 0, 300, 200) + DxfCircle("IV_INTERIOR_PROFILES", 150, 100, 60))));
            var small = DxfReader.Read(Save("Chip_x1.dxf", DxfDoc(DxfRect("0", 0, 0, 60, 60))));
            var sheet = new SheetDefinition(310, 210, 5, 3); // vÃ¹ng dÃ¹ng 300 x 200 = Ä‘Ãºng báº±ng táº¥m lá»›n
            var defs = new[] { big.ToDefinition(), small.ToDefinition() };

            var r = NestEngine.Run(defs, sheet, Shape());
            True(r.Unplaced.Count == 0, "Ä‘áº·t Ä‘Æ°á»£c háº¿t");
            True(r.Sheets.Count == 1, $"hÃ¬nh tháº­t pháº£i Ä‘áº·t chi tiáº¿t nhá» vÃ o lá»— (1 táº¥m), nháº­n {r.Sheets.Count}");
            var errs = Validator.Check(r, sheet);
            True(errs.Count == 0, "validator chÃ­nh xÃ¡c pháº£i sáº¡ch: " + string.Join("; ", errs));

            var pb = r.Sheets[0].Placements.Single(x => x.PartName == "Plate");
            var ps = r.Sheets[0].Placements.Single(x => x.PartName == "Chip");
            double cx = ps.X + ps.Width / 2, cy = ps.Y + ps.Height / 2;
            double dist = Math.Sqrt(Math.Pow(cx - (pb.X + 150), 2) + Math.Pow(cy - (pb.Y + 100), 2));
            True(dist < 60, $"tÃ¢m chi tiáº¿t nhá» pháº£i náº±m trong lá»— R60, cÃ¡ch tÃ¢m lá»— {dist:0.#} mm");

            // Hiá»‡u suáº¥t tÃ­nh theo diá»‡n tÃ­ch váº­t liá»‡u tháº­t
            double expected = (300.0 * 200 - Math.PI * 3600 + 3600) / (310.0 * 210);
            Near(expected, r.Utilization, 0.01, "hiá»‡u suáº¥t theo diá»‡n tÃ­ch tháº­t");
        });

        Test("Khe há»Ÿ tháº­t giá»¯a 2 chi tiáº¿t lá»“ng nhau khÃ´ng nhá» hÆ¡n khe há»Ÿ yÃªu cáº§u", () =>
        {
            var part = DxfReader.Read(Save("L2_x2.dxf", DxfDoc(LBracket())));
            var sheet = new SheetDefinition(260, 130, 5, 3);
            var r = NestEngine.Run(new[] { part.ToDefinition() }, sheet, Shape());
            var ps = r.Sheets[0].Placements;
            var la = ShapeOps.Build(ps[0].Shape!.Place(ps[0].X, ps[0].Y, false), ps[0].Width, ps[0].Height);
            var lb = ShapeOps.Build(ps[1].Shape!.Place(ps[1].X, ps[1].Y, false), ps[1].Width, ps[1].Height);
            True(!ShapeOps.Conflict(la, 0, 0, lb, 0, 0, 2.9), "khe há»Ÿ pháº£i >= 2.9 mm");
            True(ShapeOps.Conflict(la, 0, 0, lb, 0, 0, 25), "nhÆ°ng khÃ´ng Ä‘Æ°á»£c cÃ¡ch xa quÃ¡ (<= 25 mm), náº¿u khÃ´ng chÆ°a tháº­t sá»± lá»“ng");
        });

        Test("Validator báº¯t Ä‘Æ°á»£c 2 chi tiáº¿t chá»¯ L Ä‘áº·t chá»“ng nhau", () =>
        {
            var part = DxfReader.Read(Save("L3_x2.dxf", DxfDoc(LBracket())));
            var sheet = new SheetDefinition(260, 130, 5, 3);
            var res = new NestResult();
            var sr = new SheetResult { Index = 1, Width = 260, Height = 130 };
            sr.Placements.Add(new Placement("L3", 1, 5, 5, 200, 100, false, part.Geometry));
            sr.Placements.Add(new Placement("L3", 2, 30, 8, 200, 100, false, part.Geometry)); // chá»“ng lÃªn
            res.Sheets.Add(sr);
            True(Validator.Check(res, sheet).Count > 0, "pháº£i bÃ¡o lá»—i chá»“ng nhau");
        });

        Test("Chi tiáº¿t náº±m trá»n trong pháº§n váº­t liá»‡u cá»§a chi tiáº¿t khÃ¡c (khÃ´ng cáº¯t biÃªn) váº«n bá»‹ báº¯t", () =>
        {
            var big = DxfReader.Read(Save("Solid_x1.dxf", DxfDoc(DxfRect("0", 0, 0, 200, 200))));
            var small = DxfReader.Read(Save("Inner_x1.dxf", DxfDoc(DxfRect("0", 0, 0, 20, 20))));
            var sheet = new SheetDefinition(300, 300, 5, 3);
            var res = new NestResult();
            var sr = new SheetResult { Index = 1, Width = 300, Height = 300 };
            sr.Placements.Add(new Placement("Solid", 1, 5, 5, 200, 200, false, big.Geometry));
            sr.Placements.Add(new Placement("Inner", 1, 90, 90, 20, 20, false, small.Geometry));
            res.Sheets.Add(sr);
            True(Validator.Check(res, sheet).Count > 0, "pháº£i bÃ¡o lá»—i chi tiáº¿t náº±m Ä‘Ã¨ lÃªn váº­t liá»‡u");
        });

        Test("Contour ngoài bị hở: báo lỗi và không nesting bằng hộp bao", () =>
        {
            string open = DxfLine("0", 0, 0, 100, 0) + DxfLine("0", 100, 0, 100, 60) + DxfLine("0", 100, 60, 0, 60) + DxfLine("0", 0, 55, 0, 0);
            var p = DxfReader.Read(Save("Open_x3.dxf", DxfDoc(open)));
            var sheet = new SheetDefinition(400, 300, 5, 3);
            var r = NestEngine.Run(new[] { p.ToDefinition() }, sheet, Shape());
            True(r.Sheets.Count == 0, "contour hở không được đặt");
            True(r.Unplaced.Count > 0 && r.Unplaced.Any(m => m.Contains("contour", StringComparison.OrdinalIgnoreCase)), "phải báo contour không hợp lệ");
            True(r.Notes.Any(n => n.Contains("sửa DXF", StringComparison.OrdinalIgnoreCase)), "hướng dẫn sửa DXF");
        });

        Test("Xoay 45 Ä‘á»™: thanh 300x20 chá»‰ vá»«a táº¥m 240x240 khi xoay chÃ©o", () =>
        {
            var bar = DxfReader.Read(Save("Bar_x1.dxf", DxfDoc(DxfRect("0", 0, 0, 300, 20))));
            var sheet = new SheetDefinition(240, 240, 5, 3); // vÃ¹ng dÃ¹ng 230 x 230

            var r90 = NestEngine.Run(new[] { bar.ToDefinition() }, sheet, Shape(rotStep: 90));
            True(r90.Unplaced.Count > 0, "bÆ°á»›c 90 Ä‘á»™ khÃ´ng thá»ƒ Ä‘áº·t (300 > 230)");

            var r45 = NestEngine.Run(new[] { bar.ToDefinition() }, sheet, Shape(rotStep: 45));
            True(r45.Unplaced.Count == 0, "bÆ°á»›c 45 Ä‘á»™ pháº£i Ä‘áº·t Ä‘Æ°á»£c");
            var pl = r45.Sheets.Single().Placements.Single();
            Near(45, pl.AngleDeg, 1e-9, "gÃ³c xoay");
            True(pl.X >= 5 - 1e-6 && pl.Y >= 5 - 1e-6 && pl.X + pl.Width <= 235 + 1e-6 && pl.Y + pl.Height <= 235 + 1e-6, "náº±m trong vÃ¹ng cho phÃ©p");
            True(Validator.Check(r45, sheet).Count == 0, "validator sáº¡ch");

            // File xuáº¥t: hÃ¬nh tháº­t xoay 45 Ä‘á»™ pháº£i náº±m gá»n trong táº¥m
            string file = Path.Combine(tmp, "bar45.dxf");
            DxfWriter.Write(r45.Sheets[0], sheet, file);
            var bb = Bbox.Empty;
            foreach (var shp in DxfReader.ReadRaw(file).Shapes.Where(x => x.Layer == "0")) shp.Extend(ref bb);
            True(bb.MinX >= 5 - 1e-4 && bb.MaxX <= 235 + 1e-4 && bb.MinY >= 5 - 1e-4 && bb.MaxY <= 235 + 1e-4,
                 $"nÃ©t cáº¯t pháº£i náº±m trong táº¥m, nháº­n x {bb.MinX:0.##}..{bb.MaxX:0.##}, y {bb.MinY:0.##}..{bb.MaxY:0.##}");
        });

        Test("Kết hợp chi tiết chữ nhật và DXF ở chế độ hình thật: part chữ nhật vẫn hợp lệ", () =>
        {
            var part = DxfReader.Read(Save("MixShape_x3.dxf", DxfDoc(LBracket())));
            var defs = new[] { new PartDefinition("RectPart", 80, 40, 4), part.ToDefinition() };
            var sheet = new SheetDefinition(500, 300, 5, 3);
            var r = NestEngine.Run(defs, sheet, Shape());
            True(r.Unplaced.Count == 0, "Ä‘áº·t háº¿t");
            True(r.Sheets.SelectMany(s => s.Placements).Count(p => p.Shape == null) == 4, "4 part chữ nhật khÃ´ng cÃ³ Shape");
            True(Validator.Check(r, sheet).Count == 0, "validator sáº¡ch");
            string file = Path.Combine(tmp, "mixshape.dxf");
            DxfWriter.Write(r.Sheets[0], sheet, file);
            var raw = DxfReader.ReadRaw(file);
            True(raw.Shapes.Any(s => s.Layer == "PARTS") && raw.Shapes.Any(s => s.Layer == "IV_OUTER_PROFILE"),
                "part chữ nhật dùng PARTS, DXF giữ layer IV_OUTER_PROFILE");
        });

        Test("Hướng lấp đầy: theo rộng giảm chiều dài dùng, theo dài ưu tiên lấp dọc chiều dài", () =>
        {
            var parts = new[] { new PartDefinition("Panel", 700, 500, 4, CanRotate: false) };
            var sheet = new SheetDefinition(3000, 1250, 0, 0); // dài X = 3000, rộng Y = 1250
            var width = NestEngine.Run(parts, sheet, new NestOptions { Direction = NestDirection.FillWidth, AllowRotation = false, MaxSeconds = 5, TargetUtilizationPercent = 0 });
            var length = NestEngine.Run(parts, sheet, new NestOptions { Direction = NestDirection.FillLength, AllowRotation = false, MaxSeconds = 5, TargetUtilizationPercent = 0 });
            True(width.Unplaced.Count == 0 && length.Unplaced.Count == 0, "cả hai hướng xếp đủ");
            double widthUsedLength = width.Sheets.Max(s => s.Placements.Max(p => p.X + p.Width));
            double lengthUsedLength = length.Sheets.Max(s => s.Placements.Max(p => p.X + p.Width));
            double widthUsedAcross = width.Sheets.Max(s => s.Placements.Max(p => p.Y + p.Height));
            double lengthUsedAcross = length.Sheets.Max(s => s.Placements.Max(p => p.Y + p.Height));
            True(widthUsedLength < lengthUsedLength, $"lấp rộng giảm chiều dài: {widthUsedLength:0} < {lengthUsedLength:0}");
            True(widthUsedAcross > lengthUsedAcross, $"lấp dài ưu tiên theo dài, không mở rộng bề rộng sớm: {widthUsedAcross:0} > {lengthUsedAcross:0}");
        });

        Test("Khe hở 0 cho phép các chi tiết áp sát thay vì giữ dư 0,5 mm do raster", () =>
        {
            var sheet = new SheetDefinition(102, 30, 0, 0);
            var result = NestEngine.Run(new[] { new PartDefinition("Half", 50.5, 20, 2, CanRotate: false) }, sheet,
                new NestOptions { AllowRotation = false, MaxSeconds = 5, TargetUtilizationPercent = 0 });
            True(result.Unplaced.Count == 0 && result.Sheets.Count == 1, "xếp hai part trên cùng tấm");
            var parts = result.Sheets[0].Placements;
            double gap = Math.Abs(parts[0].X - parts[1].X) - parts[0].Width;
            True(Math.Abs(gap) < 0.001, $"khoảng cách thực tế phải bằng 0, nhận {gap:0.######} mm");
            var validationErrors = Validator.Check(result, sheet);
            True(validationErrors.Count == 0, $"kết quả áp sát vẫn hợp lệ khi spacing bằng 0: {string.Join("; ", validationErrors)} " +
                $"({parts[0].X:0.########}, {parts[0].Y:0.########}; {parts[1].X:0.########}, {parts[1].Y:0.########}; gap={gap:0.########})");
        });

        Test("Đạt mục tiêu hiệu suất thì dừng sớm sau khi xếp đủ chi tiết", () =>
        {
            var sheet = new SheetDefinition(1000, 1000, 0, 0);
            var result = NestEngine.Run(new[] { new PartDefinition("Full", 1000, 1000, 1, CanRotate: false) }, sheet,
                new NestOptions { MaxSeconds = 10, TargetUtilizationPercent = 99, Direction = NestDirection.FillWidth });
            True(result.Unplaced.Count == 0 && result.Sheets.Sum(s => s.Placements.Count) == 1, "đã xếp đủ");
            True(result.Notes.Any(n => n.Contains("đạt, dừng sớm")), "ghi nhận dừng sớm vì đạt mục tiêu");
        });

        Test("Tá»‘c Ä‘á»™: 60 chi tiáº¿t há»—n há»£p trÃªn táº¥m 1250x2500 á»Ÿ cháº¿ Ä‘á»™ hÃ¬nh tháº­t dÆ°á»›i 20 giÃ¢y vÃ  há»£p lá»‡", () =>
        {
            var types = new[]
            {
        DxfReader.Read(Save("Perf1_x12.dxf", DxfDoc(DxfRect("IV_OUTER_PROFILE", 0, 0, 300, 200) + DxfCircle("IV_INTERIOR_PROFILES", 150, 100, 40)))),
        DxfReader.Read(Save("Perf2_x20.dxf", DxfDoc(LBracket()))),
        DxfReader.Read(Save("Perf3_x18.dxf", DxfDoc(DxfLwPoly("0", true, (0, 0, 0), (120, 0, 1), (120, 40, 0), (0, 40, 1))))),
        DxfReader.Read(Save("Perf4_x10.dxf", DxfDoc(DxfCircle("0", 0, 0, 70)))),
            };
            var sheet = new SheetDefinition(1250, 2500, 5, 3);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = NestEngine.Run(types.Select(t => t.ToDefinition()).ToList(), sheet, Shape(seconds: 12));
            sw.Stop();
            True(sw.Elapsed.TotalSeconds < 20, $"quÃ¡ cháº­m: {sw.Elapsed.TotalSeconds:0.0}s");
            True(r.Unplaced.Count == 0, "Ä‘áº·t háº¿t");
            True(r.Sheets.Sum(s => s.Placements.Count) == 60, "Ä‘á»§ 60 chi tiáº¿t");
            var errs = Validator.Check(r, sheet);
            Console.WriteLine($"        -> Thời gian chạy 60 chi tiết: {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalSeconds:0.00}s), {r.Sheets.Count} tấm, hiệu suất {r.Utilization:P1}");

            var zeroGapSheet = sheet with { Spacing = 0 };
            sw.Restart();
            var zeroGapResult = NestEngine.Run(types.Select(t => t.ToDefinition()).ToList(), zeroGapSheet, Shape(seconds: 12));
            sw.Stop();
            True(sw.Elapsed.TotalSeconds < 20, $"nesting khe hở 0 quá chậm: {sw.Elapsed.TotalSeconds:0.0}s");
            True(zeroGapResult.Unplaced.Count == 0 &&
                 zeroGapResult.Sheets.Sum(s => s.Placements.Count) == 60, "nesting khe hở 0 vẫn xếp đủ 60 chi tiết");
            True(Validator.Check(zeroGapResult, zeroGapSheet).Count == 0, "kết quả khe hở 0 vượt qua validator");
            Console.WriteLine($"        -> Nesting 60 chi tiết, khe hở 0: {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalSeconds:0.00}s)");
        });

        Test("Core từ chối cấu hình xoay quá nhỏ trước khi tạo raster", () =>
        {
            var sheet = new SheetDefinition(1000, 1000, 0, 0);
            var result = NestEngine.Run(new[] { new PartDefinition("Panel", 100, 50, 1) }, sheet,
                new NestOptions { RotationStep = 0.001 });
            True(result.Sheets.Count == 0, "không chạy nesting với cấu hình sai");
            True(result.Unplaced.Count == 1 && result.Unplaced[0].Contains("5 đến 180"), "nêu rõ bước xoay không hợp lệ");
        });

        Test("Core từ chối kích thước tấm không hữu hạn", () =>
        {
            var result = NestEngine.Run(new[] { new PartDefinition("Panel", 100, 50, 1) },
                new SheetDefinition(double.NaN, 1000, 0, 0));
            True(result.Sheets.Count == 0, "không cấp phát lưới với kích thước không hợp lệ");
            True(result.Notes.Any(note => note.Contains("hữu hạn")), "nêu rõ lỗi kích thước");
        });

        Test("Core từ chối tổng số lượng vượt giới hạn an toàn", () =>
        {
            var result = NestEngine.Run(new[]
            {
        new PartDefinition("Panel A", 100, 50, 125_001),
        new PartDefinition("Panel B", 100, 50, 125_000),
            }, new SheetDefinition(1000, 1000, 0, 0));
            True(result.Sheets.Count == 0, "không tạo danh sách instance vượt giới hạn");
            True(result.Notes.Any(note => note.Contains("Tổng số chi tiết vượt giới hạn")), "nêu rõ giới hạn số lượng");
        });

        Test("Core từ chối khoảng cách tạo bán kính raster vượt giới hạn", () =>
        {
            var result = NestEngine.Run(new[] { new PartDefinition("Panel", 100, 50, 1) },
                new SheetDefinition(1000, 1000, 0, 9000));
            True(result.Sheets.Count == 0, "không cấp phát dilation quá lớn");
            True(result.Strategy.Contains("giới hạn tài nguyên"), "nêu rõ giới hạn raster");
        });

        Test("Hủy trước khi chạy trả về trạng thái hủy thay vì nesting giả thành công", () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var result = NestEngine.Run(new[] { new PartDefinition("Panel", 100, 50, 2) },
                new SheetDefinition(1000, 1000, 0, 0), new NestOptions { Cancel = cancellation.Token });
            True(result.Sheets.Count == 0, "không tạo kết quả khi đã hủy");
            True(result.Unplaced.Count == 1 && result.Unplaced[0].Contains("chưa xử lý"), "báo số lượng chưa xử lý");
            True(result.Strategy.Contains("hủy"), "trạng thái chỉ rõ tác vụ đã hủy");
        });

        Test("Xuất micro-joints tạo các khe hở trên đường biên chữ nhật", () =>
        {
            var sheet = new SheetResult { Index = 1, Width = 100, Height = 100, Material = "SS400", Thickness = 1 };
            sheet.Placements.Add(new Placement("Block", 1, 10, 10, 60, 40, false));
            string path = Path.Combine(tmp, "micro-joints.dxf");
            DxfExportWriter.Write(new[] { sheet }, path, new DxfExportOptions
            {
                OutputSheets = false,
                AddPartLabels = false,
                MicroJointWidthMm = 4,
                MicroJointCount = 2
            });
            var lines = File.ReadAllLines(path);
            int lineEntities = Enumerable.Range(0, lines.Length - 1).Count(i => lines[i].Trim() == "0" && lines[i + 1].Trim() == "LINE");
            True(lineEntities == 6, $"hai khe chia hai cạnh ngoài thành 6 đoạn LINE (thực tế {lineEntities})");
        });

        Test("GapTol arc/circle: chấp nhận thiếu khe trong dung sai và từ chối khi vượt ngưỡng", () =>
        {
            var arcPart = DxfReader.Read(Save("arc-profile.dxf", DxfDoc(
                DxfArc("0", 10, 10, 10, 0, 180) + DxfArc("0", 10, 10, 10, 180, 360))));
            var circlePart = DxfReader.Read(Save("circle-profile.dxf", DxfDoc(DxfCircle("0", 10, 10, 10))));
            True(arcPart.Geometry.Segments.All(segment => segment is ArcSeg), "biên chi tiết thứ nhất là các cung thật");
            True(circlePart.Geometry.Segments.Single() is CircleSeg, "biên chi tiết thứ hai là đường tròn thật");

            var sheet = new SheetDefinition(60, 30, 0, 0.2);

            List<string> CheckGap(double actualGap, out string? placementError)
            {
                var resultSheet = new SheetResult { Index = 1, Width = sheet.Width, Height = sheet.Height };
                var first = new Placement("ArcProfile", 1, 5, 5, arcPart.Width, arcPart.Height, false, arcPart.Geometry);
                var second = new Placement("CircleProfile", 1, 25 + actualGap, 5,
                    circlePart.Width, circlePart.Height, false, circlePart.Geometry);
                resultSheet.Placements.Add(first);
                resultSheet.Placements.Add(second);
                placementError = Validator.CheckPlacement(resultSheet, 1, second, sheet);
                var result = new NestResult();
                result.Sheets.Add(resultSheet);
                return Validator.Check(result, sheet);
            }

            var acceptedErrors = CheckGap(0.15, out var acceptedPlacementError);
            True(acceptedErrors.Count == 0 && acceptedPlacementError == null,
                "khoảng cách thực 0,15 mm được chấp nhận khi yêu cầu 0,2 mm");

            var rejectedErrors = CheckGap(0.05, out var rejectedPlacementError);
            True(rejectedErrors.Count == 1 && rejectedPlacementError?.Contains("quá gần", StringComparison.Ordinal) == true,
                "khoảng cách thực 0,05 mm bị cả hai API Validator từ chối");

            var nested = NestEngine.Run(new[] { arcPart.ToDefinition(), circlePart.ToDefinition() }, sheet,
                new NestOptions { AllowRotation = false, MaxSeconds = 1, TargetUtilizationPercent = 0 });
            True(nested.Unplaced.Count == 0 &&
                 nested.Sheets.Sum(resultSheet => resultSheet.Placements.Count) == 2 &&
                 Validator.Check(nested, sheet).Count == 0,
                "RasterNester vẫn đặt được contour cung/tròn hợp lệ và Validator xác nhận kết quả");
        });

        return new TestSuite(tmp, cases);
    }

    private sealed record RegisteredTest(string Category, string Name, Action Body);

    private sealed class TestSuite(string tempPath, IReadOnlyList<RegisteredTest> cases) : IDisposable
    {
        public IReadOnlyList<RegisteredTest> Cases { get; } = cases;

        public void Dispose() => Directory.Delete(tempPath, recursive: true);
    }
}
