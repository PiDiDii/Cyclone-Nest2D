using System.Diagnostics;

namespace CycloneNest2D.Core;

/// <summary>
/// Stable identifiers for the core raster-backed strategies available in the current portfolio.
/// </summary>
public enum NestingStrategyId
{
    RasterBaseline = 0,
    RasterDiverse = 1,
}

/// <summary>
/// Budget allocated to a specific strategy run.
/// </summary>
public sealed record NestBudget(double TotalSeconds, double PilotSeconds, double MainSeconds, int MaxCandidates)
{
    public bool UsePilot => PilotSeconds > 0 && MainSeconds > 0;
}

/// <summary>
/// Result of a single strategy execution.
/// </summary>
public sealed record StrategyRunResult(NestingStrategyId StrategyId, string Name, NestResult Result, string Reason, double ElapsedSeconds, bool Success, bool WasPilot = false);

/// <summary>
/// Strategy abstraction for the portfolio layer. It does not validate candidate placements; validation happens later in the shared validator pipeline.
/// </summary>
public interface INestingStrategy
{
    NestingStrategyId Id { get; }
    string Name { get; }
    string Description { get; }
    StrategyRunResult Run(IReadOnlyList<PartDefinition> parts, SheetDefinition sheet, NestBudget budget, CancellationToken cancellationToken);
}

/// <summary>
/// Determines which strategies to try for a given nesting problem.
/// </summary>
public sealed record StrategySelection(INestingStrategy Strategy, string Reason);

/// <summary>
/// Tuneable thresholds for the pilot and candidate-selection logic.
/// </summary>
public sealed class NestPortfolioOptions
{
    public double PilotRatio { get; set; } = 0.15;
    public double PilotCapSeconds { get; set; } = 3.0;
    public double MinimumPilotSeconds { get; set; } = 0.5;
    public int MaxCandidates { get; set; } = 2;
    public double SmallProblemThresholdSeconds { get; set; } = 10.0;
    public double HighDispersionThreshold { get; set; } = 0.35;
    public double HighPartTypeThreshold { get; set; } = 4;
    public double LowBboxFillThreshold { get; set; } = 0.65;
    public double VeryLowBboxFillThreshold { get; set; } = 0.40;
    public double HighComplexityThreshold { get; set; } = 0.25;
    public double RasterResourceWarningThresholdCells { get; set; } = 2_000_000;
}

/// <summary>
/// Portfolio entry point for raster-backed search strategies.
/// </summary>
public sealed class RasterNestingStrategy : INestingStrategy
{
    public RasterNestingStrategy(NestingStrategyId id, string name, string description, NestSearchMode searchMode, bool allowRotation = true)
    {
        Id = id;
        Name = name;
        Description = description;
        SearchMode = searchMode;
        AllowRotation = allowRotation;
    }

    public NestingStrategyId Id { get; }
    public string Name { get; }
    public string Description { get; }
    public NestSearchMode SearchMode { get; }
    public bool AllowRotation { get; }

    public StrategyRunResult Run(IReadOnlyList<PartDefinition> parts, SheetDefinition sheet, NestBudget budget, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var options = new NestOptions
        {
            AllowRotation = AllowRotation,
            MaxSeconds = Math.Max(0.05, budget.MainSeconds),
            SearchMode = SearchMode,
            Cancel = cancellationToken,
            Report = _ => { },
            TargetUtilizationPercent = 0,
            RotationStep = 90,
        };

        var result = NestEngine.Run(parts, sheet, options);
        var reason = string.IsNullOrWhiteSpace(result.Strategy)
            ? $"{Name} finished in {sw.Elapsed.TotalSeconds:0.##}s."
            : result.Strategy;

        return new StrategyRunResult(Id, Name, result, reason, sw.Elapsed.TotalSeconds, result.Unplaced.Count == 0 || result.Sheets.Count > 0, false);
    }
}

/// <summary>
/// Deterministic selection of candidate strategies based on feature signals from the preflight geometry.
/// </summary>
public sealed class PortfolioSelector
{
    private readonly IReadOnlyList<INestingStrategy> _strategies;

    public PortfolioSelector(IEnumerable<INestingStrategy>? strategies = null)
    {
        _strategies = (strategies ?? DefaultStrategies()).ToList();
    }

    public IReadOnlyList<StrategySelection> Select(NestProblemFeatures features, double totalSeconds, NestPortfolioOptions? options = null)
    {
        options ??= new NestPortfolioOptions();
        var results = new List<StrategySelection>();
        var baseline = _strategies.FirstOrDefault(s => s.Id == NestingStrategyId.RasterBaseline)
            ?? new RasterNestingStrategy(NestingStrategyId.RasterBaseline, "RasterBaseline", "Safe baseline raster run.", NestSearchMode.Fast);

        results.Add(new StrategySelection(baseline, "Raster baseline is always included as the safe, deterministic seed."));

        if (features.IsEmpty || totalSeconds <= 0)
        {
            return results.Take(options.MaxCandidates).ToList();
        }

        if (totalSeconds <= options.SmallProblemThresholdSeconds)
        {
            return results.Take(1).ToList();
        }

        if (features.PartTypeCount >= options.HighPartTypeThreshold || features.SizeDispersion >= options.HighDispersionThreshold)
        {
            var diverse = _strategies.FirstOrDefault(s => s.Id == NestingStrategyId.RasterDiverse)
                ?? new RasterNestingStrategy(NestingStrategyId.RasterDiverse, "RasterDiverse", "Broader raster ordering and more varied search seed.", NestSearchMode.Balanced);
            results.Add(new StrategySelection(diverse, $"Size dispersion or part-type count is elevated (dispersion={features.SizeDispersion:0.##}, types={features.PartTypeCount})."));
        }

        if (features.BboxFill < options.LowBboxFillThreshold || features.LowBboxFillRatio65 > 0.5 || features.ComplexityP90 > options.HighComplexityThreshold)
        {
            var diverse = _strategies.FirstOrDefault(s => s.Id == NestingStrategyId.RasterDiverse)
                ?? new RasterNestingStrategy(NestingStrategyId.RasterDiverse, "RasterDiverse", "Broader raster ordering for irregular or sparse parts.", NestSearchMode.Balanced);
            if (!results.Any(s => s.Strategy.Id == NestingStrategyId.RasterDiverse))
            {
                results.Add(new StrategySelection(diverse, $"Bbox fill is low or geometry is complex (fill={features.BboxFill:0.##}, complexity={features.ComplexityP90:0.##})."));
            }
        }

        if (features.EstimatedRasterCells > options.RasterResourceWarningThresholdCells)
        {
            results.RemoveAll(s => s.Strategy.Id == NestingStrategyId.RasterDiverse);
            results.Add(new StrategySelection(baseline, "Raster resource estimate is near the limit; the safe baseline is preferred over the broader search."));
        }

        return results.DistinctBy(r => r.Strategy.Id).Take(options.MaxCandidates).ToList();
    }

