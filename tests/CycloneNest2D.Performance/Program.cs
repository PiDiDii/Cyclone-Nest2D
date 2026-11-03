using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CycloneNest2D.Core;

return PerformanceProgram.Run(args);

internal static class PerformanceProgram
{
    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    public static int Run(string[] args)
    {
        try
        {
            var config = BenchmarkConfig.Parse(args);
            if (config.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            var workloads = Workloads.Create(config);
            Console.WriteLine($"Cyclone Nest2D nesting benchmark · {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
            Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} · {Environment.OSVersion} · " +
                              $"{Environment.ProcessorCount} logical CPU · {Environment.Is64BitProcess switch { true => "64-bit", false => "32-bit" }}");
            Console.WriteLine($"Mode: {(config.Full ? "FULL" : "QUICK")} · {config.Repetitions} lần đo/profile · " +
                              $"ngân sách {config.MaxSeconds:0.##} giây/lần · {workloads.Count} workload(s)");
            Console.WriteLine("Các lượt benchmark chạy tuần tự; warm-up và validator không tính vào thời gian NestEngine.\n");
            if (config.Diagnostics)
                Console.WriteLine("Diagnostics bật: Search bao gồm Verify; Verify bao gồm Conflict (thời gian lồng nhau, không cộng trực tiếp).\n");

            var runs = new List<BenchmarkRun>();
            foreach (var workload in workloads)
            {
                foreach (var profile in BenchmarkProfile.All)
                {
                    Console.WriteLine($"Warm-up: {workload.Name} / {profile.Name}");
                    RunOnce(workload, profile, config, warmup: true);

                    for (int repetition = 1; repetition <= config.Repetitions; repetition++)
                    {
                        var run = RunOnce(workload, profile, config, warmup: false, repetition);
                        runs.Add(run);
                        PrintRun(run);
                    }
                }
            }

            var summaries = BenchmarkSummary.FromRuns(runs);
            PrintSummary(summaries);
            PrintInsights(summaries);

            if (summaries.Any(summary => summary.ValidationErrors > 0))
            {
                Console.Error.WriteLine("\nLỖI: Có kết quả nesting không vượt qua validator độc lập.");
                return 2;
            }

            if (config.OutputPrefix is not null)
            {
                string jsonPath = Path.GetFullPath(config.OutputPrefix + ".json");
                string csvPath = Path.GetFullPath(config.OutputPrefix + ".csv");
                Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
                var report = new BenchmarkReport
                {
                    GeneratedAt = DateTimeOffset.Now,
                    Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    OperatingSystem = Environment.OSVersion.ToString(),
                    ProcessorCount = Environment.ProcessorCount,
                    Is64BitProcess = Environment.Is64BitProcess,
                    Configuration = config,
                    Runs = runs,
                    Summary = summaries,
                    Insights = BuildInsights(summaries)
                };
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, ReportJson), new UTF8Encoding(false));
                File.WriteAllText(csvPath, CsvReport.Serialize(runs), new UTF8Encoding(false));
                Console.WriteLine($"\nĐã lưu báo cáo:\n  JSON: {jsonPath}\n  CSV:  {csvPath}");
            }

            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Tham số không hợp lệ: {ex.Message}\n");
            PrintHelp();
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Benchmark thất bại: {ex}");
            return 1;
        }
    }

    private static BenchmarkRun RunOnce(BenchmarkWorkload workload, BenchmarkProfile profile,
        BenchmarkConfig config, bool warmup, int repetition = 0)
    {
        var diagnostics = !warmup && config.Diagnostics ? new NestDiagnostics() : null;
        var options = new NestOptions
        {
            AllowRotation = true,
            Direction = NestDirection.FillWidth,
            RotationStep = profile.RotationStep,
            TargetUtilizationPercent = profile.TargetUtilizationPercent,
            MaxSeconds = warmup ? Math.Min(config.MaxSeconds, 0.25) : config.MaxSeconds,
            Diagnostics = diagnostics
        };

        if (warmup)
        {
            _ = NestEngine.Run(workload.Parts, workload.Sheet, options);
            return BenchmarkRun.Warmup;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        long startWorkingSet = process.WorkingSet64;
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        int[] gcBefore = Enumerable.Range(0, GC.MaxGeneration + 1).Select(GC.CollectionCount).ToArray();
        TimeSpan cpuBefore = process.TotalProcessorTime;

        var stopwatch = Stopwatch.StartNew();
        NestResult result = NestEngine.Run(workload.Parts, workload.Sheet, options);
        stopwatch.Stop();

        TimeSpan cpu = process.TotalProcessorTime - cpuBefore;
        long allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        int[] gcCollections = Enumerable.Range(0, GC.MaxGeneration + 1)
            .Select(generation => GC.CollectionCount(generation) - gcBefore[generation])
            .ToArray();
        process.Refresh();
        long endWorkingSet = process.WorkingSet64;

        var validationTimer = Stopwatch.StartNew();
        List<string> validationErrors = Validator.Check(result, workload.Sheet);
        validationTimer.Stop();
        if (validationErrors.Count > 0)
        {
            string placements = string.Join("; ", result.Sheets.SelectMany(sheetResult => sheetResult.Placements)
                .Where(placement => validationErrors.Any(error => error.Contains(
                    $"{placement.PartName} #{placement.Instance}", StringComparison.Ordinal)))
                .Select(placement => $"{placement.PartName} #{placement.Instance} at ({placement.X:R},{placement.Y:R}) " +
                                    $"{placement.Width:R}×{placement.Height:R}"));
            Console.Error.WriteLine($"Validator: {workload.Name} / {profile.Name} #{repetition}: " +
                                    string.Join(" | ", validationErrors.Take(3)) + $" [{placements}]");
        }

        int requested = workload.Parts.Sum(part => Math.Max(0, part.Quantity));
        int placed = result.Sheets.Sum(sheet => sheet.Placements.Count);
        double utilization = result.Utilization * 100;
        bool hitTimeBudget = result.Strategy.Equals("Đã dừng do giới hạn thời gian.", StringComparison.Ordinal) ||
            result.Notes.Any(note => note.Contains("hết thời gian nesting", StringComparison.OrdinalIgnoreCase));
        return new BenchmarkRun
        {
            Workload = workload.Name,
            Description = workload.Description,
            Profile = profile.Name,
            Strategy = result.Strategy,
            CompletionStatus = hitTimeBudget ? "time-budget" : placed == requested ? "complete" : "partial",
            StopReason = hitTimeBudget ? "time budget reached" : placed == requested ? "all instances placed" : "instances unplaced",
            Repetition = repetition,
            PartTypes = workload.Parts.Count,
            RequestedInstances = requested,
            SheetWidth = workload.Sheet.Width,
            SheetHeight = workload.Sheet.Height,
            Margin = workload.Sheet.Margin,
            Spacing = workload.Sheet.Spacing,
            RotationStep = profile.RotationStep,
            TargetUtilizationPercent = profile.TargetUtilizationPercent,
            TimeBudgetSeconds = config.MaxSeconds,
            DiagnosticsEnabled = diagnostics != null,
            VariantGeometryMilliseconds = diagnostics?.VariantGeometryMilliseconds ?? 0,
            VariantRasterizationMilliseconds = diagnostics?.VariantRasterizationMilliseconds ?? 0,
            RasterCellSizeMillimeters = diagnostics?.RasterCellSizeMillimeters ?? 0,
            PrimaryVariantsBuilt = diagnostics?.PrimaryVariantsBuilt ?? 0,
            FineVariantsBuilt = diagnostics?.FineVariantsBuilt ?? 0,
            SearchMilliseconds = diagnostics?.SearchMilliseconds ?? 0,
            VerifyMilliseconds = diagnostics?.VerifyMilliseconds ?? 0,
            VerifyIndexCulled = diagnostics?.VerifyIndexCulled ?? 0,
            VerifyPlacementChecks = diagnostics?.VerifyPlacementChecks ?? 0,
            VerifyBoundsRejected = diagnostics?.VerifyBoundsRejected ?? 0,
            ConflictMilliseconds = diagnostics?.ConflictMilliseconds ?? 0,
            ConflictCalls = diagnostics?.ConflictCalls ?? 0,
            NestingMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
            ValidationMilliseconds = validationTimer.Elapsed.TotalMilliseconds,
            EndToEndMilliseconds = stopwatch.Elapsed.TotalMilliseconds + validationTimer.Elapsed.TotalMilliseconds,
            CpuMilliseconds = cpu.TotalMilliseconds,
            AllocatedBytes = allocated,
            WorkingSetBeforeBytes = startWorkingSet,
            WorkingSetAfterBytes = endWorkingSet,
            Gen0Collections = gcCollections.ElementAtOrDefault(0),
            Gen1Collections = gcCollections.ElementAtOrDefault(1),
            Gen2Collections = gcCollections.ElementAtOrDefault(2),
            PlacedInstances = placed,
            UnplacedInstances = Math.Max(0, requested - placed),
            SheetCount = result.Sheets.Count,
            UtilizationPercent = utilization,
            CompletionPercent = requested == 0 ? 100 : placed * 100d / requested,
            ValidationErrors = validationErrors.Count,
            HitTimeBudget = hitTimeBudget,
            ResultSignature = Signature(result)
        };
    }

    private static string Signature(NestResult result)
    {
        var canonical = new StringBuilder();
        foreach (var sheet in result.Sheets.OrderBy(sheet => sheet.Index))
        {
            canonical.Append("S|").Append(sheet.Index).Append('|');
            foreach (var placement in sheet.Placements
                         .OrderBy(placement => placement.PartName, StringComparer.Ordinal)
                         .ThenBy(placement => placement.Instance)
                         .ThenBy(placement => placement.X)
                         .ThenBy(placement => placement.Y))
            {
                canonical.Append(placement.PartName).Append('|')
                    .Append(placement.Instance).Append('|')
                    .Append(placement.X.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(placement.Y.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(placement.Width.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(placement.Height.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(placement.AngleDeg.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void PrintRun(BenchmarkRun run)
    {
        Console.WriteLine(
            $"  {run.Workload,-22} {run.Profile,-15} #{run.Repetition}: " +
            $"nest {run.NestingMilliseconds,8:0.0} ms · verify {run.ValidationMilliseconds,7:0.0} ms · " +
            $"{run.PlacedInstances}/{run.RequestedInstances} · {run.SheetCount} tấm · " +
            $"eff {run.UtilizationPercent,5:0.0}% · alloc {FormatBytes(run.AllocatedBytes)}" +
            (run.DiagnosticsEnabled
                ? $" · var {run.VariantGeometryMilliseconds:0.0}+{run.VariantRasterizationMilliseconds:0.0} ms" +
                  $" · search {run.SearchMilliseconds:0.0} · verify {run.VerifyMilliseconds:0.0}" +
                  $" · verify pairs {run.VerifyIndexCulled:N0} indexed out, {run.VerifyPlacementChecks:N0}/{run.VerifyBoundsRejected:N0} bounds culled" +
                  $" · Conflict {run.ConflictCalls}×/{run.ConflictMilliseconds:0.0} ms"
                : "") +
            $"{(run.HitTimeBudget ? " · TIME LIMIT" : "")}" +
            $"{(run.ValidationErrors > 0 ? $" · VALIDATION ERRORS {run.ValidationErrors}" : "")}");
    }

    private static void PrintSummary(IReadOnlyList<BenchmarkSummary> summaries)
    {
        Console.WriteLine("\nTỔNG HỢP (median của các lần đo)");
        Console.WriteLine($"{"Workload",-22} {"Profile",-15} {"Nest ms",10} {"P95 ms",10} {"Verify ms",11} {"inst/s",10} {"Placed",10} {"Sheets",7} {"Eff%",7} {"Alloc",10} {"Hash",7}");
        foreach (var summary in summaries)
        {
            Console.WriteLine($"{summary.Workload,-22} {summary.Profile,-15} " +
                              $"{summary.MedianNestingMilliseconds,10:0.0} {summary.P95NestingMilliseconds,10:0.0} " +
                              $"{summary.MedianValidationMilliseconds,11:0.0} {summary.InstancesPerSecond,10:0.0} " +
                              $"{summary.MedianPlacedInstances,10:0} {summary.MedianSheetCount,7:0} " +
                              $"{summary.MedianUtilizationPercent,7:0.0} {FormatBytes((long)summary.MedianAllocatedBytes),10} " +
                              $"{summary.DistinctResultSignatures,3}/{summary.RunCount,-3}");
        }
    }

    private static void PrintInsights(IReadOnlyList<BenchmarkSummary> summaries)
    {
        Console.WriteLine("\nNHẬN ĐỊNH TỰ ĐỘNG");
        foreach (string insight in BuildInsights(summaries))
            Console.WriteLine($"- {insight}");
    }

    private static List<string> BuildInsights(IReadOnlyList<BenchmarkSummary> summaries)
    {
        var insights = new List<string>();
        var slowest = summaries.OrderByDescending(summary => summary.MedianNestingMilliseconds).FirstOrDefault();
        if (slowest is not null)
            insights.Add($"Hot workload hiện tại: {slowest.Workload} / {slowest.Profile} " +
                         $"({slowest.MedianNestingMilliseconds:0.0} ms median, {slowest.InstancesPerSecond:0.0} chi tiết/s).");

        var validationDominant = summaries
            .Where(summary => summary.MedianValidationMilliseconds > summary.MedianNestingMilliseconds)
            .OrderByDescending(summary => summary.MedianValidationMilliseconds / Math.Max(1, summary.MedianNestingMilliseconds))
            .FirstOrDefault();
        if (validationDominant is not null)
            insights.Add($"Validator tốn nhiều hơn engine ở {validationDominant.Workload}; không gộp thời gian này khi kết luận tốc độ nesting. " +
                         "Cân nhắc tối ưu broad-phase của Validator nếu thời gian xác minh cũng ảnh hưởng UX.");
        else
            insights.Add("Thời gian verify được tách riêng; so sánh Nest ms để đánh giá tốc độ lõi.");

        foreach (var group in summaries.GroupBy(summary => summary.Workload))
        {
            AddProfileInsight(group, "Balanced-90", "Balanced-45", "giảm bước xoay 90°→45° (cùng mục tiêu dừng)");
            AddProfileInsight(group, "Balanced-90", "Exhaustive-90", "tắt dừng sớm (cùng bước xoay 90°)");
            AddProfileInsight(group, "Balanced-45", "Exhaustive-45", "tắt dừng sớm (cùng bước xoay 45°)");
        }

        var unstable = summaries.Where(summary => summary.DistinctResultSignatures > 1).ToList();
        if (unstable.Count > 0)
            insights.Add($"{unstable.Count} cấu hình có đầu ra khác nhau giữa các lần chạy; có thể do giới hạn thời gian/song song. " +
                         "Cần phân biệt biến động hợp lệ của heuristic với regression trước khi so benchmark.");
        else
            insights.Add("Dấu vân tay kết quả ổn định giữa các lần chạy trong bộ benchmark này.");

        insights.Add("Để định vị hot path chính xác hơn, chạy profiler CPU/alloc trên workload chậm nhất; benchmark hộp đen chỉ đo tổng engine và không quy kết một hàm cụ thể.");
        return insights;

        void AddProfileInsight(IEnumerable<BenchmarkSummary> group, string baselineName, string candidateName, string change)
        {
            var baseline = group.FirstOrDefault(summary => summary.Profile == baselineName);
            var candidate = group.FirstOrDefault(summary => summary.Profile == candidateName);
            if (baseline is null || candidate is null) return;

            double efficiencyDelta = candidate.MedianUtilizationPercent - baseline.MedianUtilizationPercent;
            double timeRatio = candidate.MedianNestingMilliseconds / Math.Max(1, baseline.MedianNestingMilliseconds);
            bool fewerSheets = candidate.MedianSheetCount < baseline.MedianSheetCount;
            if (efficiencyDelta >= 1.5 || fewerSheets)
                insights.Add($"{baseline.Workload}: {change} cải thiện {efficiencyDelta:+0.0;-0.0;0.0} điểm % hiệu suất" +
                             $"{(fewerSheets ? " và giảm số tấm" : "")}, đổi lấy {timeRatio:0.0}× thời gian.");
            else if (timeRatio >= 1.8 && efficiencyDelta < 1)
                insights.Add($"{baseline.Workload}: {change} chậm {timeRatio:0.0}× nhưng hiệu suất chỉ đổi " +
                             $"{efficiencyDelta:+0.0;-0.0;0.0} điểm %; không nên bật rộng cho workload tương tự.");
        }
    }

    private static string FormatBytes(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024d * 1024):0.0} MiB"
        : $"{bytes / 1024d:0.0} KiB";

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Cyclone Nest2D nesting performance suite

            Usage:
              dotnet run --project tests\CycloneNest2D.Performance -c Release -- [options]

            Options:
              --quick                 3 synthetic workloads, one measured run/profile (default)
              --full                  Full workload matrix, three measured runs/profile
              --diagnostics           Collect internal timings; adds measurement overhead
              --repeat <count>        Override measured repetitions (1-20)
              --max-seconds <seconds> NestEngine time budget per run (0.1-3600)
              --dxf <file-or-folder>  Add a real DXF file, or recursively load a folder of DXFs
              --sheet-width <mm>      Sheet width for --dxf (default 2500)
              --sheet-height <mm>     Sheet height for --dxf (default 1250)
              --margin <mm>           Sheet edge margin for --dxf (default 5)
              --spacing <mm>          Part spacing for --dxf (default 3)
              --output <path-prefix>  Write <prefix>.json and <prefix>.csv
              --help                  Show this help

            Four profiles separate rotation cost from early-stop policy:
              Balanced-90:    90-degree rotation step, 85% early-stop target
              Balanced-45:    45-degree rotation step, 85% early-stop target
              Exhaustive-90:  90-degree rotation step, no early-stop target
              Exhaustive-45:  45-degree rotation step, no early-stop target
            """);
    }
}
