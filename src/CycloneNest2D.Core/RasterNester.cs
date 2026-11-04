using System.Diagnostics;
using System.Numerics;

namespace CycloneNest2D.Core;

/// <summary>
/// Nesting theo hình dạng thật bằng lưới raster dạng bit:
///  - Mỗi chi tiết (ở từng góc xoay) được chuyển thành mặt nạ ô lưới bảo thủ (mọi ô chạm vật liệu đều bị chiếm; lỗ vẫn trống).
///  - Chi tiết đã đặt được "giãn" thêm đúng khe hở cắt rồi mới ghi vào lưới tấm, nên chi tiết nhỏ vào được lỗ và chữ L lồng được nhau.
///  - Với mỗi hàng y, tìm TẤT CẢ vị trí x hợp lệ cùng lúc bằng phép toán bit (AND-NOT các cửa sổ đã dịch bit), lấy vị trí thấp nhất, trái nhất.
///  - Mỗi vị trí ứng viên đều được xác minh lại bằng hình học chính xác (khoảng cách đường biên, chứa nhau), nên kết quả
///    đúng khe hở ngay cả khi lưới làm tròn.
/// </summary>
internal static class RasterNester
{
    /// <summary>
    /// Dung sai tối đa (mm) trừ khỏi khe hở yêu cầu khi xác minh contour thật để bù sai số rời rạc hóa cung.
    /// Ngưỡng kiểm tra là max(0, spacing - GapTol); contour chồng lấn vẫn bị từ chối. Validator dùng cùng giá trị.
    /// </summary>
    public const double GapTol = 0.1;

    private const int MaxRejects = 400;
    private const int MaxRasterDimension = 8192;
    private const long MaxRasterCells = 16_777_216;
    private const double RasterScanWorkBudget = 2_000_000;
    private const double MaxAdaptiveGridGrowth = 1.25;

    // ---------- Cấu trúc dữ liệu ----------

    private sealed class Variant
    {
        public double Angle;
        public DxfShape Shape = null!;
        public LoopSet Loops = null!;
        public bool HasContour;
        public double W, H;
        public int Wc, Hc, S;
        public (int S, int L)[][] Cover = null!;  // theo hàng, cột tính từ góc chi tiết
        public (int S, int L)[][] Dil = null!;    // Hc + 2S hàng; hàng i ứng với y - S + i; cột tính từ x - S
    }

    private sealed class PartType
    {
        public string Name = "";
        public int Quantity;
        public bool HasShape;
        public bool CanRotate;
        public DxfShape Base = null!;
        public LoopSet BaseLoops = null!;
        public readonly List<Variant> Variants = new();
        public readonly List<double> PrimaryAngles = new();
        public readonly List<double> FineAngles = new();
        public readonly HashSet<string> MaskKeys = new();
        public double Area, W, H;
    }

    private sealed record LayoutSearchResult(
        Outcome? Best,
        List<(int Type, int Inst)> BestOrder,
        string BestName);

    private sealed record Placed(int Type, int Instance, Variant V, double X, double Y);

    private sealed class Sheet
    {
        public readonly int Rows, Cols, Words;
        public readonly ulong[][] Occ;
        public readonly int[] RowVer;
        public readonly ulong[][][] P;      // P[hàng][j]: cửa sổ OR độ dài 2^j (null nếu chưa tính)
        public readonly int[][] PVer;
        public readonly List<Placed> Items = new();
        public readonly List<Placed> ItemsByX = new();
        public readonly HashSet<int> Failed = new();
        public double MaxPlacedWidth;

        public Sheet(int rows, int cols)
        {
            Rows = rows; Cols = cols;
            Words = (cols + 63) / 64 + 4;
            Occ = new ulong[rows][];
            for (int i = 0; i < rows; i++) Occ[i] = new ulong[Words];
            RowVer = new int[rows];
            P = new ulong[rows][][];
            PVer = new int[rows][];
        }

        public void Add(Placed item)
        {
            Items.Add(item);
            MaxPlacedWidth = Math.Max(MaxPlacedWidth, item.V.W);

            int low = 0, high = ItemsByX.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (ItemsByX[middle].X <= item.X) low = middle + 1;
                else high = middle;
            }
            ItemsByX.Insert(low, item);
        }

