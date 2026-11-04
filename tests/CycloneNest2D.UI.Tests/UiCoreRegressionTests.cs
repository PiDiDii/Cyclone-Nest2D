using System.Globalization;
using System.Text;
using System.Text.Json;
using CycloneNest2D.Core;
using CycloneNest2D.UI;
using Xunit;

namespace CycloneNest2D.UI.Tests;

public sealed class UiCoreRegressionTests
{
    [Fact]
    [Trait("Domain", "ProjectService")]
    public void ProjectService_CreatesStableProjectGroupNames()
    {
        var service = new ProjectService();
        var group = service.CreateProjectGroup("  Nhóm 1  ");

        Assert.Equal("Nhóm 1", group.Name);
        Assert.False(string.IsNullOrWhiteSpace(group.Id));
    }

    [Fact]
    [Trait("Domain", "ProjectLifecycle")]
    public void ProjectLifecycleService_BuildsUniqueGroupNameAndReferences()
    {
        var service = new ProjectLifecycleService();
        var groups = new[]
        {
            new ProjectGroupItem("g1", "Nhóm vật liệu", "C:/tmp/g1.nest2d")
        };

        Assert.Equal("Nhóm vật liệu 2", service.BuildUniqueGroupName(groups, "Nhóm vật liệu"));
        Assert.Equal("Nhóm vật liệu - bản sao", service.BuildDuplicateGroupName(groups, "Nhóm vật liệu"));
        Assert.Single(service.BuildProjectReferences(groups));
    }

    [Fact]
    [Trait("Domain", "ManualPlacement")]
    public void ManualPlacementService_RejectsAndMovesWithinBounds()
    {
        var service = new ManualPlacementService();
        var sheet = new SheetResult
        {
            Index = 1,
            Width = 100,
            Height = 100,
            Material = "SS400",
            Thickness = 2,
            StockName = "Tấm test"
        };
        sheet.Placements.Add(new Placement("A", 1, 10, 10, 20, 20, false));
        sheet.Placements.Add(new Placement("B", 1, 60, 10, 20, 20, false));
        var definition = new SheetDefinition(100, 100, 5, 3);

        Assert.NotNull(service.CheckMoveSelected(sheet, definition, new[] { 1 }, -50, 0));
        Assert.True(service.MoveSelectedUntilBlocked(sheet, definition, new[] { 1 }, 25, 0));
        Assert.True(sheet.Placements[1].X > 60);
    }

    [Fact]
    [Trait("Domain", "ManualPlacement")]
    public void ManualDrop_PersistsOutOfSheetAndOverlappingPlacementsAsInvalid()
    {
        var settings = new NestSettings { SheetWidth = 100, SheetHeight = 80, Margin = 5, Spacing = 3 };
        var preset = new SheetPreset("SS400", 100, 80, "SS400", 2);
        settings.SheetPresets.Clear();
        settings.SheetPresets.Add(preset);
        var vm = new MainViewModel(new FakeDialogs(), settings);
        var part = PartItem.NewRect("Manual", 20, 20, 3);
        part.AssignPreset(preset);
        vm.Parts.Add(part);

        Assert.True(vm.TryPlaceManually(part, -5, 30));
        Assert.NotNull(vm.Scene.Parts.Single().InvalidReason);
        Assert.True(vm.TryPlaceManually(part, 40, 30));
        Assert.True(vm.TryPlaceManually(part, 40, 30));
        Assert.Equal(3, vm.SelectedSheet!.Sheet.Placements.Count);
        Assert.All(vm.Scene.Parts, preview => Assert.NotNull(preview.InvalidReason));
    }

    [Fact]
    [Trait("Domain", "ManualPlacement")]
    public void DragMove_CommitsInvalidPositionAndMarksPlacementInvalid()
    {
        var settings = new NestSettings { SheetWidth = 100, SheetHeight = 80, Margin = 5, Spacing = 3 };
        var preset = new SheetPreset("SS400", 100, 80, "SS400", 2);
        settings.SheetPresets.Clear();
        settings.SheetPresets.Add(preset);
        var vm = new MainViewModel(new FakeDialogs(), settings);
        var part = PartItem.NewRect("Manual", 20, 20, 1);
        part.AssignPreset(preset);
        vm.Parts.Add(part);
        Assert.True(vm.TryPlaceManually(part, 40, 30));
        var sheetItem = vm.SelectedSheet!;
        int previewRevision = sheetItem.Revision;

        vm.SetSelectedPlacements(new[] { 0 });
        Assert.True(vm.MoveSelectedAllowInvalid(100, 0));
        Assert.NotNull(vm.Scene.Parts.Single().InvalidReason);
        Assert.Equal(previewRevision + 1, sheetItem.Revision);
    }

    [Fact]
    [Trait("Domain", "ManualPlacement")]
    public void RotateAndMirrorSelected_ApplyEvenWhenPlacementOverlaps()
    {
        var settings = new NestSettings { SheetWidth = 100, SheetHeight = 80, Margin = 5, Spacing = 3 };
        var preset = new SheetPreset("SS400", 100, 80, "SS400", 2);
        settings.SheetPresets.Clear();
        settings.SheetPresets.Add(preset);
        var vm = new MainViewModel(new FakeDialogs(), settings);
        var part = PartItem.NewRect("Manual", 30, 20, 2);
        part.AssignPreset(preset);
        vm.Parts.Add(part);

        Assert.True(vm.TryPlaceManually(part, 40, 30));
        Assert.True(vm.TryPlaceManually(part, 40, 30));
        vm.SetSelectedPlacements(new[] { 1 });

        vm.RotateSelectedAt(27, 23);
        Assert.Equal(10, vm.SelectedSheet!.Sheet.Placements[1].X);
        Assert.Equal(21, vm.SelectedSheet.Sheet.Placements[1].Y);
        Assert.Equal(20, vm.SelectedSheet!.Sheet.Placements[1].Width);
        Assert.All(vm.Scene.Parts, preview => Assert.NotNull(preview.InvalidReason));

        vm.MirrorSelectedAt(12, 25);
        Assert.Equal(-6, vm.SelectedSheet.Sheet.Placements[1].X);
        Assert.Contains("Đã lật", vm.Status);
        Assert.NotNull(vm.Scene.Parts.Single(preview => preview.Index == 1).InvalidReason);
    }

    [Fact]
    [Trait("Domain", "ExportValidation")]
    public void InvalidSheetBlocksDxfExportAndExposesValidationErrors()
    {
        var settings = new NestSettings { SheetWidth = 100, SheetHeight = 80, Margin = 5, Spacing = 3 };
        var preset = new SheetPreset("SS400", 100, 80, "SS400", 2);
        settings.SheetPresets.Clear();
        settings.SheetPresets.Add(preset);
        var dialogs = new FakeDialogs();
        string destination = Path.Combine(Path.GetTempPath(), $"invalid-nest-{Guid.NewGuid():N}.dxf");
        dialogs.SaveFile = destination;
        var vm = new MainViewModel(dialogs, settings);
        var part = PartItem.NewRect("Manual", 20, 20, 2);
        part.AssignPreset(preset);
        vm.Parts.Add(part);

        Assert.True(vm.TryPlaceManually(part, -5, 30));
        Assert.True(vm.AddManualSheet());
        Assert.True(vm.TryPlaceManually(part, 40, 30));
        Assert.NotEmpty(vm.GetExportValidationErrors());

        vm.ExportDxf(new DxfExportOptions { FileLayout = DxfFileLayout.SingleFile });

        Assert.Empty(dialogs.Confirmations);
        Assert.False(File.Exists(destination));
        Assert.Contains("Không thể xuất DXF", vm.Status);
    }