    private static IReadOnlyList<INestingStrategy> DefaultStrategies()
    {
        return new INestingStrategy[]
        {
            new RasterNestingStrategy(NestingStrategyId.RasterBaseline, "RasterBaseline", "Safe baseline raster search.", NestSearchMode.Fast),
            new RasterNestingStrategy(NestingStrategyId.RasterDiverse, "RasterDiverse", "Broader ordering and variability scan for irregular problems.", NestSearchMode.Balanced),
        };
    }
}

/// <summary>
/// Coordinates a short pilot and the full portfolio search with the same total MaxSeconds budget.
/// </summary>
public sealed class NestingPortfolioRunner
{
    private readonly PortfolioSelector _selector;

    public NestingPortfolioRunner(PortfolioSelector? selector = null)
    {
        _selector = selector ?? new PortfolioSelector();
    }

    public PortfolioRunResult Run(IReadOnlyList<PartDefinition> parts, SheetDefinition sheet, NestOptions options, CancellationToken cancellationToken = default)
    {
        var features = NestFeatureExtractor.Extract(parts, sheet);
        var portfolioOptions = options.Portfolio ?? new NestPortfolioOptions();
        var candidates = _selector.Select(features, options.MaxSeconds, portfolioOptions);
        var totalBudgetSeconds = Math.Max(0.05, options.MaxSeconds);
        var pilotSeconds = Math.Clamp(totalBudgetSeconds * portfolioOptions.PilotRatio, portfolioOptions.MinimumPilotSeconds, Math.Max(portfolioOptions.MinimumPilotSeconds, portfolioOptions.PilotCapSeconds));
        var remainingSeconds = Math.Max(0.05, totalBudgetSeconds - pilotSeconds);

        StrategyRunResult? best = null;
        var reasons = new List<string>();

        if (candidates.Count == 1)
        {
            // Do not spend only a pilot-sized budget when there is no alternative to compare.
            var only = candidates[0];
            best = only.Strategy.Run(parts, sheet,
                new NestBudget(totalBudgetSeconds, 0, totalBudgetSeconds, portfolioOptions.MaxCandidates), cancellationToken);
            reasons.Add(only.Reason);
        }
        else if (candidates.Count > 1)
        {
            var pilotCandidate = candidates[0];
            best = pilotCandidate.Strategy.Run(parts, sheet,
                new NestBudget(totalBudgetSeconds, pilotSeconds, pilotSeconds, portfolioOptions.MaxCandidates), cancellationToken);
            reasons.Add(pilotCandidate.Reason);

            double perCandidateSeconds = Math.Max(0.05, remainingSeconds / (candidates.Count - 1));
            for (var i = 1; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var run = candidate.Strategy.Run(parts, sheet,
                    new NestBudget(totalBudgetSeconds, pilotSeconds, perCandidateSeconds, portfolioOptions.MaxCandidates), cancellationToken);
                reasons.Add(candidate.Reason);
                best = Better(run, best) ? run : best;
            }
        }

        var finalResult = best ?? new StrategyRunResult(NestingStrategyId.RasterBaseline, "RasterBaseline", new NestResult(), "No portfolio strategy produced a result.", 0, false);
        finalResult.Result.Strategy ??= "Portfolio";
        finalResult.Result.Notes.Add(string.Join("; ", reasons));

        return new PortfolioRunResult(finalResult, features, candidates.Select(c => c.Reason).ToList());
    }

    private static bool Better(StrategyRunResult candidate, StrategyRunResult? current)
    {
        if (current == null) return true;
        // Manufacturing completeness dominates sheet count: never prefer a smaller
        // layout that silently leaves more requested parts unplaced.
        if (candidate.Result.Unplaced.Count != current.Result.Unplaced.Count)
        {
            return candidate.Result.Unplaced.Count < current.Result.Unplaced.Count;
        }

        if (candidate.Result.Sheets.Count != current.Result.Sheets.Count)
        {
            return candidate.Result.Sheets.Count < current.Result.Sheets.Count;
        }

        return candidate.Result.Utilization > current.Result.Utilization;
    }
}

/// <summary>
/// Aggregate output of a portfolio execution.
/// </summary>
public sealed record PortfolioRunResult(StrategyRunResult BestResult, NestProblemFeatures Features, IReadOnlyList<string> Reasons);