        public double TopMm => Items.Count == 0 ? 0 : Items.Max(p => p.Y + p.V.H);
    }

    private sealed class Outcome
    {
        public readonly List<Sheet> Sheets = new();
        public int Unplaced;
        public bool Interrupted;
    }

    private static bool Better(Outcome a, Outcome b, bool primaryX)
    {
        if (a.Unplaced != b.Unplaced) return a.Unplaced < b.Unplaced;
        if (a.Sheets.Count != b.Sheets.Count) return a.Sheets.Count < b.Sheets.Count;
        double ap = UsedExtent(a, primaryX), bp = UsedExtent(b, primaryX);
        if (Math.Abs(ap - bp) > 1e-6) return ap < bp;
        double ass = UsedExtent(a, !primaryX), bss = UsedExtent(b, !primaryX);
        if (Math.Abs(ass - bss) > 1e-6) return ass < bss;
        return OccupiedArea(a) > OccupiedArea(b) + 1e-6;
    }

    private static double UsedExtent(Outcome outcome, bool xAxis) => outcome.Sheets.Sum(sh =>
        sh.Items.Count == 0 ? 0 : sh.Items.Max(p => xAxis ? p.X + p.V.W : p.Y + p.V.H));

    private static double OccupiedArea(Outcome outcome) => outcome.Sheets.Sum(sh =>
        sh.Items.Sum(p => p.V.Loops.Area));

    private static bool IsStopped(NestOptions opt, Stopwatch sw) =>
        opt.Cancel.IsCancellationRequested || sw.Elapsed.TotalSeconds >= Math.Max(0.05, opt.MaxSeconds);

    private static string StopMessage(NestOptions opt) =>
        opt.Cancel.IsCancellationRequested ? "đã nhận yêu cầu hủy" : "đã hết thời gian nesting";

    private static bool GoalReached(Outcome outcome, double usableSheetArea, double targetPercent) =>
        targetPercent > 0 && outcome.Unplaced == 0 && outcome.Sheets.Count > 0 &&
        OccupiedArea(outcome) / (usableSheetArea * outcome.Sheets.Count) * 100 >= targetPercent;

    // ---------- Chạy ----------

    public static NestResult Run(IReadOnlyList<PartDefinition> parts, SheetDefinition sheet, NestOptions opt)
    {
        var result = new NestResult();
        var sw = Stopwatch.StartNew();
        double maxSeconds = Math.Clamp(double.IsFinite(opt.MaxSeconds) ? opt.MaxSeconds : 30, 0.05, 3600);
        if (opt.SearchMode == NestSearchMode.Fast)
            maxSeconds = Math.Min(maxSeconds, NestOptions.FastMaxSeconds);
        opt.MaxSeconds = maxSeconds;
        double target = Math.Clamp(double.IsFinite(opt.TargetUtilizationPercent) ? opt.TargetUtilizationPercent : 0, 0, 100);
        var types = new List<PartType>();
        double step = opt.RotationStep > 0 ? opt.RotationStep : 90;
        for (int partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var p = parts[partIndex];
            if (IsStopped(opt, sw))
            {
                result.Unplaced.Clear();
                foreach (var pending in parts.Where(pending => pending.Quantity > 0))
                    result.Unplaced.Add($"{pending.Name}: chưa xử lý vì {StopMessage(opt)}.");
                result.Strategy = opt.Cancel.IsCancellationRequested ? "Đã hủy nesting." : "Đã dừng do giới hạn thời gian.";
                return result;
            }
            if (p.Quantity <= 0) continue;

            var t = new PartType
            {
                Name = p.Name,
                Quantity = p.Quantity,
                HasShape = p.Shape != null,
                CanRotate = opt.AllowRotation && p.CanRotate,
                Base = p.Shape ?? ShapeOps.RectShape(p.Name, p.Width, p.Height),
                W = p.Width,
                H = p.Height
            };
            var baseLoops = ShapeOps.Build(t.Base);
            if (!baseLoops.Reliable)
            {
                result.Unplaced.Add($"{p.Name}: contour không hợp lệ — {baseLoops.Problem ?? "không tìm thấy đường biên kín"}.");
                result.Notes.Add("Nesting bị từ chối; không dùng hộp bao thay cho contour lỗi.");
                result.Strategy = "Không thể nesting: contour không hợp lệ.";
                return result;
            }
            t.BaseLoops = baseLoops;
            t.Area = baseLoops.Area > 0 ? baseLoops.Area : p.Width * p.Height;
            if (t.HasShape && baseLoops.OpenChains > 0)
                result.Notes.Add($"{p.Name}: bỏ qua {baseLoops.OpenChains} đường hở phụ; contour ngoài kín và hợp lệ.");

            foreach (double angle in RequestedAngles(step, t.CanRotate))
                if (t.HasShape || IsOrthogonal(angle))
                    (IsOrthogonal(angle) ? t.PrimaryAngles : t.FineAngles).Add(angle);
            types.Add(t);
        }

        if (types.Count == 0) return result;
        double c = ChooseCellSize(sheet, types);
        opt.Diagnostics?.RecordRasterCellSize(c);
        double colsValue = Math.Ceiling(sheet.UsableWidth / c - 1e-9);
        double rowsValue = Math.Ceiling(sheet.UsableHeight / c - 1e-9);
        double spacingCells = sheet.Spacing > 1e-9 ? Math.Ceiling(sheet.Spacing / c - 1e-9) : 0;
        if (!double.IsFinite(c) || !double.IsFinite(colsValue) || !double.IsFinite(rowsValue) ||
            !double.IsFinite(spacingCells) || colsValue < 1 || rowsValue < 1 ||
            colsValue > MaxRasterDimension || rowsValue > MaxRasterDimension ||
            colsValue * rowsValue > MaxRasterCells || spacingCells > MaxRasterDimension)
        {
            result.Strategy = "Không thể nesting: lưới raster vượt giới hạn tài nguyên.";
            result.Notes.Add($"Lưới tối đa là {MaxRasterDimension} × {MaxRasterDimension} ô và {MaxRasterCells:N0} ô tổng; hãy giảm kích thước tấm hoặc khoảng cách.");
            foreach (var part in parts.Where(part => part.Quantity > 0))
                result.Unplaced.Add($"{part.Name} ×{part.Quantity}: chưa xử lý vì lưới raster vượt giới hạn tài nguyên.");
            return result;
        }

        int s = (int)spacingCells;
        int cols = Math.Max(1, (int)colsValue);
        int rows = Math.Max(1, (int)rowsValue);
        if (!BuildVariants(types, sheet, c, s, cols, rows, primaryAngles: true, opt, sw))
        {
            result.Unplaced.Clear();
            foreach (var pending in parts.Where(pending => pending.Quantity > 0))
                result.Unplaced.Add($"{pending.Name}: chưa xử lý vì {StopMessage(opt)}.");
            result.Strategy = opt.Cancel.IsCancellationRequested ? "Đã hủy nesting." : "Đã dừng do giới hạn thời gian.";
            return result;
        }

        var all = new List<(int Type, int Inst)>();
        for (int i = 0; i < types.Count; i++)
            for (int k = 1; k <= types[i].Quantity; k++) all.Add((i, k));

        bool primaryX = opt.Direction == NestDirection.FillWidth ? sheet.Width >= sheet.Height : sheet.Width < sheet.Height;
        double fullSheetArea = sheet.Width * sheet.Height;
        bool hasFineAngles = types.Any(t => t.FineAngles.Count > 0);
        double originalMaxSeconds = opt.MaxSeconds;
        if (hasFineAngles)
            opt.MaxSeconds = Math.Min(originalMaxSeconds, sw.Elapsed.TotalSeconds + Math.Max(0.05, originalMaxSeconds * 0.2));
        var primaryOrder = all.OrderByDescending(item => types[item.Type].Area)
            .ThenBy(item => item.Type).ThenBy(item => item.Inst).ToList();
        var layout = SearchLayouts(types, all, sheet, c, s, rows, cols, primaryX, target, fullSheetArea,
            opt, sw, null, primaryOrder, "", 0, hasFineAngles ? 2 : 1,
            runOrderHeuristics: !hasFineAngles, runLocalSearch: !hasFineAngles);
        opt.MaxSeconds = originalMaxSeconds;

        if (hasFineAngles && layout.Best is { } primaryBest && !GoalReached(primaryBest, fullSheetArea, target) &&
            !IsStopped(opt, sw) && BuildVariants(types, sheet, c, s, cols, rows, primaryAngles: false, opt, sw))
        {
            layout = SearchLayouts(types, all, sheet, c, s, rows, cols, primaryX, target, fullSheetArea,
                opt, sw, layout.Best, layout.BestOrder, layout.BestName, 1, 2,
                runOrderHeuristics: true, runLocalSearch: true);
        }

        Outcome? best = layout.Best;
        List<(int Type, int Inst)> bestOrder = layout.BestOrder;
        string bestName = layout.BestName;
        if (best == null)
        {
            best = new Outcome { Interrupted = true, Unplaced = all.Count };
            bestName = opt.Cancel.IsCancellationRequested
                ? "đã hủy trước khi thử được phương án"
                : "không còn thời gian để thử phương án";
        }
        if (sheet.Spacing <= 1e-9)
            foreach (var packedSheet in best.Sheets)
                CompactZeroSpacing(packedSheet, sheet, opt, sw, opt.Diagnostics);

        int idx = 1;
        foreach (var sh in best.Sheets)
        {
            var sr = new SheetResult { Index = idx++, Width = sheet.Width, Height = sheet.Height };
            foreach (var p in sh.Items)
            {
                var t = types[p.Type];
                sr.Placements.Add(new Placement(t.Name, p.Instance, p.X, p.Y, p.V.W, p.V.H,
                    Rotated: false, Shape: t.HasShape ? p.V.Shape : null, AngleDeg: p.V.Angle, Area: t.Area));
            }
            result.Sheets.Add(sr);
        }
        if (best.Unplaced > 0)
            result.Unplaced.Add($"{best.Unplaced} chi tiết chưa đặt được trước khi dừng (kiểm tra kích thước, khoảng cách, giới hạn thời gian hoặc yêu cầu hủy).");

        string directionLabel = opt.Direction == NestDirection.FillWidth ? "ưu tiên lấp theo chiều rộng, giảm chiều dài dùng" : "ưu tiên lấp theo chiều dài, giảm bề rộng dùng";
        result.Strategy = $"hình dạng thật · {directionLabel} · {bestName}";
        result.Notes.Add($"Nesting hình dạng thật: ô lưới tự chọn {c:0.##} mm; khe giữa Part {sheet.Spacing:0.##} mm; lề mép {sheet.Margin:0.##} mm.");
        if (target > 0) result.Notes.Add($"Mục tiêu hiệu suất: {target:0.#}%{(best.Unplaced == 0 && GoalReached(best, fullSheetArea, target) ? " (đạt, dừng sớm)" : " (chưa đạt hoặc hết thời gian)")}.");
        if (IsStopped(opt, sw)) result.Notes.Add($"Đã {StopMessage(opt)}; trả về phương án tốt nhất tìm được.");
        return result;
    }

    private static double ChooseCellSize(SheetDefinition sheet, IReadOnlyList<PartType> types)
    {
        double sheetScale = Math.Max(1.0,
            Math.Ceiling(Math.Max(sheet.UsableWidth, sheet.UsableHeight) / 1250.0 * 2) / 2);
        double minPartDimension = double.PositiveInfinity;
        double rasterWork = 0;
        foreach (var type in types)
        {
            minPartDimension = Math.Min(minPartDimension, Math.Min(type.W, type.H));
            int vertices = type.BaseLoops.Loops.Sum(loop => loop.X.Length);
            int angleCount = type.PrimaryAngles.Count + type.FineAngles.Count;
            rasterWork += (double)vertices * type.H * angleCount;
        }

        double featureScale = minPartDimension / 8;
        double spacingScale = sheet.Spacing > 1e-9 ? sheet.Spacing / 2 : double.PositiveInfinity;
        // Bound extra grid work unless part features or spacing are small relative to the sheet scale.
        double maxGridGrowth = minPartDimension < 4 * sheetScale || sheet.Spacing < sheetScale / 2
            ? MaxAdaptiveGridGrowth
            : 1.0;
        double minimumRefinedCell = sheetScale / Math.Sqrt(maxGridGrowth);
        double desired = Math.Max(minimumRefinedCell,
            Math.Min(sheetScale, Math.Min(featureScale, spacingScale)));
        double complexityScale = Math.Min(sheetScale * 2, rasterWork / RasterScanWorkBudget);
        double resourceScale = Math.Max(
            Math.Max(sheet.UsableWidth / MaxRasterDimension, sheet.UsableHeight / MaxRasterDimension),
            Math.Sqrt(sheet.UsableWidth * sheet.UsableHeight / MaxRasterCells));
        return Math.Max(resourceScale, Math.Max(desired, complexityScale));
    }

    private static List<double> RequestedAngles(double step, bool canRotate)
    {
        var angles = new List<double> { 0 };
        if (canRotate)
            for (double angle = step; angle < 360 - 1e-9; angle += step)
                angles.Add(angle);
        return angles;
    }

    private static bool IsOrthogonal(double angle)
    {
        double nearestQuarterTurn = Math.Round(angle / 90) * 90;
        return Math.Abs(angle - nearestQuarterTurn) < 1e-9;
    }

    private static bool BuildVariants(IReadOnlyList<PartType> types, SheetDefinition sheet,
        double cellSize, int spacingCells, int cols, int rows, bool primaryAngles, NestOptions opt, Stopwatch sw)
    {
        foreach (var type in types)
        {
            var angles = primaryAngles ? type.PrimaryAngles : type.FineAngles;
            foreach (double angle in angles)
            {
                if (IsStopped(opt, sw)) return false;
                var variant = BuildVariant(type, angle, cellSize, spacingCells, opt, sw, opt.Diagnostics);
                if (variant == null) continue;
                variant.HasContour = type.HasShape;
                if (variant.Wc > cols || variant.Hc > rows ||
                    variant.W > sheet.UsableWidth + 1e-6 || variant.H > sheet.UsableHeight + 1e-6)
                    continue;
                if (type.MaskKeys.Add(MaskKey(variant)))
                {
                    type.Variants.Add(variant);
                    opt.Diagnostics?.RecordVariantBuilt(primaryAngles);
                }
            }
        }
        return !IsStopped(opt, sw);
    }

    private static LayoutSearchResult SearchLayouts(List<PartType> types, List<(int Type, int Inst)> all,
        SheetDefinition sheet, double cellSize, int spacingCells, int rows, int cols, bool primaryX,
        double target, double fullSheetArea, NestOptions opt, Stopwatch sw, Outcome? incumbent,
        List<(int Type, int Inst)> incumbentOrder, string incumbentName, int phaseIndex, int phaseCount,
        bool runOrderHeuristics, bool runLocalSearch)
    {
        var keys = new (string Name, Func<PartType, double> Key)[]
        {
            ("diện tích hình thật", type => type.Area),
            ("cạnh dài", type => Math.Max(type.W, type.H)),
            ("cạnh ngắn", type => Math.Min(type.W, type.H)),
            ("chu vi", type => type.W + type.H),
        };
        List<(string Name, List<(int Type, int Inst)> Order)> keyRuns = runOrderHeuristics
            ? keys.Select(key => (
                key.Name,
                Order: all.OrderByDescending(item => key.Key(types[item.Type]))
                    .ThenBy(item => item.Type).ThenBy(item => item.Inst).ToList()
            )).ToList()
            : [("thứ tự tốt nhất hiện tại", incumbentOrder)];
        int searchIterations = opt.SearchMode switch
        {
            NestSearchMode.Fast => 0,
            NestSearchMode.Quality => 12,
            _ => 6
        };

        Outcome? best = incumbent;
        List<(int Type, int Inst)> bestOrder = incumbentOrder;
        string bestName = incumbentName;
        var syncLock = new object();
        bool earlyStop = false;
        int completedLayouts = 0;
        int progressSteps = Math.Max(1, keyRuns.Count + (runLocalSearch ? searchIterations : 0));
        int totalProgressSteps = progressSteps * phaseCount;

        Parallel.ForEach(keyRuns, runItem =>
        {
            if (Volatile.Read(ref earlyStop) || IsStopped(opt, sw)) return;
            var outcome = Pack(runItem.Order, types, sheet, cellSize, spacingCells, rows, cols, primaryX, opt, sw,
                fraction => opt.ReportProgress?.Invoke(
                    0.9 * (phaseIndex * progressSteps + Volatile.Read(ref completedLayouts) + fraction) / totalProgressSteps));
            lock (syncLock)
            {
                if (best == null || Better(outcome, best, primaryX))
                {
                    best = outcome;
                    bestOrder = runItem.Order;
                    bestName = $"sắp theo {runItem.Name}";
                }
                opt.Report?.Invoke($"Lấp {(opt.Direction == NestDirection.FillWidth ? "theo chiều rộng" : "theo chiều dài")}: {outcome.Sheets.Count} tấm, còn {outcome.Unplaced} chi tiết chưa đặt.");
                Interlocked.Increment(ref completedLayouts);
                opt.ReportProgress?.Invoke(
                    0.9 * (phaseIndex * progressSteps + completedLayouts) / totalProgressSteps);
                if (GoalReached(outcome, fullSheetArea, target))
                {
                    best = outcome;
                    bestOrder = runItem.Order;
                    bestName += " · đã đạt mục tiêu hiệu suất";
                    Volatile.Write(ref earlyStop, true);
                }
                if (outcome.Interrupted) Volatile.Write(ref earlyStop, true);
            }
        });

        if (runLocalSearch && best != null && (!best.Interrupted || phaseIndex > 0) && !IsStopped(opt, sw) &&
            !GoalReached(best, fullSheetArea, target))
        {
            var rng = new Random(12345);
            for (int iteration = 0; iteration < searchIterations && !IsStopped(opt, sw); iteration++)
            {
                var candidate = new List<(int Type, int Inst)>(bestOrder);
                int moves = rng.Next(1, 4);
                for (int move = 0; move < moves && candidate.Count > 1; move++)
                {
                    int i = rng.Next(candidate.Count), j = rng.Next(candidate.Count);
                    if (rng.Next(2) == 0) (candidate[i], candidate[j]) = (candidate[j], candidate[i]);
                    else { var item = candidate[i]; candidate.RemoveAt(i); candidate.Insert(j, item); }
                }
                var outcome = Pack(candidate, types, sheet, cellSize, spacingCells, rows, cols, primaryX, opt, sw,
                    fraction => opt.ReportProgress?.Invoke(
                        0.9 * (phaseIndex * progressSteps + keyRuns.Count + iteration + fraction) / totalProgressSteps));
                if (Better(outcome, best, primaryX))
                {
                    best = outcome;
                    bestOrder = candidate;
                    bestName = $"tìm kiếm thứ tự (lượt {iteration + 1})";
                }
                double utilization = OccupiedArea(outcome) / (fullSheetArea * Math.Max(1, outcome.Sheets.Count)) * 100;
                opt.Report?.Invoke($"Tìm kiếm {iteration + 1}/{searchIterations}: {outcome.Sheets.Count} tấm, hiệu suất {utilization:0.0}% (mục tiêu {target:0.#}%).");
                opt.ReportProgress?.Invoke(
                    0.9 * (phaseIndex * progressSteps + keyRuns.Count + iteration + 1) / totalProgressSteps);
                if (GoalReached(outcome, fullSheetArea, target))
                {
                    best = outcome;
                    bestOrder = candidate;
                    bestName = $"đã đạt mục tiêu hiệu suất ở lượt {iteration + 1}";
                    break;
                }
                if (outcome.Interrupted) break;
            }
        }

        return new LayoutSearchResult(best, bestOrder, bestName);
    }

    // ---------- Mặt nạ ----------

    private static string MaskKey(Variant v)
    {
        var h = new HashCode();
        h.Add(v.Wc); h.Add(v.Hc);
        foreach (var row in v.Cover)
        {
            h.Add(row.Length);
            foreach (var (st, len) in row) { h.Add(st); h.Add(len); }
        }
        return h.ToHashCode().ToString();
    }

    private static Variant? BuildVariant(PartType t, double angle, double c, int s, NestOptions opt, Stopwatch sw,
        NestDiagnostics? diagnostics)
    {
        long geometryStart = diagnostics == null ? 0 : Stopwatch.GetTimestamp();
        var shape = ShapeOps.Rotate(t.Base, angle);
        var loops = ShapeOps.Build(shape);
        if (diagnostics != null)
            diagnostics.AddVariantGeometryTicks(Stopwatch.GetTimestamp() - geometryStart);
        if (IsStopped(opt, sw)) return null;
        if (!loops.Reliable || loops.Area <= 0) return null;
        long rasterizationStart = diagnostics == null ? 0 : Stopwatch.GetTimestamp();
        try
        {
            return BuildVariantMask(shape, loops, angle, c, s, opt, sw);
        }
        finally
        {
            if (diagnostics != null)
                diagnostics.AddVariantRasterizationTicks(Stopwatch.GetTimestamp() - rasterizationStart);
        }
    }

    private static Variant? BuildVariantMask(DxfShape shape, LoopSet verify, double angle, double c, int s,
        NestOptions opt, Stopwatch sw)
    {
        double w = shape.Width, h = shape.Height;
        double wcValue = Math.Ceiling(w / c - 1e-9);
        double hcValue = Math.Ceiling(h / c - 1e-9);
        if (!double.IsFinite(wcValue) || !double.IsFinite(hcValue) ||
            wcValue < 1 || hcValue < 1 ||
            wcValue > MaxRasterDimension || hcValue > MaxRasterDimension ||
            wcValue * hcValue > MaxRasterCells)
            return null;
        int wc = Math.Max(1, (int)wcValue);
        int hc = Math.Max(1, (int)hcValue);
        var cell = new bool[hc][];
        for (int j = 0; j < hc; j++)
        {
            if ((j & 255) == 0 && IsStopped(opt, sw)) return null;
            cell[j] = new bool[wc];
        }

        // Tô vùng trong (even-odd) theo tâm ô
        var cross = new List<double>();
        for (int j = 0; j < hc; j++)
        {
            if ((j & 31) == 0 && IsStopped(opt, sw)) return null;
            double yc = (j + 0.5) * c;
            cross.Clear();
            foreach (var (xs, ys) in verify.Loops)
            {
                for (int i = 0, k = xs.Length - 1; i < xs.Length; k = i++)
                {
                    if ((i & 1023) == 0 && IsStopped(opt, sw)) return null;
                    double y0 = ys[k], y1 = ys[i];
                    if ((y0 <= yc) != (y1 <= yc)) cross.Add(xs[k] + (yc - y0) * (xs[i] - xs[k]) / (y1 - y0));
                }
            }
            cross.Sort();
            for (int k = 0; k + 1 < cross.Count; k += 2)
            {
                int i0 = Math.Max(0, (int)Math.Ceiling(cross[k] / c - 0.5));
                int i1 = Math.Min(wc - 1, (int)Math.Ceiling(cross[k + 1] / c - 0.5) - 1);
                for (int i = i0; i <= i1; i++) cell[j][i] = true;
            }
        }

        // Đánh dấu mọi ô mà đường biên đi qua (bảo thủ)
        foreach (var (xs, ys) in verify.Loops)
            for (int i = 0, k = xs.Length - 1; i < xs.Length; k = i++)
            {
                if ((i & 255) == 0 && IsStopped(opt, sw)) return null;
                MarkEdge(cell, wc, hc, xs[k] / c, ys[k] / c, xs[i] / c, ys[i] / c);
            }

        var cover = new (int, int)[hc][];
        for (int j = 0; j < hc; j++)
        {
            if ((j & 255) == 0 && IsStopped(opt, sw)) return null;
            cover[j] = Runs(cell[j]);
        }

        // Giãn mặt nạ thêm s ô (đĩa tròn) để làm khe hở
        int dh = hc + 2 * s;
        var ivs = new List<(int A, int B)>?[dh];
        var half = new int[s + 1];
        for (int d = 0; d <= s; d++) half[d] = (int)Math.Floor(Math.Sqrt(s * s - d * d) + 1e-9);

        for (int j = 0; j < hc; j++)
        {
            if ((j & 31) == 0 && IsStopped(opt, sw)) return null;
            if (cover[j].Length == 0) continue;
            for (int dy = -s; dy <= s; dy++)
            {
                if ((dy & 255) == 0 && IsStopped(opt, sw)) return null;
                int jj = j + dy + s;
                int hw = half[Math.Abs(dy)];
                var lst = ivs[jj] ??= new List<(int, int)>();
                foreach (var (st, len) in cover[j]) lst.Add((st + s - hw, st + s + len + hw));
            }
        }

        var dil = new (int, int)[dh][];
        for (int j = 0; j < dh; j++) dil[j] = MergeIntervals(ivs[j]);

        return new Variant
        {
            Angle = angle,
            Shape = shape,
            Loops = verify,
            W = w,
            H = h,
            Wc = wc,
            Hc = hc,
            S = s,
            Cover = cover,
            Dil = dil
        };
    }

    private static (int, int)[] Runs(bool[] row)
    {
        var list = new List<(int, int)>();
        int i = 0;
        while (i < row.Length)
        {
            if (!row[i]) { i++; continue; }
            int j = i;
            while (j < row.Length && row[j]) j++;
            list.Add((i, j - i));
            i = j;
        }
        return list.ToArray();
    }

    private static (int, int)[] MergeIntervals(List<(int A, int B)>? lst)
    {
        if (lst == null || lst.Count == 0) return Array.Empty<(int, int)>();
        lst.Sort();
        var res = new List<(int, int)>();
        int a = lst[0].A, b = lst[0].B;
        for (int i = 1; i < lst.Count; i++)
        {
            if (lst[i].A <= b) b = Math.Max(b, lst[i].B);
            else { res.Add((a, b - a)); a = lst[i].A; b = lst[i].B; }
        }
        res.Add((a, b - a));
        return res.ToArray();
    }

    /// <summary>Duyệt voxel (Amanatides-Woo): đánh dấu mọi ô mà đoạn thẳng đi qua.</summary>
    private static void MarkEdge(bool[][] cell, int wc, int hc, double x0, double y0, double x1, double y1)
    {
        void Mark(int ix, int iy)
        {
            if (ix >= 0 && ix < wc && iy >= 0 && iy < hc) cell[iy][ix] = true;
        }

        int cx = (int)Math.Floor(x0), cy = (int)Math.Floor(y0);
        int ex = (int)Math.Floor(x1), ey = (int)Math.Floor(y1);
        Mark(cx, cy);
        double dx = x1 - x0, dy = y1 - y0;
        int sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1;
        double tdx = dx != 0 ? Math.Abs(1.0 / dx) : double.PositiveInfinity;
        double tdy = dy != 0 ? Math.Abs(1.0 / dy) : double.PositiveInfinity;
        double tmx = dx > 0 ? (cx + 1 - x0) / dx : dx < 0 ? (x0 - cx) / -dx : double.PositiveInfinity;
        double tmy = dy > 0 ? (cy + 1 - y0) / dy : dy < 0 ? (y0 - cy) / -dy : double.PositiveInfinity;

        int guard = Math.Abs(ex - cx) + Math.Abs(ey - cy) + 4;
        while ((cx != ex || cy != ey) && guard-- > 0)
        {
            if (tmx < tmy) { tmx += tdx; cx += sx; }
            else if (tmy < tmx) { tmy += tdy; cy += sy; }
            else
            {
                Mark(cx + sx, cy); Mark(cx, cy + sy); // đi qua đúng góc ô
                tmx += tdx; tmy += tdy; cx += sx; cy += sy;
            }
            Mark(cx, cy);
        }
    }

    // ---------- Đặt chi tiết ----------

    private static Outcome Pack(List<(int Type, int Inst)> order, List<PartType> types, SheetDefinition sheet,
        double c, int s, int rows, int cols, bool primaryX, NestOptions opt, Stopwatch sw,
        Action<double>? reportProgress = null)
    {
        var oc = new Outcome();
        var impossible = new HashSet<int>();
        var feasibleBuf = new ulong[(cols + 63) / 64 + 4];
        int progressInterval = Math.Max(1, order.Count / 100);

        for (int itemIndex = 0; itemIndex < order.Count; itemIndex++)
        {
            if (itemIndex % progressInterval == 0)
                reportProgress?.Invoke((double)itemIndex / Math.Max(1, order.Count));
            if (IsStopped(opt, sw))
            {
                oc.Unplaced += order.Count - itemIndex;
                oc.Interrupted = true;
                break;
            }
            var (ti, inst) = order[itemIndex];
            if (impossible.Contains(ti)) { oc.Unplaced++; continue; }

            bool done = false;
            foreach (var sh in oc.Sheets)
            {
                if (IsStopped(opt, sw)) break;
                if (sh.Failed.Contains(ti)) continue;
                if (TryPlace(sh, types[ti], ti, inst, sheet, c, primaryX, opt, sw, feasibleBuf)) { done = true; break; }
                if (IsStopped(opt, sw)) break;
                sh.Failed.Add(ti);
            }
            if (IsStopped(opt, sw))
            {
                oc.Unplaced += order.Count - itemIndex;
                oc.Interrupted = true;
                break;
            }
            if (done) continue;

            var newSheet = new Sheet(rows, cols);
            if (TryPlace(newSheet, types[ti], ti, inst, sheet, c, primaryX, opt, sw, feasibleBuf)) oc.Sheets.Add(newSheet);
            else if (IsStopped(opt, sw))
            {
                oc.Unplaced += order.Count - itemIndex;
                oc.Interrupted = true;
                break;
            }
            else { impossible.Add(ti); oc.Unplaced++; }
        }
        reportProgress?.Invoke(1);
        return oc;
    }

    private static bool TryPlace(Sheet sh, PartType t, int ti, int inst, SheetDefinition sheet, double c,
        bool primaryX, NestOptions opt, Stopwatch sw, ulong[] feasible)
    {
        int bestPrimary = int.MaxValue, bestSecondary = int.MaxValue, bestX = 0, bestY = 0;
        Variant? bestV = null;
        foreach (var v in t.Variants)
        {
            if (IsStopped(opt, sw)) break;
            int x, y;
            bool found;
            if (opt.Diagnostics is { } diagnostics)
            {
                long searchStart = Stopwatch.GetTimestamp();
                try
                {
                    found = Search(sh, v, sheet, c, primaryX, opt, sw, feasible, diagnostics, out x, out y);
                }
                finally
                {
                    diagnostics.AddSearchTicks(Stopwatch.GetTimestamp() - searchStart);
                }
            }
            else
                found = Search(sh, v, sheet, c, primaryX, opt, sw, feasible, null, out x, out y);
            if (!found) continue;
            int primary = primaryX ? x + v.Wc : y + v.Hc;
            int secondary = primaryX ? y + v.Hc : x + v.Wc;
            if (primary < bestPrimary || (primary == bestPrimary && secondary < bestSecondary))
            {
                bestPrimary = primary; bestSecondary = secondary;
                bestX = x; bestY = y; bestV = v;
            }
        }

        if (bestV == null) return false;
        Stamp(sh, bestV, bestX, bestY);
        sh.Add(new Placed(ti, inst, bestV, sheet.Margin + bestX * c, sheet.Margin + bestY * c));
        return true;
    }

    /// <summary>Tìm vị trí hợp lệ theo trục ưu tiên: X trước hoặc Y trước.</summary>
    private static bool Search(Sheet sh, Variant v, SheetDefinition sheet, double c, bool primaryX,
        NestOptions opt, Stopwatch sw, ulong[] feasible, NestDiagnostics? diagnostics, out int ox, out int oy)
    {
        ox = oy = 0;
        int maxX = Math.Min(sh.Cols - v.Wc, (int)Math.Floor((sheet.UsableWidth - v.W + 1e-6) / c));
        int maxY = Math.Min(sh.Rows - v.Hc, (int)Math.Floor((sheet.UsableHeight - v.H + 1e-6) / c));
        if (maxX < 0 || maxY < 0) return false;
        int fw = maxX / 64 + 1;
        int rejects = 0, bestX = int.MaxValue, bestY = int.MaxValue;

        for (int y = 0; y <= maxY; y++)
        {
            if (IsStopped(opt, sw)) break;
            for (int i = 0; i < fw; i++) feasible[i] = ulong.MaxValue;
            int tail = maxX + 1 - (fw - 1) * 64;
            if (tail < 64) feasible[fw - 1] = (1UL << tail) - 1;

            bool alive = true;
            for (int r = 0; r < v.Hc && alive; r++)
            {
                var runs = v.Cover[r];
                int row = y + r;
                foreach (var (st, len) in runs)
                {
                    int j = 31 - BitOperations.LeadingZeroCount((uint)len);
                    var occupied = GetP(sh, row, j);
                    AndNotShift(feasible, fw, occupied, st);
                    int width = 1 << j;
                    if (len != width) AndNotShift(feasible, fw, occupied, st + len - width);
                }
                bool any = false;
                for (int fi = 0; fi < fw; fi++)
                {
                    if (feasible[fi] != 0) { any = true; break; }
                }
                alive = any;
            }
            if (!alive) continue;

            bool foundAtRow = false;
            for (int wi = 0; wi < fw && !foundAtRow; wi++)
            {
                ulong word = feasible[wi];
                while (word != 0)
                {
                    int tz = BitOperations.TrailingZeroCount(word);
                    word &= word - 1;
                    int x = wi * 64 + tz;
                    if (primaryX && x >= bestX) break; // row đang đi lên; cùng X không thể thắng Y đã có
                    if (Verify(sh, v, sheet, c, x, y, diagnostics))
                    {
                        if (!primaryX) { ox = x; oy = y; return true; }
                        bestX = x; bestY = y; foundAtRow = true;
                        break; // bitset đã duyệt X tăng dần nên đây là X nhỏ nhất của hàng
                    }
                    if (++rejects > MaxRejects)
                    {
                        if (bestX != int.MaxValue) { ox = bestX; oy = bestY; return true; }
                        return false;
                    }
                    if (IsStopped(opt, sw)) break;
                }
            }
            if (primaryX && bestX == 0 && bestY == 0) break;
        }
        if (primaryX && bestX != int.MaxValue) { ox = bestX; oy = bestY; return true; }
        return false;
    }

    /// <summary>Xác minh chính xác: trong vùng cho phép và đúng khe hở với mọi chi tiết gần đó.</summary>
    private static bool Verify(Sheet sh, Variant v, SheetDefinition sheet, double c, int x, int y,
        NestDiagnostics? diagnostics)
    {
        long verifyStart = diagnostics == null ? 0 : Stopwatch.GetTimestamp();
        try
        {
            double X = sheet.Margin + x * c, Y = sheet.Margin + y * c;
            if (X + v.W > sheet.Width - sheet.Margin + 1e-6 || Y + v.H > sheet.Height - sheet.Margin + 1e-6) return false;

            double contourGap = Math.Max(0, sheet.Spacing - GapTol);
            double minX = X - sh.MaxPlacedWidth - sheet.Spacing;
            double maxX = X + v.W + sheet.Spacing;
            int start = LowerBoundByX(sh.ItemsByX, minX);
            int end = LowerBoundByX(sh.ItemsByX, maxX);
            diagnostics?.RecordVerifyIndexRange(sh.Items.Count, end - start);
            for (int i = start; i < end; i++)
            {
                var p = sh.ItemsByX[i];
                diagnostics?.RecordVerifyPlacementCheck();
                bool separatedBySpacing =
                    X >= p.X + p.V.W + sheet.Spacing || p.X >= X + v.W + sheet.Spacing ||
                    Y >= p.Y + p.V.H + sheet.Spacing || p.Y >= Y + v.H + sheet.Spacing;
                if (separatedBySpacing)
                {
                    diagnostics?.RecordVerifyBoundsRejected();
                    continue;
                }
                if (!v.HasContour && !p.V.HasContour) return false;
                if (MeasuredConflict(v.Loops, X, Y, p.V.Loops, p.X, p.Y, contourGap, diagnostics)) return false;
            }
            return true;
        }
        finally
        {
            if (diagnostics != null)
                diagnostics.AddVerifyTicks(Stopwatch.GetTimestamp() - verifyStart);
        }
    }

    private static int LowerBoundByX(List<Placed> items, double x)
    {
        int low = 0, high = items.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (items[middle].X < x) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private static bool MeasuredConflict(LoopSet a, double ax, double ay, LoopSet b, double bx, double by,
        double minGap, NestDiagnostics? diagnostics)
    {
        if (diagnostics == null) return ShapeOps.Conflict(a, ax, ay, b, bx, by, minGap);

        long conflictStart = Stopwatch.GetTimestamp();
        try
        {
            return ShapeOps.Conflict(a, ax, ay, b, bx, by, minGap);
        }
        finally
        {
            diagnostics.RecordConflict(Stopwatch.GetTimestamp() - conflictStart);
        }
    }

    private static void CompactZeroSpacing(Sheet sh, SheetDefinition sheet, NestOptions opt, Stopwatch sw,
        NestDiagnostics? diagnostics)
    {
        for (int i = 0; i < sh.Items.Count && !IsStopped(opt, sw); i++)
        {
            var item = sh.Items[i];
            double leftTravel = Math.Max(0, item.X - sheet.Margin);
            double leftMove = FindSafeTravel(sh, i, item, -leftTravel, 0, sheet, opt, sw, diagnostics);
            if (leftMove > 1e-7)
            {
                item = item with { X = item.X - leftMove };
                sh.Items[i] = item;
            }

            double downTravel = Math.Max(0, item.Y - sheet.Margin);
            double downMove = FindSafeTravel(sh, i, item, 0, -downTravel, sheet, opt, sw, diagnostics);
            if (downMove > 1e-7)
                sh.Items[i] = item with { Y = item.Y - downMove };
        }
    }

    private static double FindSafeTravel(Sheet sh, int movingIndex, Placed moving, double dx, double dy,
        SheetDefinition sheet, NestOptions opt, Stopwatch sw, NestDiagnostics? diagnostics)
    {
        double maxTravel = Math.Sqrt(dx * dx + dy * dy);
        if (maxTravel <= 1e-7) return 0;
        bool horizontal = Math.Abs(dx) > 1e-9;
        var exactNeighbors = new List<Placed>();
        double bound = maxTravel;
        for (int i = 0; i < sh.Items.Count; i++)
        {
            if (i == movingIndex) continue;
            var other = sh.Items[i];
            bool overlapsPerpendicular = horizontal
                ? moving.Y < other.Y + other.V.H && other.Y < moving.Y + moving.V.H
                : moving.X < other.X + other.V.W && other.X < moving.X + moving.V.W;
            if (!overlapsPerpendicular) continue;

            if (horizontal)
            {
                if (other.X + other.V.W <= moving.X)
                    bound = Math.Min(bound, moving.X - (other.X + other.V.W));
                else if (other.X < moving.X + moving.V.W && other.X + other.V.W > moving.X)
                    exactNeighbors.Add(other);
            }
            else
            {
                if (other.Y + other.V.H <= moving.Y)
                    bound = Math.Min(bound, moving.Y - (other.Y + other.V.H));
                else if (other.Y < moving.Y + moving.V.H && other.Y + other.V.H > moving.Y)
                    exactNeighbors.Add(other);
            }
        }

        bool CanTravel(double distance)
        {
            double x = moving.X + dx / maxTravel * distance;
            double y = moving.Y + dy / maxTravel * distance;
            if (x < sheet.Margin - 1e-6 || y < sheet.Margin - 1e-6 ||
                x + moving.V.W > sheet.Width - sheet.Margin + 1e-6 ||
                y + moving.V.H > sheet.Height - sheet.Margin + 1e-6)
                return false;

            foreach (var other in exactNeighbors)
            {
                if (x >= other.X + other.V.W || other.X >= x + moving.V.W ||
                    y >= other.Y + other.V.H || other.Y >= y + moving.V.H)
                    continue;
                if (MeasuredConflict(moving.V.Loops, x, y, other.V.Loops, other.X, other.Y, 0, diagnostics))
                    return false;
            }
            return true;
        }

        if (exactNeighbors.Count == 0) return bound < maxTravel ? Math.Max(0, bound - 5e-4) : bound;
        if (CanTravel(bound)) return bound < maxTravel ? Math.Max(0, bound - 5e-4) : bound;
        double valid = 0, invalid = bound;
        for (int i = 0; i < 24 && !IsStopped(opt, sw); i++)
        {
            double mid = (valid + invalid) / 2;
            if (CanTravel(mid)) valid = mid;
            else invalid = mid;
        }
        return Math.Max(0, valid - 5e-4);
    }

    // ---------- Phép toán bit ----------

    private static ulong[] GetP(Sheet sh, int row, int j)
    {
        var rowP = sh.P[row] ??= new ulong[12][];
        var rowV = sh.PVer[row] ??= new int[12];
        int ver = sh.RowVer[row];

        if (j == 0) return sh.Occ[row];
        if (rowP[j] != null && rowV[j] == ver) return rowP[j];

        var src = GetP(sh, row, j - 1);
        var dst = rowP[j] ?? new ulong[sh.Words];
        int n = 1 << (j - 1);
        int ws = n >> 6, bs = n & 63;
        int W = sh.Words;
        for (int i = 0; i < W; i++)
        {
            ulong v = i + ws < W ? src[i + ws] >> bs : 0;
            if (bs != 0 && i + ws + 1 < W) v |= src[i + ws + 1] << (64 - bs);
            dst[i] = src[i] | v;
        }
        rowP[j] = dst;
        rowV[j] = ver;
        return dst;
    }

    /// <summary>F[x] &amp;= ~P[x + shift] cho mọi bit x.</summary>
    private static void AndNotShift(ulong[] F, int fw, ulong[] P, int shift)
    {
        int ws = shift >> 6, bs = shift & 63;
        if (bs == 0)
        {
            for (int i = 0; i < fw; i++) F[i] &= ~P[i + ws];
        }
        else
        {
            for (int i = 0; i < fw; i++) F[i] &= ~((P[i + ws] >> bs) | (P[i + ws + 1] << (64 - bs)));
        }
    }

    /// <summary>Ghi mặt nạ đã giãn của chi tiết vào lưới tấm.</summary>
    private static void Stamp(Sheet sh, Variant v, int x, int y)
    {
        for (int i = 0; i < v.Dil.Length; i++)
        {
            int row = y - v.S + i;
            if (row < 0 || row >= sh.Rows) continue;
            var occ = sh.Occ[row];
            foreach (var (st, len) in v.Dil[i])
            {
                int a = Math.Max(0, x - v.S + st), b = Math.Min(sh.Cols, x - v.S + st + len);
                if (a < b) SetBits(occ, a, b);
            }
            sh.RowVer[row]++;
        }
    }

    private static void SetBits(ulong[] arr, int from, int to)
    {
        int wa = from >> 6, wb = (to - 1) >> 6;
        ulong ma = ulong.MaxValue << (from & 63);
        ulong mb = ulong.MaxValue >> (63 - ((to - 1) & 63));
        if (wa == wb) { arr[wa] |= ma & mb; return; }
        arr[wa] |= ma;
        for (int i = wa + 1; i < wb; i++) arr[i] = ulong.MaxValue;
        arr[wb] |= mb;
    }
}
