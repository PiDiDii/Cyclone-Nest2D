using CycloneNest2D.Core;
using Xunit;

namespace CycloneNest2D.Tests;

public sealed class NestingPortfolioTests
{
    [Fact]
    public void PortfolioPrefersCompleteLayoutAndKeepsRunsWithinTimeBudget()
    {
        var baseline = new StubStrategy(NestingStrategyId.RasterBaseline, "baseline", sheetCount: 1, unplacedCount: 2);
        var diverse = new StubStrategy(NestingStrategyId.RasterDiverse, "diverse", sheetCount: 2, unplacedCount: 0);
        var options = new NestOptions
        {
            MaxSeconds = 12,
            Portfolio = new NestPortfolioOptions
            {
                PilotRatio = 0.15,
                PilotCapSeconds = 1,
                MinimumPilotSeconds = 0.5,
                MaxCandidates = 2,
                SmallProblemThresholdSeconds = 0
            }
        };

        var parts = new[]
        {
            new PartDefinition("A", 10, 10, 1),
            new PartDefinition("B", 11, 10, 1),
            new PartDefinition("C", 12, 10, 1),
            new PartDefinition("D", 13, 10, 1)
        };
        var result = new NestingPortfolioRunner(new PortfolioSelector(new[] { baseline, diverse }))
            .Run(parts, new SheetDefinition(100, 100, 0, 0), options);

        Assert.Equal("diverse", result.BestResult.Name);
        Assert.Empty(result.BestResult.Result.Unplaced);
        Assert.True(baseline.Seconds + diverse.Seconds <= options.MaxSeconds + 1e-9);
        Assert.True(baseline.Seconds > 0);
        Assert.True(diverse.Seconds > 0);
    }

    private sealed class StubStrategy : INestingStrategy
    {
        private readonly int _sheetCount;
        private readonly int _unplacedCount;

        public StubStrategy(NestingStrategyId id, string name, int sheetCount, int unplacedCount)
        {
            Id = id;
            Name = name;
            _sheetCount = sheetCount;
            _unplacedCount = unplacedCount;
        }

        public NestingStrategyId Id { get; }
        public string Name { get; }
        public string Description => Name;
        public double Seconds { get; private set; }

        public StrategyRunResult Run(IReadOnlyList<PartDefinition> parts, SheetDefinition sheet,
            NestBudget budget, CancellationToken cancellationToken)
        {
            Seconds += budget.MainSeconds;
            var result = new NestResult { Strategy = Name };
            for (var i = 0; i < _sheetCount; i++)
                result.Sheets.Add(new SheetResult { Index = i + 1, Width = sheet.Width, Height = sheet.Height });
            for (var i = 0; i < _unplacedCount; i++) result.Unplaced.Add($"part-{i}");
            return new StrategyRunResult(Id, Name, result, Name, 0, true);
        }
    }
}
