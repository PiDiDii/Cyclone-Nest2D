using System.Globalization;
using System.Text;

internal sealed record BenchmarkRun
{
    public static BenchmarkRun Warmup { get; } = new();
    public string Workload { get; init; } = "";
    public string Description { get; init; } = "";
    public string Profile { get; init; } = "";
    public int Repetition { get; init; }
    public int PartTypes { get; init; }
    public int RequestedInstances { get; init; }
    public double SheetWidth { get; init; }
    public double SheetHeight { get; init; }
    public double Margin { get; init; }
    public double Spacing { get; init; }
    public double RotationStep { get; init; }
    public double TargetUtilizationPercent { get; init; }
    public double TimeBudgetSeconds { get; init; }
    public bool DiagnosticsEnabled { get; init; }
    public double VariantGeometryMilliseconds { get; init; }
    public double VariantRasterizationMilliseconds { get; init; }
    public double RasterCellSizeMillimeters { get; init; }
    public long PrimaryVariantsBuilt { get; init; }
    public long FineVariantsBuilt { get; init; }
    public double SearchMilliseconds { get; init; }
    public double VerifyMilliseconds { get; init; }
    public long VerifyIndexCulled { get; init; }
    public long VerifyPlacementChecks { get; init; }
    public long VerifyBoundsRejected { get; init; }
    public double ConflictMilliseconds { get; init; }
    public long ConflictCalls { get; init; }
    public double NestingMilliseconds { get; init; }
    public double ValidationMilliseconds { get; init; }
    public double EndToEndMilliseconds { get; init; }
    public double CpuMilliseconds { get; init; }
    public long AllocatedBytes { get; init; }
    public long WorkingSetBeforeBytes { get; init; }
    public long WorkingSetAfterBytes { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    public int PlacedInstances { get; init; }
    public int UnplacedInstances { get; init; }
    public int SheetCount { get; init; }
    public double UtilizationPercent { get; init; }
    public double CompletionPercent { get; init; }
    public int ValidationErrors { get; init; }
    public bool HitTimeBudget { get; init; }
    public string ResultSignature { get; init; } = "";
}

internal sealed record BenchmarkSummary
{
    public string Workload { get; init; } = "";
    public string Profile { get; init; } = "";
    public int RunCount { get; init; }
    public double MedianNestingMilliseconds { get; init; }
    public double P95NestingMilliseconds { get; init; }
    public double MedianValidationMilliseconds { get; init; }
    public double MedianAllocatedBytes { get; init; }
    public double MedianUtilizationPercent { get; init; }
    public double MedianCompletionPercent { get; init; }
    public double MedianPlacedInstances { get; init; }
    public double MedianSheetCount { get; init; }
    public double InstancesPerSecond { get; init; }
    public int DistinctResultSignatures { get; init; }
    public int ValidationErrors { get; init; }

    public static List<BenchmarkSummary> FromRuns(IReadOnlyList<BenchmarkRun> runs) =>
        runs.GroupBy(run => (run.Workload, run.Profile))
            .Select(group =>
            {
                var times = group.Select(run => run.NestingMilliseconds).Order().ToArray();
                double medianTime = Median(times);
                return new BenchmarkSummary
                {
                    Workload = group.Key.Workload,
                    Profile = group.Key.Profile,
                    RunCount = group.Count(),
                    MedianNestingMilliseconds = medianTime,
                    P95NestingMilliseconds = Percentile95(times),
                    MedianValidationMilliseconds = Median(group.Select(run => run.ValidationMilliseconds)),
                    MedianAllocatedBytes = Median(group.Select(run => (double)run.AllocatedBytes)),
                    MedianUtilizationPercent = Median(group.Select(run => run.UtilizationPercent)),
                    MedianCompletionPercent = Median(group.Select(run => run.CompletionPercent)),
                    MedianPlacedInstances = Median(group.Select(run => (double)run.PlacedInstances)),
                    MedianSheetCount = Median(group.Select(run => (double)run.SheetCount)),
                    InstancesPerSecond = medianTime <= 0 ? 0 :
                        Median(group.Select(run => (double)run.PlacedInstances)) / (medianTime / 1000),
                    DistinctResultSignatures = group.Select(run => run.ResultSignature).Distinct().Count(),
                    ValidationErrors = group.Sum(run => run.ValidationErrors)
                };
            })
            .OrderBy(summary => summary.Workload, StringComparer.Ordinal)
            .ThenBy(summary => summary.Profile, StringComparer.Ordinal)
            .ToList();

    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = values.Order().ToArray();
        if (sorted.Length == 0) return 0;
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }

