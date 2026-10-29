using System.Globalization;
using System.Text;
using Nesting2D.Core;
using Nesting2D.UI;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
Console.OutputEncoding = Encoding.UTF8;

int passed = 0, failed = 0;
string tmp = Path.Combine(Path.GetTempPath(), "n2d_ui_" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(tmp);

void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine("  ok   " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("  FAIL " + name + "\n       " + ex.Message); }
}
void Check(bool cond, string msg) { if (!cond) throw new Exception(msg); }

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
    Check(vm.MoveUntilBlocked(0, 1, 0), "mũi tên phải dịch part");
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
    Check(vm.DeleteSelectedPlacements() == 1 && vm.PlacedCount == 1 && part.RemainingQuantity == 2, "delete placement restores quantity");
    vm.ClearNesting();
    Check(vm.Result == null && vm.Sheets.Count == 0 && part.RemainingQuantity == 3, "clear all nesting but keep the source Part");
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
    // xoay quanh tâm có thể vượt lề; nếu bị từ chối thì Status báo và dữ liệu không đổi
    vm.RotateSelected();
    var after = s.Placements[0];
    if (Math.Abs(after.Width - before.Height) < 1e-9)
    {
        Check(Math.Abs((after.X + after.Width / 2) - (before.X + before.Width / 2)) < 1e-9, "tâm X");
        Check(Math.Abs((after.Y + after.Height / 2) - (before.Y + before.Height / 2)) < 1e-9, "tâm Y");
    }
    else Check(after == before, "bị từ chối thì không đổi");
    Check(Validator.Check(vm.Result!, vm.ResultSheet).Count == 0, "validator sạch");
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
    Check(File.ReadAllText(proj).Contains("..\\dxf\\") || File.ReadAllText(proj).Contains("../dxf/") || File.ReadAllText(proj).Contains("..\\\\dxf"), "đường dẫn tương đối");

    var vm2 = NewVm();
    Check(vm2.OpenProject(proj), "mở");
    Check(vm2.Parts.Count == vm.Parts.Count, "đủ số dòng");
    var r2 = vm2.Parts.Single(p => p.Name == "Ring");
    Check(r2.Quantity == 7, "số lượng");
    Check(r2.Material == "SUS304" && Math.Abs(r2.Thickness - 1.25) < 1e-9 && r2.AssignedPreset?.Name == "SUS tấm", "vật liệu/độ dày tự link lại tới profile");
    Check(!r2.Layers.Single(l => l.Name == "HOLES").IsChecked, "layer bỏ chọn được nhớ");
    var rect2 = vm2.Parts.Single(p => !p.IsDxf);
    Check(Math.Abs(rect2.Width - 321) < 1e-9 && rect2.Material == "SS400" && Math.Abs(rect2.Thickness - 2.5) < 1e-9, "chữ nhật và metadata vật liệu");
    Check(vm2.Settings.SheetPresets.Single(p => p.Name == "SUS tấm").Material == "SUS304", "sheet profile roundtrip");
    Check(vm2.Settings.Spacing == 4.5 && vm2.Settings.Direction == NestDirection.FillWidth, "thông số");
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
Test("Thông số: kiểm tra hợp lệ và lưu/nạp JSON (file hỏng thì giữ mặc định)", () =>
{
    var s = new NestSettings();
    Check(s.Validate() == null, "mặc định hợp lệ");
    s.Spacing = -1; Check(s.Validate() != null, "khe âm");
    s.Spacing = 3; s.RotationStep = 1; Check(s.Validate() != null, "bước xoay quá nhỏ");
    s.RotationStep = 45; s.Direction = NestDirection.FillLength; s.Margin = 12; s.MaxSeconds = 42; s.TargetUtilizationPercent = 91;
    string p = Path.Combine(tmp, "set.json"); s.SaveTo(p);
    var t = new NestSettings(); t.LoadFrom(p);
    Check(t.RotationStep == 45 && t.Direction == NestDirection.FillLength && t.Margin == 12 && t.MaxSeconds == 42 && t.TargetUtilizationPercent == 91, "round-trip");
    File.WriteAllText(p, "rac"); var u = new NestSettings(); u.LoadFrom(p);
    Check(u.Validate() == null && u.SheetWidth == 2500, "file hỏng -> mặc định");
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
Test("XAML: có GridSplitter kéo vùng, icon vector, toast host; không còn icon bằng ký tự", () =>
{
    string? root = AppContext.BaseDirectory;
    while (root != null && !File.Exists(Path.Combine(root, "src", "Nesting2D.Desktop", "MainWindow.xaml"))) root = Path.GetDirectoryName(root);
    var x = File.ReadAllText(Path.Combine(root!, "src", "Nesting2D.Desktop", "MainWindow.xaml"));
    var app = File.ReadAllText(Path.Combine(root!, "src", "Nesting2D.Desktop", "App.xaml"));
    Check(System.Text.RegularExpressions.Regex.Matches(x, "<GridSplitter").Count >= 2, "đủ thanh kéo cho các vùng có thể đổi kích thước trong bố cục hiện tại");
    Check(x.Contains("Toasts.Items"), "toast host");
    foreach (var g in new[] { "▤", "▧", "▦", "⊞", "⇧", "▶", "⚙", "↖", "✋", "▣", "✓", "⚠" })
        Check(!x.Contains(g), "còn icon ký tự " + g);
    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(x, @"StaticResource (Ic[A-Za-z]+)\}"))
        Check(app.Contains("x:Key=\"" + m.Groups[1].Value + "\""), "thiếu geometry " + m.Groups[1].Value);
    System.Xml.Linq.XDocument.Parse(app);
});

Console.WriteLine("Lớp WPF (kiểm tra tĩnh, vì không biên dịch được WPF trên Linux)");
Test("XAML: mọi {Binding} trỏ đúng thuộc tính ViewModel, lệnh đúng kiểu, TwoWay có setter, handler có trong code-behind", () =>
{
    string? root = AppContext.BaseDirectory;
    string? xamlPath = null;
    while (root != null)
    {
        var c = Path.Combine(root, "src", "Nesting2D.Desktop", "MainWindow.xaml");
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
        if (name is "DataGridTextColumn" or "DataGridCheckBoxColumn" or "DataGridTemplateColumn") ctx = typeof(PartItem);
        if (name == "DataTemplate")
        {
            var dt = el.Attributes().FirstOrDefault(a => a.Name.LocalName == "DataType")?.Value;
            if (dt != null)
            {
                var tn = dt.Trim('{', '}').Split(':').Last().Trim();
                ctx = uiAsm.GetType("Nesting2D.UI." + tn) ?? throw new Exception("DataType không có: " + dt);
            }
            else if (el.Parent?.Name.LocalName is "ListBox.ItemTemplate" or "ItemsControl.ItemTemplate")
            {
                var owner = el.Parent.Parent;
                var source = owner?.Attributes().FirstOrDefault(a => a.Name.LocalName == "ItemsSource")?.Value ?? "";
                ctx = source.Contains("PartGroups", StringComparison.Ordinal) ? uiAsm.GetType("Nesting2D.UI.PartMaterialGroup")
                    : source.Contains("Parts", StringComparison.Ordinal) ? typeof(PartItem)
                    : source.Contains("Sheets", StringComparison.Ordinal) ? uiAsm.GetType("Nesting2D.UI.SheetItem")
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
    while (root != null && !File.Exists(Path.Combine(root, "src", "Nesting2D.Desktop", "MainWindow.xaml"))) root = Path.GetDirectoryName(root);
    var xaml = File.ReadAllText(Path.Combine(root!, "src", "Nesting2D.Desktop", "MainWindow.xaml"));
    var cs = File.ReadAllText(Path.Combine(root!, "src", "Nesting2D.Desktop", "MainWindow.xaml.cs"));
    Check(xaml.Contains("x:Class=\"Nesting2D.Desktop.MainWindow\"") && cs.Contains("namespace Nesting2D.Desktop") && cs.Contains("partial class MainWindow"), "x:Class");
    Check(xaml.Contains("local:SheetView") && File.Exists(Path.Combine(root!, "src", "Nesting2D.Desktop", "SheetView.cs")), "SheetView");
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

try { Directory.Delete(tmp, true); } catch { }
Console.WriteLine($"\nKết quả: {passed} đạt, {failed} lỗi.");
return failed == 0 ? 0 : 1;

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

sealed class FakeDialogs : IDialogService
{
    public List<string> Messages { get; } = new();
    public string? Folder, SaveFile;
    public string? OpenFile { get; set; }
    public string[] Files = Array.Empty<string>();
    public bool ConfirmAnswer = true;
    public string[] PickOpenFiles(string title, string filter) => Files;
    public string? PickFolder(string title) => Folder;
    public string? PickSaveFile(string title, string filter, string defaultName) => SaveFile;
    public string? PickOpenFile(string title, string filter) => OpenFile;
    public void ShowMessage(string message) => Messages.Add(message);
    public bool Confirm(string message) => ConfirmAnswer;
}