    [Fact]
    [Trait("Domain", "NestingPreflight")]
    public void NestingOrchestrator_FlagsMissingMaterialAndMissingSheet()
    {
        var settings = new NestSettings { SheetWidth = 1000, SheetHeight = 600, Margin = 5 };
        var validSheets = new[]
        {
            new SheetPreset("Tấm 1", 1000, 600, "SS400", 2)
        };

        var missingMaterial = PartItem.NewRect("Phần không vật liệu", 50, 40, 1);
        missingMaterial.Material = "";
        missingMaterial.Thickness = 2;

        var validPart = PartItem.NewRect("Phần hợp lệ", 40, 30, 1);
        validPart.Material = "SS400";
        validPart.Thickness = 2;

        var issues = new NestingOrchestrator().BuildPreflightIssues(new[] { missingMaterial, validPart }, settings, validSheets);

        Assert.Contains(issues, issue => issue.Contains("Phần không vật liệu", StringComparison.Ordinal));
        Assert.DoesNotContain(issues, issue => issue.Contains("Phần hợp lệ", StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> PartManagementCases() => DiscoverCases("PartManagement");
    public static IEnumerable<object[]> NestingPreviewCases() => DiscoverCases("NestingPreview");
    public static IEnumerable<object[]> ManualPlacementCases() => DiscoverCases("ManualPlacement");
    public static IEnumerable<object[]> PlacementValidationCases() => DiscoverCases("PlacementValidation");
    public static IEnumerable<object[]> ProjectStoreAndExportCases() => DiscoverCases("ProjectStoreAndExport");
    public static IEnumerable<object[]> UiCommandsCases() => DiscoverCases("UiCommands");
    public static IEnumerable<object[]> XamlRegressionCases() => DiscoverCases("XamlRegression");
    public static IEnumerable<object[]> PreviewSceneCases() => DiscoverCases("PreviewScene");

    [Theory]
    [MemberData(nameof(PartManagementCases))]
    [Trait("Domain", "PartManagement")]
    public void PartManagement(string name, int index) => ExecuteCase(name, index);

    [Theory]
    [MemberData(nameof(NestingPreviewCases))]
    [Trait("Domain", "NestingPreview")]
    public void NestingPreview(string name, int index) => ExecuteCase(name, index);

    [Theory]
    [MemberData(nameof(ManualPlacementCases))]
    [Trait("Domain", "ManualPlacement")]
    public void ManualPlacement(string name, int index) => ExecuteCase(name, index);

    [Theory]
    [MemberData(nameof(PlacementValidationCases))]
    [Trait("Domain", "PlacementValidation")]
    public void PlacementValidation(string name, int index) => ExecuteCase(name, index);

    [Theory]
    [MemberData(nameof(ProjectStoreAndExportCases))]
    [Trait("Domain", "ProjectStore")]
    public void ProjectStoreAndExport(string name, int index) => ExecuteCase(name, index);

    [Theory]
    [MemberData(nameof(UiCommandsCases))]
    [Trait("Domain", "UiCommands")]
    public void UiCommands(string name, int index) => ExecuteCase(name, index);

    [Theory]
    [MemberData(nameof(XamlRegressionCases))]
    [Trait("Domain", "XamlRegression")]
    public void XamlRegression(string name, int index) => ExecuteCase(name, index);

    [Theory]
    [MemberData(nameof(PreviewSceneCases))]
    [Trait("Domain", "PreviewScene")]
    public void PreviewSceneRendering(string name, int index) => ExecuteCase(name, index);

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
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        Console.OutputEncoding = Encoding.UTF8;

        var cases = new List<RegisteredTest>();
        string tmp = Path.Combine(Path.GetTempPath(), "n2d_ui_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);

        void Test(string name, Action body)
        {
            // Keep these ranges aligned with the contiguous domain blocks below.
            int index = cases.Count;
            string category = index switch
            {
                < 11 => "PartManagement",
                < 22 => "NestingPreview",
                < 25 => "ManualPlacement",
                < 29 => "PlacementValidation",
                < 40 => "ProjectStoreAndExport",
                < 44 => "UiCommands",
                < 48 => "XamlRegression",
                _ => "PreviewScene"
            };
            cases.Add(new RegisteredTest(category, name, body));
        }
        void Check(bool cond, string msg) => Assert.True(cond, msg);

        // ---------- DXF mẫu ----------
        string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        string Line(string layer, double x1, double y1, double x2, double y2) =>
            $"0\nLINE\n8\n{layer}\n10\n{F(x1)}\n20\n{F(y1)}\n30\n0\n11\n{F(x2)}\n21\n{F(y2)}\n31\n0\n";
        string Circle(string layer, double cx, double cy, double r) =>
            $"0\nCIRCLE\n8\n{layer}\n10\n{F(cx)}\n20\n{F(cy)}\n30\n0\n40\n{F(r)}\n";
        string Rect(string layer, double x, double y, double w, double h) =>
            Line(layer, x, y, x + w, y) + Line(layer, x + w, y, x + w, y + h) + Line(layer, x + w, y + h, x, y + h) + Line(layer, x, y + h, x, y);
        void WriteDxf(string path, string entities)
        {
            File.WriteAllText(path, "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n" + entities + "0\nENDSEC\n0\nEOF\n");
        }

        string dxfDir = Path.Combine(tmp, "dxf");
        Directory.CreateDirectory(dxfDir);
        // Vòng đệm 200x200 lỗ tròn R70 (lỗ to để chi tiết nhỏ lọt vào), kèm layer BEND và DIM
        WriteDxf(Path.Combine(dxfDir, "Ring_x2.dxf"), Rect("CUT", 0, 0, 200, 200) + Circle("HOLES", 100, 100, 70) + Line("BEND", 0, 100, 200, 100));
        WriteDxf(Path.Combine(dxfDir, "Plate_x4.dxf"), Rect("0", 0, 0, 120, 60) + Circle("0", 30, 30, 10));
        WriteDxf(Path.Combine(dxfDir, "Small_x3_norot.dxf"), Rect("0", 0, 0, 80, 40));
        File.WriteAllText(Path.Combine(dxfDir, "readme.txt"), "x");

        // ---------- hộp thoại giả ----------
        var dlg = new FakeDialogs();
        MainViewModel NewVm(NestSettings? s = null)
        {
            dlg = new FakeDialogs();
            var vm = new MainViewModel(dlg, s ?? new NestSettings { SheetWidth = 1000, SheetHeight = 600, MaxSeconds = 2 });
            return vm;
        }

        Test("Thư viện mẫu tạo hình thật và cho chỉnh kích thước", () =>
        {
            var circle = PartItem.NewTemplate("Circle", StandardPartShape.Circle, 80, 80);
            Check(circle.Geometry?.Segments.Single() is CircleSeg, "mẫu tròn dùng đường tròn thật");
            var triangle = PartItem.NewTemplate("Triangle", StandardPartShape.Triangle, 120, 90);
            Check(triangle.Geometry?.Segments.Count == 3 && triangle.Width == 120 && triangle.Height == 90,
                "mẫu tam giác có ba cạnh và đúng kích thước");
            var polygon = PartItem.NewTemplate("Polygon", StandardPartShape.Polygon, 60, 60, 5);
            Check(polygon.Geometry?.Segments.Count == 5 && polygon.KindText.Contains("5 cạnh", StringComparison.Ordinal),
                "đa giác đều có số cạnh đã chọn");
            Check(new[] { circle, triangle, polygon }.All(part =>
                      part.Geometry != null && ShapeOps.Build(part.Geometry).Reliable && part.ToDefinition().Shape != null),
                "hình học mẫu khép kín, hợp lệ để nesting");
            polygon.ResizeTemplate(100, 100);
            Check(polygon.TemplateSize == 100 && polygon.Width == polygon.Geometry?.Width &&
                  polygon.Height == polygon.Geometry?.Height,
                "kích thước đa giác được cập nhật theo hình học");
            var vm = NewVm();
            Check(vm.AddTemplate(StandardPartShape.Circle, 35, 35).Kind == PartKind.Template &&
                  vm.SelectedPart?.Width == 35, "mẫu mới được thêm và chọn trong danh sách");
        });

        Console.WriteLine("Danh sách chi tiết");
        Test("Thêm thư mục: chỉ lấy .dxf, tên theo file, số lượng theo _xN", () =>
        {
            var vm = NewVm();
            int n = vm.AddPaths(new[] { dxfDir });
            Check(n == 3 && vm.Parts.Count == 3, $"thêm {n}");
            Check(vm.Parts.Any(p => p.Name == "Ring" && p.Quantity == 2), "Ring x2");
            Check(vm.Parts.Single(p => p.Name == "Small").CanRotate == false, "norot");
            Check(Math.Abs(vm.Parts.Single(p => p.Name == "Plate").Width - 120) < 0.01, "kích thước 120");
        });
        Test("Thêm trùng file bị bỏ qua và có thông báo", () =>
        {
            var vm = NewVm();
            vm.AddPaths(new[] { dxfDir });
            int n = vm.AddPaths(new[] { Path.Combine(dxfDir, "Ring_x2.dxf") });
            Check(n == 0 && vm.Parts.Count == 3, "không thêm trùng");
            Check(vm.Messages.Any(m => m.Contains("đã có")), "có thông báo");
        });
        Test("File rác/không có DXF: không ném lỗi", () =>
        {
            var vm = NewVm();
            string bad = Path.Combine(tmp, "bad.dxf");
            File.WriteAllText(bad, "khong phai dxf");
            int n = vm.AddPaths(new[] { bad, Path.Combine(dxfDir, "readme.txt"), Path.Combine(tmp, "khongco.dxf") });
            Check(n == 0 && vm.Parts.Count == 0, "không thêm gì");
            Check(vm.Messages.Count >= 1, "có thông báo lỗi");
        });
        Test("Tên được làm duy nhất khi trùng", () =>
        {
            var vm = NewVm();
            vm.AddRect(); vm.AddRect(); vm.AddRect();
            Check(vm.Parts.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3, "3 tên khác nhau");
        });
        Test("Sidebar nhóm Part theo vật liệu/độ dày và tổng số lượng cập nhật", () =>
        {
            var vm = NewVm();
            vm.AddRect(); vm.Parts[0].Material = "SS400"; vm.Parts[0].Thickness = 1; vm.Parts[0].Quantity = 2;
            vm.AddRect(); vm.Parts[1].Material = "SS400"; vm.Parts[1].Thickness = 1; vm.Parts[1].Quantity = 3;
            vm.AddRect(); vm.Parts[2].Material = "SUS304"; vm.Parts[2].Thickness = 1.5; vm.Parts[2].Quantity = 4;
            Check(vm.PartGroups.Count == 2, "cùng vật liệu và độ dày phải nằm cùng nhóm");
            var ss = vm.PartGroups.Single(g => g.Material == "SS400");
            Check(ss.UniqueCount == 2 && ss.TotalQuantity == 5, "số part duy nhất và số lượng nhóm SS400");
            Check(vm.RequiredCount == 9 && vm.PlacedCount == 0 && vm.RemainingCount == 9, "summary trước khi nest");
            vm.Parts[1].Quantity = 6;
            Check(vm.PartGroups.Single(g => g.Material == "SS400").TotalQuantity == 8 && vm.RequiredCount == 12 && vm.RemainingCount == 12, "summary cập nhật khi sửa số lượng");
        });
        Test("Gán profile khổ tấm cập nhật vật liệu/độ dày cho nhiều Part", () =>
        {
            var vm = NewVm();
            vm.AddRect(); vm.AddRect(); vm.AddRect();
            var profile = new SheetPreset("Nhôm 2mm 1250x2500", 2500, 1250, "Nhôm A6061", 2);
            vm.Settings.SheetPresets.Add(profile);
            int count = vm.AssignPresetToParts(vm.Parts.Take(2), profile);
            Check(count == 2, "đã gán hai dòng được chọn");
            Check(vm.Parts.Take(2).All(p => p.Material == "Nhôm A6061" && p.Thickness == 2 && p.AssignedPreset == profile), "metadata lấy từ profile");
            Check(vm.Parts[2].Material == "" && vm.Parts[2].Thickness == 0, "không ảnh hưởng dòng không được chọn");
        });
        Test("Thêm, sửa và xóa khổ tấm giữ liên kết Part và cấu hình nesting đồng bộ", () =>
        {
            var vm = NewVm();
            vm.Settings.SheetPresets.Clear();
            var original = new SheetPreset("SS400 1mm", 2000, 1000, "SS400", 1);
            vm.AddSheetPreset(original);
            vm.AddRect();
            vm.AssignPresetToPart(vm.Parts[0], original);

            var edited = new SheetPreset("SUS304 2mm", 3000, 1500, "SUS304", 2) { Note = "Tấm cán nguội" };
            vm.EditSheetPreset(original, edited);
            Check(vm.Presets.Count == 1 && ReferenceEquals(vm.Presets[0], edited), "sửa dòng thay thế đúng profile cũ");
            Check(ReferenceEquals(vm.Parts[0].AssignedPreset, edited) && vm.Parts[0].Material == "SUS304" && vm.Parts[0].Thickness == 2, "Part được cập nhật theo profile mới");
            Check(ReferenceEquals(vm.SelectedPreset, edited) && vm.Settings.SheetWidth == 3000 && vm.Settings.SheetHeight == 1500, "profile đang chọn cập nhật kích thước dùng chung");
            Check(edited.Note == "Tấm cán nguội", "ghi chú profile được giữ khi sửa khổ tấm");

            vm.DeleteSheetPreset(edited);
            Check(vm.Presets.Count == 0 && vm.Parts[0].AssignedPreset == null && vm.SelectedPreset == null, "xóa profile gỡ liên kết Part và selection");
        });

        Test("Xóa nhiều Part theo selection cập nhật danh sách và số thứ tự", () =>
        {
            var vm = NewVm();
            vm.AddRect(); vm.AddRect(); vm.AddRect();
            var removed = vm.RemoveParts(new[] { vm.Parts[0], vm.Parts[2] });
            Check(removed == 2 && vm.Parts.Count == 1, "xóa đúng các Part được chọn");
            Check(vm.Parts[0].Sequence == 1, "đánh lại STT sau xóa");
        });

        Test("Khoảng cách giữa part và lề mép là hai thông số độc lập, luôn dùng khi nesting", () =>
        {
            var settings = new NestSettings { Spacing = 4.5, Margin = 7.25 };
            var sheet = settings.ToSheet();
            Check(Math.Abs(sheet.Spacing - 4.5) < 1e-9, "gap giữa part giữ chính xác");
            Check(Math.Abs(sheet.Margin - 7.25) < 1e-9, "lề mép giữ chính xác");
            settings.Spacing = 0;
            Check(settings.ToSheet().Spacing == 0 && settings.ToSheet().Margin == 7.25, "gap bằng 0 không làm thay đổi lề");
        });
        Test("Chọn/bỏ layer đọc lại hình học; bỏ hết layer thì lỗi và bị bỏ qua khi chạy", () =>
        {
            var vm = NewVm();
            vm.AddPaths(new[] { Path.Combine(dxfDir, "Ring_x2.dxf") });
            var ring = vm.Parts[0];
            Check(ring.HasLayers, "có nhiều layer");
            Check(ring.Layers.Single(l => l.Name == "BEND").IsChecked == false, "BEND mặc định tắt");
            Check(ring.Layers.Single(l => l.Name == "HOLES").IsChecked, "HOLES mặc định bật");
            int segs = ring.Geometry!.Segments.Count;
            ring.Layers.Single(l => l.Name == "HOLES").IsChecked = false;
            Check(ring.Geometry!.Segments.Count == segs - 1, "bỏ layer HOLES mất 1 hình tròn");
            ring.Layers.Single(l => l.Name == "BEND").IsChecked = true;
            Check(ring.Geometry!.Segments.Count == segs - 1 + 1, "bật BEND thêm 1 đường");
            foreach (var l in ring.Layers) l.IsChecked = false;
            Check(!ring.IsValid && ring.Error != null, "không còn layer nào -> lỗi");
            foreach (var l in ring.Layers.Where(l => l.Name != "BEND")) l.IsChecked = true;
            Check(ring.IsValid, "bật lại -> hợp lệ");
        });

        Console.WriteLine("Nesting và xem trước");
        Test("Chạy nesting: có kết quả, không lỗi kiểm tra, danh sách tấm và cảnh xem trước", () =>
        {
            var vm = NewVm();
            vm.AddPaths(new[] { dxfDir });
            PrepareProfiles(vm);
            vm.RunCommand.Execute(null);
            WaitIdle(vm);
            Check(vm.Result != null, "có kết quả");
            Check(vm.Result!.Unplaced.Count == 0, "đặt hết");
            Check(vm.Messages.All(m => !m.StartsWith("KIỂM TRA LỖI")), "validator sạch");
            Check(vm.Sheets.Count >= 1 && vm.SelectedSheet != null, "có tấm được chọn");
            int total = vm.Result.Sheets.Sum(s => s.Placements.Count);
            Check(total == 2 + 4 + 3, $"tổng chi tiết {total}");
            Check(vm.Scene.Parts.Count == vm.SelectedSheet!.Sheet.Placements.Count, "scene khớp tấm");
            Check(!vm.IsStale && !vm.IsBusy, "không stale, không busy");
            Check(vm.ProgressPercent == 100 && vm.ElapsedNestTimeText.Length > 0,
                "kết thúc nesting cập nhật tiến độ và thời gian");
        });
        Test("Hit-test: trúng vật liệu thì ra chi tiết, lỗ trống thì không", () =>
        {
            var vm = NewVm(new NestSettings { SheetWidth = 600, SheetHeight = 400, MaxSeconds = 1 });
            vm.AddPaths(new[] { Path.Combine(dxfDir, "Ring_x2.dxf") });
            vm.Parts[0].Quantity = 1;
            PrepareProfiles(vm);
            vm.RunCommand.Execute(null); WaitIdle(vm);
            var pp = vm.Scene.Parts.Single();
            Check(pp.HasShape, "dùng hình thật");
            Check(vm.Scene.HitTest(pp.X + 5, pp.Y + 100) == pp.Index, "chạm viền");
            Check(vm.Scene.HitTest(pp.X + 100, pp.Y + 100) == -1, "giữa lỗ: không trúng");
            Check(vm.Scene.HitTest(pp.X - 50, pp.Y - 50) == -1, "ngoài chi tiết");
        });
        Test("Dừng giữa chừng: giữ kết quả tốt nhất có sẵn, không treo", () =>
        {
            var vm = NewVm(new NestSettings { SheetWidth = 2500, SheetHeight = 1250, MaxSeconds = 60 });
            for (int i = 0; i < 30; i++) { vm.AddRect(); vm.Parts[^1].Width = 100 + i * 7; vm.Parts[^1].Height = 80 + i * 3; vm.Parts[^1].Quantity = 4; }
            PrepareProfiles(vm);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            vm.RunCommand.Execute(null);
            Thread.Sleep(600);
            Check(vm.IsBusy, "đang chạy");
            vm.CancelCommand.Execute(null);
            WaitIdle(vm, 15000);
            Check(sw.Elapsed.TotalSeconds < 10, $"dừng đủ nhanh ({sw.Elapsed.TotalSeconds:0.0}s)");
            Check(vm.Result != null && vm.Result.Sheets.Count > 0, "vẫn giữ được kết quả tốt nhất đã có");
            Check(vm.Result!.Sheets.Sum(s => s.Placements.Count) <= 30 * 4, "không vượt số part khi dừng");
        });
        Test("Kết quả cũ được đánh dấu stale khi sửa chi tiết hoặc thông số", () =>
        {
            var vm = QuickRectVm();
            Check(!vm.IsStale, "mới chạy xong");
            vm.Parts[0].Quantity = 5;
            Check(vm.IsStale, "đổi số lượng -> stale");
            vm.RunCommand.Execute(null); WaitIdle(vm);
            Check(!vm.IsStale, "chạy lại -> hết stale");
            vm.Settings.Spacing = 7;
            Check(vm.IsStale, "đổi thông số -> stale");
        });
        Test("Thông số không hợp lệ: không chạy, có hộp thoại", () =>
        {
            var vm = QuickRectVm();
            vm.Settings.Margin = 900;
            vm.RunCommand.Execute(null); WaitIdle(vm);
            Check(dlg.Messages.Count == 1, "có báo lỗi");
        });
        Test("Tổng số lượng vượt giới hạn bị từ chối trước preflight", () =>
        {
            var vm = NewVm();
            vm.AddRect();
            vm.Parts[0].Quantity = NestEngine.MaxPartInstances + 1;
            vm.RunCommand.Execute(null); WaitIdle(vm);
            Check(vm.Result == null, "không chạy nesting");
            Check(dlg.Messages.Any(message => message.Contains("250,000")), "nêu rõ giới hạn số lượng");
        });
        Test("Không có chi tiết: nút Nesting bị khóa", () =>
        {
            var vm = NewVm();
            Check(!vm.RunCommand.CanExecute(null), "khóa");
        });
        Test("Preflight chặn Part chưa được gán vật liệu/độ dày từ khổ tấm", () =>
        {
            var vm = NewVm();
            vm.AddRect();
            vm.Parts[0].Thickness = 1;
            vm.Settings.SheetPresets.Clear();
            vm.Settings.SheetPresets.Add(new SheetPreset("SUS304 tấm", 1000, 600, "SUS304", 1));
            vm.RunCommand.Execute(null); WaitIdle(vm);
            Check(vm.Result == null && dlg.Messages.Any(m => m.Contains("hãy chọn vật liệu/độ dày")), "báo lỗi vật liệu trước khi nesting");
        });
        Test("Preflight chặn contour DXF hở và báo người dùng trước khi Nesting", () =>
        {
            string openFile = Path.Combine(tmp, "OpenContour_x2.dxf");
            WriteDxf(openFile, Line("0", 0, 0, 100, 0) + Line("0", 100, 0, 100, 60) + Line("0", 100, 60, 0, 60) + Line("0", 0, 55, 0, 0));
            var vm = NewVm();
            vm.AddPaths(new[] { openFile });
            PrepareProfiles(vm);
            vm.RunCommand.Execute(null); WaitIdle(vm);
            Check(vm.Result == null, "không tạo kết quả nesting với contour lỗi");
            Check(dlg.Messages.Any(m => m.Contains("contour DXF lỗi", StringComparison.OrdinalIgnoreCase)), "dialog nêu rõ lỗi contour");
        });
        Test("Giới hạn thời gian áp dụng cho toàn bộ tác vụ nhiều tấm", () =>
        {
            var vm = NewVm(new NestSettings { SheetWidth = 400, SheetHeight = 300, Margin = 5, Spacing = 3, MaxSeconds = 0.5, TargetUtilizationPercent = 0 });
            vm.AddRect();
            vm.Parts[0].Width = 50; vm.Parts[0].Height = 50; vm.Parts[0].Quantity = 10000;
            PrepareProfiles(vm);
            vm.RunCommand.Execute(null); WaitIdle(vm, 5000);
            Check(vm.Result != null && vm.Status.Contains("giới hạn thời gian", StringComparison.OrdinalIgnoreCase), "hết thời gian phải báo trạng thái và giữ kết quả tốt nhất");
            Check(vm.LastNestDuration.TotalSeconds < 3, $"không được kéo dài vô hạn ({vm.LastNestDuration.TotalSeconds:0.0}s)");
        });

        Test("Nesting nhiều vật liệu/độ dày tách nhóm và không giới hạn số tấm", () =>
        {
            var vm = NewVm(new NestSettings { SheetWidth = 1000, SheetHeight = 600, MaxSeconds = 1 });
            vm.AddRect();
            vm.Parts[0].Width = 150; vm.Parts[0].Height = 150; vm.Parts[0].Quantity = 2;
            vm.AddRect();
            vm.Parts[1].Width = 150; vm.Parts[1].Height = 150; vm.Parts[1].Quantity = 2;
            vm.Settings.SheetPresets.Clear();
            var sus = new SheetPreset("SUS304 nhỏ", 200, 200, "SUS304", 1);
            var ss = new SheetPreset("SS400 nhỏ", 200, 200, "SS400", 2);
            vm.Settings.SheetPresets.Add(sus); vm.Settings.SheetPresets.Add(ss);
            vm.AssignPresetToPart(vm.Parts[0], sus);
            vm.AssignPresetToPart(vm.Parts[1], ss);
            vm.RunCommand.Execute(null); WaitIdle(vm);
            Check(vm.Result != null && vm.Result.Sheets.Count == 4, "4 tấm được tạo dù không khai báo tồn kho");
            Check(vm.Result!.Sheets.Select(s => (s.Material, s.Thickness)).Distinct().Count() == 2, "tách theo vật liệu và độ dày");
            Check(vm.Result.Sheets.All(s => s.Placements.Count == 1), "mỗi sheet 200x200 chứa một part");
            Check(vm.Result.Unplaced.Count == 0 && vm.Result.Sheets.Sum(s => s.Placements.Count) == 4, "xếp đủ mọi part không bị giới hạn bởi tồn kho");
        });

        Console.WriteLine("Xếp thủ công");
        Test("Thả part tạo sheet thủ công, căn tâm và mũi tên đưa part đến sát lề hợp lệ", () =>
        {
            var settings = new NestSettings { SheetWidth = 200, SheetHeight = 100, Margin = 5, Spacing = 3 };
            var vm = NewVm(settings);
            var preset = new SheetPreset("SS400 200x100", 200, 100, "SS400", 1);
            vm.Settings.SheetPresets.Clear(); vm.Settings.SheetPresets.Add(preset); vm.SelectedPreset = preset;
            var part = PartItem.NewRect("Manual", 50, 30, 1);
            part.AssignPreset(preset); vm.Parts.Add(part);
            Check(vm.TryPlaceManually(part, 40, 30), "thả thủ công phải thành công");
            Check(vm.Result != null && vm.Result.Sheets.Count == 1 && vm.PlacedCount == 1, "tạo một nest và đặt một part");
            var placed = vm.SelectedSheet!.Sheet.Placements[0];
            Check(Math.Abs(placed.X - 15) < 1e-6 && Math.Abs(placed.Y - 15) < 1e-6, "tâm part khớp điểm thả");
            vm.SetSelectedPlacements(new[] { 0 });
            Check(vm.MoveSelectedUntilBlocked(1, 0), "mũi tên phải dịch part tới vật cản/mép tấm");
            Check(Math.Abs(vm.SelectedSheet.Sheet.Placements[0].X - 145) < 0.01, "dừng sát mép phải, giữ lề 5 mm");
            Check(Validator.Check(vm.Result!, vm.ResultSheet!).Count == 0, "placement hợp lệ sau thao tác");
        });
        Test("Số lượng còn lại, thêm tấm thủ công, chọn nhóm và xóa placement đồng bộ", () =>
        {
            var settings = new NestSettings { SheetWidth = 200, SheetHeight = 100, Margin = 5, Spacing = 2 };
            var vm = NewVm(settings);
            var preset = new SheetPreset("SS400 200x100", 200, 100, "SS400", 1);
            vm.Settings.SheetPresets.Clear(); vm.Settings.SheetPresets.Add(preset); vm.SelectedPreset = preset;
            var part = PartItem.NewRect("Group", 30, 20, 3);
            part.AssignPreset(preset); vm.Parts.Add(part);
            Check(vm.TryPlaceManually(part, 30, 30), "first manual placement");
            Check(part.RemainingQuantity == 2 && vm.PartGroups.Sum(g => g.TotalQuantity) == 2, "filter count decreases immediately");
            Check(vm.AddManualSheet() && vm.Sheets.Count == 2 && vm.SelectedSheet!.Sheet.Placements.Count == 0, "add a second empty sheet");
            Check(vm.TryPlaceManually(part, 30, 30), "placement on second sheet");
            Check(part.RemainingQuantity == 1, "remaining count across all sheets");
            vm.SetSelectedPlacements(new[] { 0 });
            Check(vm.TryMoveSelected(10, 0), "move selected group by a valid delta");
            Check(vm.DeletePlacementCommand.CanExecute(null), "delete command enabled for selected placement");
            vm.DeletePlacementCommand.Execute(null);
            Check(vm.PlacedCount == 1 && part.RemainingQuantity == 2, "delete command restores quantity");
            vm.ClearNesting();
            Check(vm.Result == null && vm.Sheets.Count == 0 && part.RemainingQuantity == 3, "clear all nesting but keep the source Part");
        });

        Test("Xóa nesting chỉ xóa tấm đang chọn, không phụ thuộc part được chọn", () =>
        {
            var settings = new NestSettings { SheetWidth = 200, SheetHeight = 100, Margin = 5, Spacing = 0 };
            var vm = NewVm(settings);
            var preset = new SheetPreset("SS400 200x100", 200, 100, "SS400", 1);
            vm.Settings.SheetPresets.Clear(); vm.Settings.SheetPresets.Add(preset); vm.SelectedPreset = preset;
            var part = PartItem.NewRect("Panel", 30, 20, 2);
            part.AssignPreset(preset); vm.Parts.Add(part);
            Check(vm.TryPlaceManually(part, 30, 30), "place one part on first sheet");
            Check(vm.AddManualSheet() && vm.TryPlaceManually(part, 30, 30), "place another part on second sheet");

            vm.SelectedSheet = vm.Sheets[0];
            Check(vm.SelectedPlacement == -1 && vm.DeleteSelectedSheetCommand.CanExecute(null),
                "delete selected sheet works without selecting a placement");
            vm.DeleteSelectedSheetCommand.Execute(null);
            Check(vm.Sheets.Count == 1 && vm.SelectedSheet!.Sheet.Index == 1, "selected sheet removed and remaining sheet renumbered");
            Check(vm.PlacedCount == 1 && part.RemainingQuantity == 1, "part quantity recalculated from remaining nests");
            vm.DeleteSelectedSheet();
            Check(vm.Result == null && vm.Sheets.Count == 0 && part.RemainingQuantity == 2,
                "deleting final nest restores all source quantities");
        });

        Console.WriteLine("Chỉnh tay");
        Test("Dời hợp lệ thì áp dụng, chồng lên chi tiết khác thì từ chối và giữ nguyên", () =>
        {
            var vm = QuickRectVm();
            var s = vm.SelectedSheet!.Sheet;
            Check(s.Placements.Count >= 2, "cần ≥2 chi tiết");
            var a = s.Placements[0]; var b = s.Placements[1];
            string? err = vm.TryMove(0, b.X, b.Y);
            Check(err != null, "chồng lên phải bị từ chối");
            Check(s.Placements[0].X == a.X && s.Placements[0].Y == a.Y, "giữ nguyên");
            err = vm.TryMove(0, -50, 0);
            Check(err != null, "ra ngoài lề phải bị từ chối");
            // dời về góc trống trên cùng bên phải
            double fx = vm.ResultSheet!.Width - vm.ResultSheet.Margin - a.Width;
            double fy = vm.ResultSheet.Height - vm.ResultSheet.Margin - a.Height;
            err = vm.TryMove(0, fx, fy);
            Check(err == null, "dời vào chỗ trống: " + err);
            Check(Math.Abs(s.Placements[0].X - fx) < 1e-9 && vm.IsEdited, "đã áp dụng và đánh dấu đã chỉnh");
            Check(Validator.Check(vm.Result!, vm.ResultSheet).Count == 0, "validator vẫn sạch");
        });
        Test("Xoay 90° chi tiết đã chọn: kích thước hoán đổi, tâm giữ nguyên, vẫn hợp lệ", () =>
        {
            var vm = QuickRectVm();
            var s = vm.SelectedSheet!.Sheet;
            // chọn chi tiết có chỗ xoay: dời về góc trống rồi xoay
            var a = s.Placements[0];
            double fx = vm.ResultSheet!.Width - vm.ResultSheet.Margin - a.Width;
            double fy = vm.ResultSheet.Height - vm.ResultSheet.Margin - a.Height;
            Check(vm.TryMove(0, fx, fy) == null, "dời được");
            vm.SelectedPlacement = 0;
            Check(vm.RotateSelectedCommand.CanExecute(null), "có thể xoay khi đã chọn");
            var before = s.Placements[0];
            // Xoay quanh tâm giữ nguyên tâm, kể cả khi kết quả vượt lề.
            vm.RotateSelected();
            var after = s.Placements[0];
            Check(Math.Abs(after.Width - before.Height) < 1e-9, "xoay được dù kết quả vượt lề");
            Check(Math.Abs((after.X + after.Width / 2) - (before.X + before.Width / 2)) < 1e-9, "tâm X");
            Check(Math.Abs((after.Y + after.Height / 2) - (before.Y + before.Height / 2)) < 1e-9, "tâm Y");
        });
        Test("PlacementOps.Rotated90 bốn lần trở về hình ban đầu (chi tiết DXF)", () =>
        {
            var part = DxfReader.Read(Path.Combine(dxfDir, "Plate_x4.dxf"));
            var p = new Placement("Plate", 1, 100, 100, part.Width, part.Height, false, part.Geometry);
            var q = p;
            for (int i = 0; i < 4; i++) q = PlacementOps.Rotated90(q);
            Check(Math.Abs(q.X - p.X) < 1e-6 && Math.Abs(q.Y - p.Y) < 1e-6 && Math.Abs(q.Width - p.Width) < 1e-6, "vị trí/kích thước");
            Check(Math.Abs(Mat.Norm360(q.AngleDeg)) < 1e-6 || Math.Abs(Mat.Norm360(q.AngleDeg) - 360) < 1e-6, "góc về 0");
            var one = PlacementOps.Rotated90(p);
            Check(Math.Abs(one.Width - p.Height) < 1e-6 && Math.Abs(one.Height - p.Width) < 1e-6, "hoán đổi sau 1 lần");
        });
        Test("Validator.CheckPlacement khớp với Validator.Check trên kết quả hình thật", () =>
        {
            var vm = NewVm(new NestSettings { SheetWidth = 1000, SheetHeight = 600, MaxSeconds = 2 });
            vm.AddPaths(new[] { dxfDir });
            PrepareProfiles(vm);
            vm.RunCommand.Execute(null); WaitIdle(vm);
            foreach (var sh in vm.Result!.Sheets)
                for (int i = 0; i < sh.Placements.Count; i++)
                {
                    var err = Validator.CheckPlacement(sh, i, sh.Placements[i], vm.ResultSheet!);
                    Check(err == null, $"tấm {sh.Index} chi tiết {i}: {err}");
                }
        });

        Console.WriteLine("Xuất và dự án");
        Test("Xuất DXF mỗi tấm và đọc lại được", () =>
        {
            var vm = QuickRectVm();
            string outDir = Path.Combine(tmp, "out");
            dlg.Folder = outDir;
            vm.ExportDxfCommand.Execute(null);
            Check(Directory.GetFiles(outDir, "*.dxf").Length == vm.Result!.Sheets.Count, "số DXF");
            // đọc lại DXF đã xuất bằng chính DxfReader
            var back = DxfReader.Read(Directory.GetFiles(outDir, "*.dxf").OrderBy(x => x).First(), new DxfReadOptions { IncludeLayers = new HashSet<string> { "CUT", "PARTS" } });
            Check(back.Width > 0, "đọc lại được");
        });
        Test("Lưu/mở dự án: đường dẫn tương đối, layer, số lượng, thông số", () =>
        {
            var vm = NewVm();
            vm.AddPaths(new[] { dxfDir });
            vm.AddRect(); vm.Parts[^1].Width = 321; vm.Parts[^1].Height = 123; vm.Parts[^1].Quantity = 9;
            vm.Parts[^1].Material = "SS400"; vm.Parts[^1].Thickness = 2.5;
            vm.AddTemplate(StandardPartShape.Polygon, 80, 80, 5);
            var ring = vm.Parts.Single(p => p.Name == "Ring");
            ring.Material = "SUS304"; ring.Thickness = 1.25;
            ring.Layers.Single(l => l.Name == "HOLES").IsChecked = false;
            ring.Quantity = 7;
            vm.Settings.SheetPresets.Add(new SheetPreset("SUS tấm", 2440, 1220, "SUS304", 1.25));
            vm.Settings.Spacing = 4.5; vm.Settings.Direction = NestDirection.FillWidth;

            string proj = Path.Combine(tmp, "proj", "a" + ProjectStore.Extension);
            Directory.CreateDirectory(Path.GetDirectoryName(proj)!);
            dlg.SaveFile = proj;
            Check(vm.SaveProject(true), "lưu");
            var childReference = ProjectStore.LoadProjectIndex(proj).Single();
            string childPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(proj)!, childReference.Path));
            string childJson = File.ReadAllText(childPath);
            Check(childJson.Contains($"\"Version\": {ProjectStore.CurrentFormatVersion}", StringComparison.Ordinal),
                "workspace ghi đúng version schema hiện tại");
            Check(childJson.Contains("..\\dxf\\") || childJson.Contains("../dxf/") || childJson.Contains("..\\\\dxf"), "đường dẫn DXF tương đối trong file nhóm");

            var vm2 = NewVm();
            Check(vm2.OpenProject(proj), "mở");
            Check(vm2.Parts.Count == vm.Parts.Count, "đủ số dòng");
            var r2 = vm2.Parts.Single(p => p.Name == "Ring");
            Check(r2.Quantity == 7, "số lượng");
            Check(r2.Material == "SUS304" && Math.Abs(r2.Thickness - 1.25) < 1e-9 && r2.AssignedPreset?.Name == "SUS tấm", "vật liệu/độ dày tự link lại tới profile");
            Check(!r2.Layers.Single(l => l.Name == "HOLES").IsChecked, "layer bỏ chọn được nhớ");
            var rect2 = vm2.Parts.Single(p => p.Kind == PartKind.Rect);
            Check(Math.Abs(rect2.Width - 321) < 1e-9 && rect2.Material == "SS400" && Math.Abs(rect2.Thickness - 2.5) < 1e-9, "chữ nhật và metadata vật liệu");
            var template2 = vm2.Parts.Single(p => p.Kind == PartKind.Template);
            Check(template2.TemplateShape == StandardPartShape.Polygon && template2.TemplateSides == 5 &&
                  template2.Geometry?.Segments.Count == 5 && Math.Abs(template2.TemplateSize - 80) < 1e-9,
                "hình mẫu và kích thước được lưu/mở lại từ dự án");
            Check(vm2.Settings.SheetPresets.Single(p => p.Name == "SUS tấm").Material == "SUS304", "sheet profile roundtrip");
            Check(vm2.Settings.Spacing == 4.5 && vm2.Settings.Direction == NestDirection.FillWidth, "thông số");

            string copy = Path.Combine(tmp, "proj-copy", "b" + ProjectStore.Extension);
            dlg.SaveFile = copy;
            Check(vm2.SaveProject(true), "Save As dự án");
            var vm3 = NewVm();
            Check(vm3.OpenProject(copy) && vm3.Parts.Count == vm2.Parts.Count, "Save As giữ dữ liệu và rebasing tham chiếu DXF");
        });
        Test("Dự án nhiều nhóm: chuyển nhóm, lưu kết quả nesting và mở lại", () =>
        {
            var vm = NewVm();
            vm.AddRect();
            vm.Parts[0].Name = "Part gốc";
            string project = Path.Combine(tmp, "multi-group", "job.nest2d");
            dlg.SaveFile = project;
            Check(vm.SaveProject(true), "tạo dự án tổng");
            var firstGroup = vm.SelectedProjectGroup!;

            Check(vm.CreateProjectGroup("SUS304 · 1.5 mm"), "tạo nhóm mới");
            var secondGroup = vm.SelectedProjectGroup!;
            Check(secondGroup != firstGroup && vm.Parts.Count == 0, "nhóm mới có danh sách riêng");
            vm.AddRect();
            vm.Parts[0].Name = "Part SUS";
            vm.Parts[0].Material = "SUS304";
            vm.Parts[0].Thickness = 1.5;
            var stock = new SheetPreset("SUS304 1.5", 1000, 600, "SUS304", 1.5);
            vm.Settings.SheetPresets.Add(stock);
            vm.Parts[0].AssignPreset(stock);
            vm.RunAsync().GetAwaiter().GetResult();
            Check(vm.Result?.Sheets.Count == 1, "nesting nhóm SUS");

            vm.SelectedProjectGroup = firstGroup;
            Check(vm.Parts.Single().Name == "Part gốc", "khôi phục danh sách nhóm đầu");
            vm.SelectedProjectGroup = secondGroup;
            Check(vm.Parts.Single().Name == "Part SUS" && vm.Result?.Sheets.Count == 1, "khôi phục kết quả nhóm SUS khi chuyển lại");
            Check(vm.SaveProject(false), "lưu dự án nhiều nhóm");

            var reopened = NewVm();
            Check(reopened.OpenProject(project), "mở lại dự án nhiều nhóm");
            Check(reopened.ProjectGroups.Count == 2, "cả hai nhóm được nạp");
            var reopenedSecond = reopened.ProjectGroups.Single(group => group.Name == secondGroup.Name);
            reopened.SelectedProjectGroup = reopenedSecond;
            Check(reopened.Parts.Single().Name == "Part SUS" && reopened.Result?.Sheets.Count == 1, "kết quả nesting được lưu/nạp theo nhóm");
        });
        Test("Lưu dự án cũ chuyển sang chỉ mục và nhóm con", () =>
        {
            string project = Path.Combine(tmp, "legacy", "old.nest2d");
            Directory.CreateDirectory(Path.GetDirectoryName(project)!);
            var legacySettings = new NestSettings { SheetWidth = 1000, SheetHeight = 600 };
            File.WriteAllText(project, System.Text.Json.JsonSerializer.Serialize(new
            {
                Settings = legacySettings.ToJson(),
                Parts = new[] { new { Kind = "Rect", Name = "Legacy part", Material = "SS400", Quantity = 2,
            CanRotate = true, Flip = false, Thickness = 1.2, Width = 200.0, Height = 100.0 } }
            }));

            var vm = NewVm();
            Check(vm.OpenProject(project), "mở project legacy");
            Check(vm.Parts.Count == 1 && vm.Parts[0].Name == "Legacy part", "dữ liệu legacy được đọc");
            Check(vm.SaveProject(false), "chuyển định dạng khi lưu");
            Check(ProjectStore.IsProjectIndex(project) && Directory.Exists(ProjectStore.DataDirectory(project)), "master và data directory được tạo");
            var migrated = NewVm();
            Check(migrated.OpenProject(project) && migrated.Parts.Single().Name == "Legacy part", "mở lại dữ liệu đã chuyển");
        });
        Test("Nhân bản/gỡ nhóm giữ nguyên file, nhóm con bị thiếu không chặn nhóm khác", () =>
        {
            var vm = NewVm();
            vm.AddRect();
            string project = Path.Combine(tmp, "group-errors", "job.nest2d");
            dlg.SaveFile = project;
            Check(vm.SaveProject(true), "tạo dự án gốc");
            Check(vm.CreateProjectGroup("Nhóm phụ"), "thêm nhóm phụ");
            Check(vm.DuplicateProjectGroup(), "nhân bản nhóm phụ");
            var duplicate = vm.SelectedProjectGroup!;
            string duplicatePath = duplicate.Path;
            dlg.ConfirmAnswer = true;
            Check(vm.RemoveProjectGroup(duplicate), "gỡ nhóm nhân bản");
            Check(File.Exists(duplicatePath) && vm.ProjectGroups.Count == 2, "file con được giữ lại sau khi gỡ");

            var missingGroup = vm.ProjectGroups.Single(group => group.Name == "Nhóm phụ");
            File.Delete(missingGroup.Path);
            var reopened = NewVm();
            Check(reopened.OpenProject(project), "mở project còn một nhóm hợp lệ");
            Check(reopened.ProjectGroups.Count == 2 && reopened.Parts.Count == 1, "nhóm khả dụng vẫn được nạp");
            Check(reopened.Messages.Any(message => message.Contains("không tìm thấy file con")), "báo nhóm con bị thiếu");
        });
        Test("Trạng thái thay đổi dự án: lưu xóa dirty và xác nhận bỏ thay đổi", () =>
        {
            var vm = NewVm();
            vm.AddRect();
            Check(vm.IsProjectDirty, "thêm Part đánh dấu dự án chưa lưu");

            string proj = Path.Combine(tmp, "dirty-state.nest2d");
            dlg.SaveFile = proj;
            Check(vm.SaveProject(true) && !vm.IsProjectDirty, "lưu thành công xóa trạng thái dirty");

            vm.Parts[0].Name = "Part đã sửa";
            Check(vm.IsProjectDirty, "sửa Part đánh dấu dự án dirty");
            dlg.ConfirmAnswer = false;
            vm.NewProject();
            Check(vm.Parts.Count == 1 && vm.IsProjectDirty, "hủy tạo mới giữ nguyên dự án");

            dlg.ConfirmAnswer = true;
            vm.NewProject();
            Check(vm.Parts.Count == 0 && !vm.IsProjectDirty, "xác nhận tạo mới xóa nội dung và dirty");
        });
        Test("Mở dự án: hủy xác nhận giữ lại thay đổi chưa lưu", () =>
        {
            var source = NewVm();
            source.AddRect();
            string proj = Path.Combine(tmp, "open-confirm.nest2d");
            dlg.SaveFile = proj;
            Check(source.SaveProject(true), "tạo file dự án fixture");

            var vm = NewVm();
            vm.AddRect();
            vm.Parts[0].Name = "Thay đổi chưa lưu";
            dlg.ConfirmAnswer = false;
            Check(!vm.OpenProject(proj), "mở bị hủy");
            Check(vm.Parts.Count == 1 && vm.Parts[0].Name == "Thay đổi chưa lưu" && vm.IsProjectDirty, "giữ nguyên dữ liệu hiện tại");
        });
        Test("Mở dự án có file DXF bị mất: vẫn mở phần còn lại, báo vấn đề", () =>
        {
            var vm = NewVm();
            string d2 = Path.Combine(tmp, "d2"); Directory.CreateDirectory(d2);
            File.Copy(Path.Combine(dxfDir, "Plate_x4.dxf"), Path.Combine(d2, "Plate_x4.dxf"));
            vm.AddPaths(new[] { d2 });
            vm.AddRect();
            string proj = Path.Combine(d2, "p.nest2d");
            dlg.SaveFile = proj; vm.SaveProject(true);
            File.Delete(Path.Combine(d2, "Plate_x4.dxf"));
            var vm2 = NewVm();
            Check(vm2.OpenProject(proj), "mở được");
            Check(vm2.Parts.Count == 1 && vm2.Messages.Count == 1, "còn chữ nhật, 1 vấn đề");
        });
        Test("Mở file dự án hỏng: báo lỗi chứ không sập", () =>
        {
            var vm = NewVm();
            string p = Path.Combine(tmp, "hong.nest2d"); File.WriteAllText(p, "{ khong phai json");
            Check(!vm.OpenProject(p), "trả về false");
            Check(dlg.Messages.Count == 1, "có hộp thoại lỗi");
        });
        Test("Từ chối schema dự án tương lai thay vì nạp sai dữ liệu", () =>
        {
            var settings = new NestSettings();
            var problems = new List<string>();
            string futureGroup = Path.Combine(tmp, "future-group.nest2d");
            File.WriteAllText(futureGroup, JsonSerializer.Serialize(new ProjectStore.ProjectDto
            {
                Kind = ProjectStore.GroupKind,
                Version = ProjectStore.CurrentFormatVersion + 1
            }));
            bool groupRejected = false;
            try { ProjectStore.LoadWorkspace(futureGroup, settings, problems); }
            catch (InvalidDataException) { groupRejected = true; }
            Check(groupRejected, "file nhóm version mới hơn bị từ chối rõ ràng");

            string futureIndex = Path.Combine(tmp, "future-index.nest2d");
            File.WriteAllText(futureIndex, JsonSerializer.Serialize(new ProjectStore.ProjectDto
            {
                Kind = ProjectStore.ProjectKind,
                Version = ProjectStore.CurrentFormatVersion + 1,
                Groups = { new ProjectStore.GroupReferenceDto { Id = "group", Name = "Nhóm", Path = "group.nest2d" } }
            }));
            bool indexRejected = false;
            try { ProjectStore.LoadProjectIndex(futureIndex); }
            catch (InvalidDataException) { indexRejected = true; }
            Check(indexRejected, "file chỉ mục version mới hơn bị từ chối rõ ràng");
        });
        Test("Thông số: kiểm tra hợp lệ và lưu/nạp JSON (file hỏng được báo lỗi)", () =>
        {
            var s = new NestSettings();
            Check(s.Validate() == null, "mặc định hợp lệ");
            s.Spacing = -1; Check(s.Validate() != null, "khe âm");
            s.Spacing = 3; s.RotationStep = 1; Check(s.Validate() != null, "bước xoay quá nhỏ");
            s.RotationStep = 45; s.Direction = NestDirection.FillLength; s.Margin = 12; s.MaxSeconds = 42; s.TargetUtilizationPercent = 91;
            s.SheetPresets.Add(new SheetPreset("SS400 3mm", 3000, 1500, "SS400", 3) { Note = "Mặt phẳng" });
            string p = Path.Combine(tmp, "set.json"); s.SaveTo(p);
            var t = new NestSettings(); t.LoadFrom(p);
            Check(t.RotationStep == 45 && t.Direction == NestDirection.FillLength && t.Margin == 12 && t.MaxSeconds == 42 && t.TargetUtilizationPercent == 91, "round-trip");
            Check(t.SheetPresets.Single(profile => profile.Material == "SS400").Note == "Mặt phẳng", "ghi chú profile được lưu/nạp");
            File.WriteAllText(p, "rac"); var u = new NestSettings();
            bool loadFailureReported = false;
            try { u.LoadFrom(p); }
            catch (System.Text.Json.JsonException) { loadFailureReported = true; }
            Check(loadFailureReported && u.Validate() == null && u.SheetWidth == 2500,
                "file hỏng được báo và không làm thay đổi thiết lập mặc định");
            bool saveFailureReported = false;
            try { u.SaveTo("bad\0path"); }
            catch (ArgumentException) { saveFailureReported = true; }
            Check(saveFailureReported, "lỗi lưu thiết lập được trả về thay vì bị bỏ qua");
            var preset = NestSettings.Presets[1]; u.ApplyPreset(preset);
            Check(u.SheetWidth == preset.Width && u.SheetHeight == preset.Height, "preset");
        });



        Console.WriteLine("Thông báo nhanh và giao diện");
        Test("Toast: thêm, đóng, giới hạn số thẻ, tự tắt", () =>
        {
            var t = new ToastService { AutoDismiss = false };
            var a = t.Show(ToastKind.Success, "A");
            for (int i = 0; i < 6; i++) t.Show(ToastKind.Info, "x" + i);
            Check(t.Items.Count == ToastService.MaxVisible && !t.Items.Contains(a), "giới hạn thẻ, bỏ thẻ cũ nhất");
            t.Items[0].DismissCommand.Execute(null);
            Check(t.Items.Count == ToastService.MaxVisible - 1, "đóng một thẻ");
            t.Clear(); Check(t.Items.Count == 0, "xoá hết");
            var u = new ToastService();
            u.Show(ToastKind.Info, "tự tắt", "", 0.2);
            WaitUntil(() => u.Items.Count == 0, 5000);
            foreach (ToastKind k in Enum.GetValues<ToastKind>())
            {
                var it = new ToastService { AutoDismiss = false }.Show(k, "t", "m");
                Check(it.AccentHex.StartsWith('#') && it.TintHex.StartsWith('#') && it.IconData.Length > 4, "dữ liệu hiển thị " + k);
            }
        });
        Test("Nesting xong và lỗi đều bắn thông báo nhanh", () =>
        {
            var vm = NewVm();
            vm.Toasts.AutoDismiss = false;
            vm.AddRect(); vm.Parts[0].Width = 300; vm.Parts[0].Height = 200; vm.Parts[0].Quantity = 2;
            PrepareProfiles(vm);
            vm.RunCommand.Execute(null); WaitIdle(vm);
            Check(vm.Result != null, "có kết quả");
            Check(vm.Toasts.Items.Any(x => x.Kind == ToastKind.Success || x.Kind == ToastKind.Warning), "có thông báo kết quả");
        });
        Test("Hủy xác nhận thì không bắt đầu nesting", () =>
        {
            var vm = NewVm();
            vm.AddRect();
            vm.Parts[0].Width = 300;
            vm.Parts[0].Height = 200;
            vm.Parts[0].Quantity = 2;
            PrepareProfiles(vm);
            dlg.ConfirmAnswer = false;
            vm.RunAsync().GetAwaiter().GetResult();
            Check(vm.Result == null && !vm.IsBusy, "không chạy khi người dùng hủy xác nhận");
        });
        Test("Tên dự án không lặp tên ứng dụng", () =>
        {
            var vm = NewVm();
            Check(vm.Title == "Dự án mới" && !vm.Title.Contains("Cyclone Nest2D", StringComparison.Ordinal),
                "tiêu đề dự án chỉ hiển thị tên dự án");
        });
        Test("XAML: có GridSplitter kéo vùng, icon vector, toast host; không còn icon bằng ký tự", () =>
        {
            string? root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "src", "CycloneNest2D.Desktop", "MainWindow.xaml"))) root = Path.GetDirectoryName(root);
            var x = File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "MainWindow.xaml"));
            var app = File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "App.xaml"));
            var theme = File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "Themes", "DesignSystem.xaml"));
            Check(System.Text.RegularExpressions.Regex.Matches(x, "<GridSplitter").Count >= 2, "đủ thanh kéo cho các vùng có thể đổi kích thước trong bố cục hiện tại");
            Check(x.Contains("Toasts.Items"), "toast host");
            Check(x.Contains("VerticalAlignment=\"Top\"") && x.Contains("HorizontalAlignment=\"Right\""), "toast ở góc trên bên phải");
            Check(x.Contains("Header=\"Thông tin\"") && x.Contains("NavInfo\" Tag=\"6\""), "trang Thông tin sau Thiết lập");
            Check(x.Contains("DeleteSelectedSheet_Click") && x.Contains("IsProjectDirty"), "xóa nesting theo tấm có xác nhận và trạng thái dự án chưa lưu");
            Check(x.Contains("SelectedItem=\"{Binding SelectedProjectGroup, Mode=TwoWay}\"") &&
                  x.Contains("Header=\"Tạo nhóm vật liệu…\"") && x.Contains("Click=\"RenameProjectGroup_Click\""),
                "chọn nhóm trên thanh trên cùng và tạo/đổi tên từ menu chuột phải");
            Check(x.Contains("Border Grid.Column=\"1\" Background=\"{DynamicResource AccentTintBrush}\"") &&
                  !x.Contains("ProjectMenu_Click", StringComparison.Ordinal) &&
                  !x.Contains("x:Name=\"ProjectMenu\"", StringComparison.Ordinal),
                "tên dự án chỉ hiển thị, có icon tương phản và không mở menu chức năng");
            Check(x.Contains("Command=\"{Binding NewProjectCommand}\"") &&
                  x.Contains("Command=\"{Binding OpenProjectCommand}\"") &&
                  x.Contains("Command=\"{Binding SaveProjectCommand}\"") &&
                  app.Contains("x:Key=\"IcNewProject\"") && app.Contains("x:Key=\"IcOpenProject\""),
                "có icon thao tác tạo/mở/lưu dự án trên thanh trên cùng");
            Check(x.Contains("Data=\"{StaticResource IcEye}\"") &&
                  !x.Contains("DataGridCheckBoxColumn Header=\"Xoay\"") &&
                  !x.Contains("DataGridCheckBoxColumn Header=\"Lật\""),
                "nút xem nhanh Part dùng icon, cột xoay/lật bị gỡ");
            Check(x.Contains("Grid.Column=\"2\"") && x.Contains("Text=\"LAYER DXF\"") &&
                  x.Contains("Text=\"Hình xem trước\"") && x.Contains("x:Name=\"PartPreview\"") &&
                  x.Contains("Click=\"OpenPartPreview_Click\""),
                "vùng Part chia đôi: layer phía trên, preview phía dưới, có nút mở xem lớn");
            Check(theme.Contains("x:Key=\"CenteredEditor\"") &&
                  theme.Contains("<Setter Property=\"VerticalContentAlignment\" Value=\"Center\"/>") &&
                  x.Contains("EditingElementStyle=\"{StaticResource CenteredEditor}\""),
                "ô nhập liệu trong bảng được căn giữa theo chiều dọc");
            Check(!File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "MainWindow.xaml.cs"))
                      .Contains("BuildBooleanPropertyRow", StringComparison.Ordinal) &&
                  !File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "MainWindow.xaml.cs"))
                      .Contains("Cho phép xoay", StringComparison.Ordinal) &&
                  !File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "MainWindow.xaml.cs"))
                      .Contains("Lật hình", StringComparison.Ordinal),
                "hộp thuộc tính không còn hai tùy chọn xoay/lật");
            Check(x.Contains("RowStyle=\"{StaticResource PartRowStyle}\"") &&
                  theme.Contains("x:Key=\"PartSelectionBgBrush\"") && theme.Contains("x:Key=\"PartListBorderBrush\""),
                "selection và viền danh sách Part có token riêng");
            foreach (var g in new[] { "▤", "▧", "▦", "⊞", "⇧", "▶", "⚙", "↖", "✋", "▣", "✓", "⚠" })
                Check(!x.Contains(g), "còn icon ký tự " + g);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(x, @"StaticResource (Ic[A-Za-z]+)\}"))
                Check(app.Contains("x:Key=\"" + m.Groups[1].Value + "\""), "thiếu geometry " + m.Groups[1].Value);
            string dialog = File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "ConfirmationDialog.xaml"));
            System.Xml.Linq.XDocument.Parse(dialog);
            System.Xml.Linq.XDocument.Parse(app);
        });
        Test("XAML: lệnh nesting chỉ hiện trong tab Nesting và danh mục tấm hỗ trợ sửa/chọn nhiều dòng", () =>
        {
            string? root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "src", "CycloneNest2D.Desktop", "MainWindow.xaml"))) root = Path.GetDirectoryName(root);
            string xaml = File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "MainWindow.xaml"));
            string code = File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "MainWindow.xaml.cs"));
            var doc = System.Xml.Linq.XDocument.Parse(xaml);
            System.Xml.Linq.XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            Check(!xaml.Contains("Trợ giúp", StringComparison.Ordinal) &&
                  !xaml.Contains("Click=\"Help_Click\"", StringComparison.Ordinal) &&
                  !code.Contains("Help_Click", StringComparison.Ordinal),
                "nút trợ giúp và thanh công cụ trống đã được bỏ");
            var nestingTab = doc.Descendants(wpf + "TabItem").Single(element => (string?)element.Attribute("Header") == "Nesting");
            Check(!nestingTab.Descendants(wpf + "ComboBox").Any(combo =>
                      ((string?)combo.Attribute("SelectedItem"))?.Contains("SelectedProjectGroup", StringComparison.Ordinal) == true),
                "bỏ bộ chọn nhóm vật liệu trùng trong tab Nesting");
            Check(nestingTab.Descendants(wpf + "UniformGrid").All(grid => (string?)grid.Attribute("Grid.Row") != "1") &&
                  nestingTab.Descendants(wpf + "TextBlock").Any(text => (string?)text.Attribute("Text") == "CHI TIẾT ĐÃ XẾP") &&
                  nestingTab.Descendants(wpf + "TextBlock").Any(text => (string?)text.Attribute("Text") == "TẤM ĐÃ DÙNG"),
                "đưa chỉ số nesting lên cùng hàng tiêu đề");
            Check(nestingTab.Descendants(wpf + "Button").Any(button => ((string?)button.Attribute("Command"))?.Contains("RunCommand", StringComparison.Ordinal) == true) &&
                  nestingTab.Descendants(wpf + "Button").Any(button => ((string?)button.Attribute("Command"))?.Contains("CancelCommand", StringComparison.Ordinal) == true),
                "nút chạy/dừng nằm trong tab Nesting");
            Check(nestingTab.Descendants(wpf + "StackPanel").Any(panel =>
                      (string?)panel.Attribute("Grid.Column") == "2" &&
                      panel.Descendants(wpf + "Button").Any(button => ((string?)button.Attribute("Command"))?.Contains("RunCommand", StringComparison.Ordinal) == true) &&
                      panel.Descendants(wpf + "Button").Any(button => ((string?)button.Attribute("Command"))?.Contains("CancelCommand", StringComparison.Ordinal) == true)) &&
                  nestingTab.Descendants(wpf + "UniformGrid").Any(grid =>
                      (string?)grid.Attribute("Grid.Column") == "1" && (string?)grid.Attribute("Columns") == "4"),
                "nút nằm cùng hàng chỉ số, các chỉ số được chia đều trong 4 cột");
            Check(nestingTab.Descendants(wpf + "Button").Any(button =>
                      ((string?)button.Attribute("Command"))?.Contains("CancelCommand", StringComparison.Ordinal) == true &&
                      ((string?)button.Attribute("Style"))?.Contains("DangerButton", StringComparison.Ordinal) == true),
                "nút dừng dùng màu cảnh báo rõ ràng, đồng bộ với theme");
            Check(doc.Descendants(wpf + "ProgressBar").Any(bar =>
                      ((string?)bar.Attribute("Value"))?.Contains("ProgressPercent", StringComparison.Ordinal) == true) &&
                  doc.Descendants(wpf + "TextBlock").Any(text =>
                      ((string?)text.Attribute("Text"))?.Contains("ElapsedNestTimeText", StringComparison.Ordinal) == true) &&
                  doc.Descendants(wpf + "TextBlock").Any(text =>
                      ((string?)text.Attribute("Text"))?.Contains("EstimatedRemainingTimeText", StringComparison.Ordinal) == true),
                "hộp tiến trình có phần trăm, thời gian đã chạy và ETA");
            var presetsGrid = doc.Descendants(wpf + "DataGrid").Single(element =>
                (string?)element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name") == "SheetPresetsGrid");
            var partsGrid = doc.Descendants(wpf + "DataGrid").Single(element =>
                (string?)element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name") == "PartsGrid");
            System.Xml.Linq.XNamespace xamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
            var partHeaderStyle = doc.Descendants(wpf + "Style").Single(style =>
                (string?)style.Attribute(xamlNs + "Key") == "PartTableHeader");
            Check(xaml.Contains("Width=\"3.5*\"") &&
                  partHeaderStyle.Descendants(wpf + "Setter").Any(setter =>
                      (string?)setter.Attribute("Property") == "HorizontalContentAlignment" &&
                      (string?)setter.Attribute("Value") == "Center"),
                "vùng bảng chi tiết rộng, layer/preview bên phải; tiêu đề cột căn giữa");
            Check(partsGrid.Descendants(wpf + "DataGridColumn").Count() == 0 &&
                  partsGrid.Descendants().Count(element =>
                      element.Name.LocalName is "DataGridTextColumn" or "DataGridTemplateColumn" or "DataGridCheckBoxColumn") == 7,
                "danh sách Part chỉ còn bảy cột sau khi bỏ xoay/lật");
            Check((string?)partsGrid.Attribute("GridLinesVisibility") == "All" &&
                  (string?)partsGrid.Attribute("HorizontalGridLinesBrush") == "{DynamicResource PartListBorderBrush}" &&
                  (string?)partsGrid.Attribute("VerticalGridLinesBrush") == "{DynamicResource PartListBorderBrush}" &&
                  partsGrid.Descendants(wpf + "Setter").Any(setter =>
                      (string?)setter.Attribute("Property") == "BorderThickness" &&
                      (string?)setter.Attribute("Value") == "0,0,1,1"),
                "danh sách chi tiết có lưới ngang/dọc theo màu theme");
            var selectionSummary = doc.Descendants(wpf + "TextBlock").SingleOrDefault(text =>
                (string?)text.Attribute(xamlNs + "Name") == "PartSelectionSummary");
            var selectionToolbar = selectionSummary?.Ancestors(wpf + "StackPanel").FirstOrDefault(panel =>
                panel.Descendants(wpf + "Button").Any(button => (string?)button.Attribute("Content") == "Chọn tất cả"));
            Check(selectionToolbar?.Descendants(wpf + "Button").Any(button => (string?)button.Attribute("Content") == "Bỏ chọn") == true,
                "bộ đếm lựa chọn nằm trong hàng nút thao tác");
            Check(!xaml.Contains("Chuột phải: thuộc tính · Delete: xóa dòng đã chọn", StringComparison.Ordinal) &&
                  !partsGrid.Parent!.Parent!.Elements(wpf + "Grid").Any(grid =>
                      (string?)grid.Attribute("Grid.Row") == "2"),
                "đã bỏ toàn bộ dòng trạng thái/hướng dẫn dưới bảng");
            Check(new[] { "48", "280", "280", "100", "100", "100", "*" }.SequenceEqual(
                      partsGrid.Descendants().Where(element =>
                          element.Name.LocalName is "DataGridTextColumn" or "DataGridTemplateColumn")
                          .Select(column => (string?)column.Attribute("Width"))) &&
                  (string?)partsGrid.Attribute("FontSize") == "12.5" &&
                  (string?)partsGrid.Attribute("BorderThickness") == "1.5" &&
                  partsGrid.Descendants(wpf + "Setter").Any(setter =>
                      (string?)setter.Attribute("Property") == "HorizontalContentAlignment" &&
                      (string?)setter.Attribute("Value") == "Center") &&
                  (double?)partHeaderStyle.Descendants(wpf + "Setter").First(setter =>
                      (string?)setter.Attribute("Property") == "FontSize").Attribute("Value") > 12.5,
                "cột Part cố định theo bố cục, nội dung căn giữa, viền rõ và tiêu đề lớn hơn chữ trong dòng");
            string mainWindowCode = File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "MainWindow.xaml.cs"));
            string partAlignmentCode = File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "PartGridAlignmentSettings.cs"));
            Check(!mainWindowCode.Contains("AutoFitPartColumns", StringComparison.Ordinal) &&
                  !mainWindowCode.Contains("SchedulePartColumnAutoFit", StringComparison.Ordinal) &&
                  !mainWindowCode.Contains("PartsGrid_Loaded", StringComparison.Ordinal) &&
                  mainWindowCode.Contains("PartColumnContextMenu_Opening", StringComparison.Ordinal) &&
                  mainWindowCode.Contains("Căn tiêu đề", StringComparison.Ordinal) &&
                  mainWindowCode.Contains("Căn nội dung", StringComparison.Ordinal) &&
                  mainWindowCode.Contains("new[] { PartGridTextAlignment.Left, PartGridTextAlignment.Center }", StringComparison.Ordinal) &&
                  mainWindowCode.Contains("PartGridAlignmentChoice", StringComparison.Ordinal) &&
                  !partHeaderStyle.Descendants(wpf + "Setter").Any(setter => (string?)setter.Attribute("Property") == "ContextMenu") &&
                  partAlignmentCode.Contains("part-grid-alignment.json", StringComparison.Ordinal) &&
                  partAlignmentCode.Contains("Normalize(alignment)", StringComparison.Ordinal) &&
                  mainWindowCode.Contains("PartColumnAlignment_Click", StringComparison.Ordinal),
                "không tự đo cột; menu căn tiêu đề/nội dung chỉ có trái/giữa và lưu thiết lập");
            string desktopProject = File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "CycloneNest2D.Desktop.csproj"));
            Check(desktopProject.Contains("<Version>1.0.11</Version>", StringComparison.Ordinal) &&
                  desktopProject.Contains("Cập nhật 31/10/2026", StringComparison.Ordinal),
                "tăng phiên bản và cập nhật ngày phát hành");
            Check((string?)presetsGrid.Attribute("SelectionMode") == "Extended" &&
                  (string?)presetsGrid.Attribute("CanUserResizeColumns") == "False" &&
                  new[] { "42", "138", "112", "116", "124", "230" }.SequenceEqual(
                      presetsGrid.Descendants(wpf + "DataGridTextColumn").Select(column => (string?)column.Attribute("Width"))) &&
                  new[] { "STT", "Tên vật liệu", "Dài (mm)", "Rộng (mm)", "Độ dày (mm)", "Ghi chú" }.All(header =>
                      presetsGrid.Descendants(wpf + "DataGridTextColumn").Any(column => (string?)column.Attribute("Header") == header)),
                "bảng khổ tấm cố định độ rộng cột, có ghi chú, nhập trực tiếp và chọn nhiều dòng");
            Check(xaml.Contains("HorizontalContentAlignment=\"{Binding HorizontalContentAlignment, RelativeSource={RelativeSource AncestorType={x:Type DataGridCell}}}\"", StringComparison.Ordinal) &&
                  presetsGrid.Descendants(wpf + "DataGridTextColumn").All(column =>
                      (string?)column.Attribute("CellStyle") == "{StaticResource CenteredCell}") &&
                  xaml.Contains("SelectedPart.TextInfo", StringComparison.Ordinal),
                "nội dung vật liệu căn giữa và thông tin phát hiện TEXT/MTEXT hiển thị trong preview chi tiết");
            var partTab = doc.Descendants(wpf + "TabItem").Single(element => (string?)element.Attribute("Header") == "Part");
            var actionButtons = partTab.Descendants(wpf + "WrapPanel").Single(panel =>
                panel.Descendants(wpf + "Button").Any(button => (string?)button.Attribute("Content") == "Thư viện mẫu"));
            Check((string?)actionButtons.Attribute("Grid.Column") == "1" &&
                  (string?)partTab.Descendants(wpf + "StackPanel").First(panel =>
                      panel.Descendants(wpf + "TextBlock").Any(text => (string?)text.Attribute("Text") == "MÃ CHI TIẾT"))
                      .Attribute("Grid.Column") == "2" &&
                  new[] { "Thêm thư mục", "Thư viện mẫu", "Thêm DXF" }.SequenceEqual(
                      actionButtons.Descendants(wpf + "Button").Select(button => (string?)button.Attribute("Content"))),
                "ba nút thêm nằm bên trái khu vực thống kê trong tab Part");
            string previewSource = File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "PartPreviewView.cs"));
            string shapeDialog = File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "StandardShapeLibraryWindow.cs"));
            Check(previewSource.Contains("DrawTextEntity", StringComparison.Ordinal) &&
                  previewSource.Contains("shape.Texts", StringComparison.Ordinal) &&
                  shapeDialog.Contains("StandardPartShape.Polygon", StringComparison.Ordinal) &&
                  code.Contains("OpenStandardShapeLibrary_Click", StringComparison.Ordinal),
                "preview vẽ TEXT/MTEXT và thư viện có hình tròn, chữ nhật, tam giác, đa giác chỉnh được");
            Check(new[] { "Chọn tất cả", "Hủy chọn", "Thêm khổ tấm", "Xóa tấm đã chọn" }.All(header =>
                      presetsGrid.Descendants(wpf + "MenuItem").Any(item => (string?)item.Attribute("Header") == header)) &&
                  code.Contains("SheetPresetsGrid_RowEditEnding", StringComparison.Ordinal) &&
                  code.Contains("DeleteSelectedSheetPresets_Click", StringComparison.Ordinal) &&
                  !xaml.Contains("AddStockFromNesting_Click", StringComparison.Ordinal),
                "menu chuột phải và các thao tác chỉnh sửa/xóa đã được nối");
            Check(xaml.Contains("Xóa nesting tấm đang chọn", StringComparison.Ordinal) &&
                  xaml.Contains("DeleteSelectedSheet_Click", StringComparison.Ordinal) &&
                  code.Contains("ConfirmationDialog.Confirm", StringComparison.Ordinal) &&
                  !xaml.Contains("DeletePlacementCommand", StringComparison.Ordinal),
                "xóa nesting nhắm đúng tấm và yêu cầu xác nhận");
        });

        Console.WriteLine("Lớp WPF (kiểm tra tĩnh, vì không biên dịch được WPF trên Linux)");
        Test("XAML: mọi {Binding} trỏ đúng thuộc tính ViewModel, lệnh đúng kiểu, TwoWay có setter, handler có trong code-behind", () =>
        {
            string? root = AppContext.BaseDirectory;
            string? xamlPath = null;
            while (root != null)
            {
                var c = Path.Combine(root, "src", "CycloneNest2D.Desktop", "MainWindow.xaml");
                if (File.Exists(c)) { xamlPath = c; break; }
                root = Path.GetDirectoryName(root);
            }
            Check(xamlPath != null, "không thấy MainWindow.xaml");
            var doc = System.Xml.Linq.XDocument.Load(xamlPath!);
            string code = File.ReadAllText(Path.ChangeExtension(xamlPath!, ".xaml.cs"));
            var uiAsm = typeof(MainViewModel).Assembly;
            var problems = new List<string>();
            int bindings = 0;

            Type? Resolve(Type t, string path, out System.Reflection.PropertyInfo? last)
            {
                last = null;
                Type? cur = t;
                foreach (var seg in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pi = cur!.GetProperty(seg);
                    if (pi == null) return null;
                    last = pi; cur = pi.PropertyType;
                }
                return cur;
            }

            void Walk(System.Xml.Linq.XElement el, Type? ctx)
            {
                string name = el.Name.LocalName;
                if (name is "DataGridTextColumn" or "DataGridCheckBoxColumn" or "DataGridTemplateColumn")
                    ctx = el.Ancestors().Any(a => a.Name.LocalName == "DataGrid" &&
                        a.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "SheetPresetsGrid"))
                        ? null
                        : typeof(PartItem);
                if (name == "DataTemplate")
                {
                    var dt = el.Attributes().FirstOrDefault(a => a.Name.LocalName == "DataType")?.Value;
                    var key = el.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key")?.Value;
                    if (dt != null)
                    {
                        var tn = dt.Trim('{', '}').Split(':').Last().Trim();
                        ctx = uiAsm.GetType("CycloneNest2D.UI." + tn) ?? throw new Exception("DataType không có: " + dt);
                    }
                    else if (key == "LayerTemplate")
                    {
                        ctx = uiAsm.GetType("CycloneNest2D.UI.LayerItem")
                            ?? throw new Exception("Không tìm thấy LayerItem cho LayerTemplate");
                    }
                    else if (el.Parent?.Name.LocalName is "ListBox.ItemTemplate" or "ItemsControl.ItemTemplate")
                    {
                        var owner = el.Parent.Parent;
                        var source = owner?.Attributes().FirstOrDefault(a => a.Name.LocalName == "ItemsSource")?.Value ?? "";
                        ctx = source.Contains("PartGroups", StringComparison.Ordinal) ? uiAsm.GetType("CycloneNest2D.UI.PartMaterialGroup")
                            : source.Contains("Parts", StringComparison.Ordinal) ? typeof(PartItem)
                            : source.Contains("Sheets", StringComparison.Ordinal) ? uiAsm.GetType("CycloneNest2D.UI.SheetItem")
                            : source.Contains("Messages", StringComparison.Ordinal) ? typeof(string)
                            : null;
                    }
                }
                foreach (var a in el.Attributes())
                {
                    var v = a.Value.Trim();
                    if (a.Name.LocalName is "DataType" or "Key" or "Class") continue;
                    if (name.StartsWith("Setter")) continue;
                    if (v.StartsWith("{Binding") && ctx != null &&
                        !v.Contains("RelativeSource", StringComparison.Ordinal) && !v.Contains("ElementName", StringComparison.Ordinal))
                    {
                        bindings++;
                        if (name == "Run" && a.Name.LocalName == "Text" && !v.Contains("Mode=OneWay", StringComparison.Ordinal))
                            problems.Add("Run.Text binding phải đặt Mode=OneWay rõ ràng");
                        var m = System.Text.RegularExpressions.Regex.Match(v, @"^\{Binding\s*(?:Path=)?([^,=}\s]*)");
                        string path = m.Groups[1].Value;
                        if (path.Length == 0) continue;
                        Type bindingContext = ctx;
                        if (path.StartsWith("DataContext.", StringComparison.Ordinal))
                        {
                            bindingContext = typeof(MainViewModel);
                            path = path["DataContext.".Length..];
                        }
                        var t = Resolve(bindingContext, path, out var pi);
                        if (t == null) { problems.Add($"{name}.{a.Name.LocalName}: không có '{path}' trong {bindingContext.Name}"); continue; }
                        bool explicitTwoWay = v.Contains("Mode=TwoWay", StringComparison.Ordinal);
                        bool defaultTwoWayTarget =
                            (name == "Run" && a.Name.LocalName == "Text") ||
                            (name == "ProgressBar" && a.Name.LocalName == "Value") ||
                            (name == "DataGridTextColumn" && a.Name.LocalName == "Binding") ||
                            (name == "DataGridCheckBoxColumn" && a.Name.LocalName == "Binding") ||
                            (name == "TextBox" && a.Name.LocalName == "Text") ||
                            (name == "CheckBox" && a.Name.LocalName == "IsChecked") ||
                            (name == "RadioButton" && a.Name.LocalName == "IsChecked") ||
                            ((name is "ListBox" or "DataGrid" or "ComboBox") && a.Name.LocalName is "SelectedItem" or "SelectedValue" or "SelectedIndex");
                        bool publicSetter = pi?.GetSetMethod() != null;
                        if (explicitTwoWay && !publicSetter) problems.Add($"{name}.{a.Name.LocalName}: '{path}' TwoWay nhưng không có public setter");
                        if (defaultTwoWayTarget && !publicSetter && !v.Contains("Mode=OneWay", StringComparison.Ordinal) && !v.Contains("Mode=OneTime", StringComparison.Ordinal))
                            problems.Add($"{name}.{a.Name.LocalName}: '{path}' có thể TwoWay mặc định nhưng không có public setter; phải đặt Mode=OneWay");
                        if (a.Name.LocalName == "Command" && !typeof(System.Windows.Input.ICommand).IsAssignableFrom(t)) problems.Add($"Command '{path}' không phải ICommand");
                    }
                    if ((a.Name.LocalName is "Click" or "Closing" or "PreviewDrop" or "PreviewDragOver" or "Drop" or "PreviewMouseRightButtonDown" or "PreviewKeyDown" or "Opened") && !v.StartsWith("{"))
                        if (!code.Contains(" " + v + "(")) problems.Add($"handler '{v}' không có trong code-behind");
                }
                foreach (var ch in el.Elements()) Walk(ch, ctx);
            }
            Walk(doc.Root!, typeof(MainViewModel));
            Check(bindings > 60, $"chỉ kiểm tra được {bindings} binding (phân tích có thể sai)");
            Check(problems.Count == 0, string.Join(" | ", problems));
            Console.WriteLine($"       ({bindings} binding đã kiểm tra)");
        });
        Test("XAML: Closing/Drop handler và x:Class khớp với code-behind", () =>
        {
            string? root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "src", "CycloneNest2D.Desktop", "MainWindow.xaml"))) root = Path.GetDirectoryName(root);
            var xaml = File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "MainWindow.xaml"));
            var cs = File.ReadAllText(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "MainWindow.xaml.cs"));
            Check(xaml.Contains("x:Class=\"CycloneNest2D.Desktop.MainWindow\"") && cs.Contains("namespace CycloneNest2D.Desktop") && cs.Contains("partial class MainWindow"), "x:Class");
            Check(xaml.Contains("local:SheetView") && File.Exists(Path.Combine(root!, "src", "CycloneNest2D.Desktop", "SheetView.cs")), "SheetView");
        });

        Console.WriteLine("Hình ảnh kiểm tra");
        Test("Vẽ cảnh xem trước ra SVG để kiểm tra bằng mắt", () =>
        {
            var vm = NewVm();
            vm.AddPaths(new[] { dxfDir });
            PrepareProfiles(vm);
            vm.Settings.Direction = NestDirection.FillWidth;
            vm.RunCommand.Execute(null); WaitIdle(vm);
            string svg = SceneSvg(vm.Scene);
            string outDir = Environment.GetEnvironmentVariable("N2D_PREVIEW_OUT") ?? tmp;
            File.WriteAllText(Path.Combine(outDir, "preview.svg"), svg);
            Check(svg.Contains("fill-rule=\"evenodd\""), "dùng even-odd");
        });

        // ---------- hàm phụ ----------
        void WaitIdle(MainViewModel vm, int ms = 30000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // AsyncRelayCommand chạy tiếp ở thread pool khi không có SynchronizationContext
            while ((vm.IsBusy || !vm.RunCommand.CanExecute(null) && vm.Parts.Count > 0 && vm.IsBusy) && sw.ElapsedMilliseconds < ms) Thread.Sleep(20);
            Thread.Sleep(60);
            if (sw.ElapsedMilliseconds >= ms) throw new Exception("quá thời gian chờ");
        }

        void WaitUntil(Func<bool> cond, int ms = 15000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!cond() && sw.ElapsedMilliseconds < ms) Thread.Sleep(20);
            if (!cond()) throw new Exception("quá thời gian chờ điều kiện");
        }

        MainViewModel QuickRectVm()
        {
            var vm = NewVm(new NestSettings { SheetWidth = 1000, SheetHeight = 600, MaxSeconds = 1 });
            vm.AddRect(); vm.Parts[0].Width = 300; vm.Parts[0].Height = 200; vm.Parts[0].Quantity = 3;
            vm.AddRect(); vm.Parts[1].Width = 150; vm.Parts[1].Height = 100; vm.Parts[1].Quantity = 2;
            PrepareProfiles(vm);
            vm.RunCommand.Execute(null);
            WaitIdle(vm);
            Check(vm.Result != null, "QuickRectVm: chưa có kết quả");
            return vm;
        }

        void PrepareProfiles(MainViewModel vm)
        {
            vm.Settings.SheetPresets.Clear();
            var profile = new SheetPreset("Test SS400 1mm", vm.Settings.SheetWidth, vm.Settings.SheetHeight, "SS400", 1);
            vm.Settings.SheetPresets.Add(profile);
            vm.AssignPresetToParts(vm.Parts, profile);
            vm.SelectedPreset = profile;
        }

        string SceneSvg(PreviewScene sc)
        {
            var sb = new StringBuilder();
            double H = sc.SheetHeight;
            sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"-20 -20 {sc.SheetWidth + 40} {H + 40}\" width=\"1100\">");
            sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"0\" y=\"0\" width=\"{sc.SheetWidth}\" height=\"{H}\" fill=\"#f4f4f4\" stroke=\"#333\" stroke-width=\"2\"/>");
            sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"{sc.Margin}\" y=\"{sc.Margin}\" width=\"{sc.SheetWidth - 2 * sc.Margin}\" height=\"{H - 2 * sc.Margin}\" fill=\"none\" stroke=\"#c33\" stroke-dasharray=\"6 4\"/>");
            foreach (var p in sc.Parts)
            {
                var d = new StringBuilder();
                foreach (var l in p.Loops)
                {
                    for (int i = 0; i < l.Length; i++)
                        d.Append(CultureInfo.InvariantCulture, $"{(i == 0 ? "M" : "L")}{l[i].X:0.##} {H - l[i].Y:0.##} ");
                    d.Append("Z ");
                }
                sb.Append($"<path d=\"{d}\" fill=\"{PreviewScene.Palette[p.ColorIndex]}\" fill-opacity=\"0.65\" fill-rule=\"evenodd\" stroke=\"#111\" stroke-width=\"1.2\"/>");
            }
            sb.Append("</svg>");
            return sb.ToString();
        }

        return new TestSuite(tmp, cases);
    }

    private sealed record RegisteredTest(string Category, string Name, Action Body);

    private sealed class TestSuite(string tempPath, IReadOnlyList<RegisteredTest> cases) : IDisposable
    {
        public IReadOnlyList<RegisteredTest> Cases { get; } = cases;

        public void Dispose() => Directory.Delete(tempPath, recursive: true);
    }
}

sealed class FakeDialogs : IDialogService
{
    public List<string> Messages { get; } = new();
    public string? Folder, SaveFile;
    public string? OpenFile { get; set; }
    public string[] Files = Array.Empty<string>();
    public bool ConfirmAnswer = true;
    public List<string> Confirmations { get; } = new();
    public string[] PickOpenFiles(string title, string filter) => Files;
    public string? PickFolder(string title) => Folder;
    public string? PickSaveFile(string title, string filter, string defaultName) => SaveFile;
    public string? PickOpenFile(string title, string filter) => OpenFile;
    public void ShowMessage(string message) => Messages.Add(message);
    public bool Confirm(string message) { Confirmations.Add(message); return ConfirmAnswer; }
}
