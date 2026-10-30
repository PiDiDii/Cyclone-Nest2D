using CycloneNest2D.Core;

namespace CycloneNest2D.UI;

public sealed record NestRun(NestResult Result, SheetDefinition Sheet, List<string> Errors, bool Cancelled, TimeSpan Elapsed, bool TimeLimitReached = false);

/// <summary>Chạy nesting ở luồng nền, có huỷ và báo tiến độ; kết quả luôn được kiểm tra lại bằng Validator.</summary>
public static class NestingService
{
    public static Task<NestRun> RunAsync(IReadOnlyList<PartDefinition> parts, NestSettings settings,
                                         IProgress<string>? progress, CancellationToken cancel)
    {
        var sheet = settings.ToSheet();
        var opt = settings.ToOptions(cancel, progress == null ? null : progress.Report);
        return Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = NestEngine.Run(parts, sheet, opt);
            var errors = Validator.Check(result, sheet);
            return new NestRun(result, sheet, errors, cancel.IsCancellationRequested, sw.Elapsed);
        });
    }
}