    private static double Percentile95(double[] sorted)
    {
        if (sorted.Length == 0) return 0;
        int index = Math.Clamp((int)Math.Ceiling(sorted.Length * 0.95) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }
}

internal sealed record BenchmarkReport
{
    public DateTimeOffset GeneratedAt { get; init; }
    public string Runtime { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public int ProcessorCount { get; init; }
    public bool Is64BitProcess { get; init; }
    public BenchmarkConfig Configuration { get; init; } = new();
    public List<BenchmarkRun> Runs { get; init; } = new();
    public List<BenchmarkSummary> Summary { get; init; } = new();
    public List<string> Insights { get; init; } = new();
}

internal static class CsvReport
{
    private static readonly string[] Columns =
    [
        "Workload", "Description", "Profile", "Repetition", "PartTypes", "RequestedInstances",
        "SheetWidth", "SheetHeight", "Margin", "Spacing", "RotationStep", "TargetUtilizationPercent",
        "TimeBudgetSeconds", "DiagnosticsEnabled", "VariantGeometryMilliseconds", "VariantRasterizationMilliseconds",
        "RasterCellSizeMillimeters", "PrimaryVariantsBuilt", "FineVariantsBuilt",
        "SearchMilliseconds", "VerifyMilliseconds", "VerifyIndexCulled", "VerifyPlacementChecks", "VerifyBoundsRejected",
        "ConflictMilliseconds", "ConflictCalls",
        "NestingMilliseconds", "ValidationMilliseconds", "EndToEndMilliseconds",
        "CpuMilliseconds", "AllocatedBytes", "WorkingSetBeforeBytes", "WorkingSetAfterBytes",
        "Gen0Collections", "Gen1Collections", "Gen2Collections", "PlacedInstances", "UnplacedInstances",
        "SheetCount", "UtilizationPercent", "CompletionPercent", "ValidationErrors", "HitTimeBudget",
        "ResultSignature"
    ];

    public static string Serialize(IEnumerable<BenchmarkRun> runs)
    {
        var output = new StringBuilder(string.Join(",", Columns)).AppendLine();
        foreach (BenchmarkRun run in runs)
        {
            string[] values =
            [
                run.Workload, run.Description, run.Profile, run.Repetition.ToString(CultureInfo.InvariantCulture),
                run.PartTypes.ToString(CultureInfo.InvariantCulture), run.RequestedInstances.ToString(CultureInfo.InvariantCulture),
                Number(run.SheetWidth), Number(run.SheetHeight), Number(run.Margin), Number(run.Spacing),
                Number(run.RotationStep), Number(run.TargetUtilizationPercent), Number(run.TimeBudgetSeconds),
                run.DiagnosticsEnabled.ToString(CultureInfo.InvariantCulture),
                Number(run.VariantGeometryMilliseconds), Number(run.VariantRasterizationMilliseconds),
                Number(run.RasterCellSizeMillimeters), run.PrimaryVariantsBuilt.ToString(CultureInfo.InvariantCulture),
                run.FineVariantsBuilt.ToString(CultureInfo.InvariantCulture),
                Number(run.SearchMilliseconds), Number(run.VerifyMilliseconds),
                run.VerifyIndexCulled.ToString(CultureInfo.InvariantCulture),
                run.VerifyPlacementChecks.ToString(CultureInfo.InvariantCulture),
                run.VerifyBoundsRejected.ToString(CultureInfo.InvariantCulture), Number(run.ConflictMilliseconds),
                run.ConflictCalls.ToString(CultureInfo.InvariantCulture),
                Number(run.NestingMilliseconds), Number(run.ValidationMilliseconds), Number(run.EndToEndMilliseconds),
                Number(run.CpuMilliseconds), run.AllocatedBytes.ToString(CultureInfo.InvariantCulture),
                run.WorkingSetBeforeBytes.ToString(CultureInfo.InvariantCulture), run.WorkingSetAfterBytes.ToString(CultureInfo.InvariantCulture),
                run.Gen0Collections.ToString(CultureInfo.InvariantCulture), run.Gen1Collections.ToString(CultureInfo.InvariantCulture),
                run.Gen2Collections.ToString(CultureInfo.InvariantCulture), run.PlacedInstances.ToString(CultureInfo.InvariantCulture),
                run.UnplacedInstances.ToString(CultureInfo.InvariantCulture), run.SheetCount.ToString(CultureInfo.InvariantCulture),
                Number(run.UtilizationPercent), Number(run.CompletionPercent), run.ValidationErrors.ToString(CultureInfo.InvariantCulture),
                run.HitTimeBudget.ToString(CultureInfo.InvariantCulture), run.ResultSignature
            ];
            output.AppendLine(string.Join(",", values.Select(Escape)));
        }
        return output.ToString();
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
