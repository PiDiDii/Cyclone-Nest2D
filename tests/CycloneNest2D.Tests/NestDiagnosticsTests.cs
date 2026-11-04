using CycloneNest2D.Core;
using Xunit;

namespace CycloneNest2D.Tests;

public sealed class NestDiagnosticsTests
{
    [Fact]
    public void ConflictDetectsContainmentWithoutTreatingHolesAsMaterial()
    {
        var outer = ShapeOps.Build(ShapeOps.RectShape("Outer", 10, 10));
        var inner = ShapeOps.Build(ShapeOps.RectShape("Inner", 2, 2));
        var ring = new DxfShape(
            "Ring",
            [new CircleSeg(10, 10, 10, "CUT"), new CircleSeg(10, 10, 8, "CUT")],
            1,
            new Dictionary<string, int> { ["CUT"] = 1 },
            Array.Empty<string>(),
            0);
        var holeInsert = ShapeOps.Build(ShapeOps.RectShape("Hole insert", 5, 5));

        Assert.True(ShapeOps.Conflict(outer, 0, 0, inner, 4, 4, 0));
        Assert.False(ShapeOps.Conflict(ShapeOps.Build(ring), 0, 0, holeInsert, 6, 6, 0));
    }

    [Fact]
    public void CollectsVariantSearchAndConflictMetricsWithoutChangingPlacementValidity()
    {
        var ring = new DxfShape(
            "Ring",
            [new CircleSeg(10, 10, 10, "CUT"), new CircleSeg(10, 10, 8, "CUT")],
            1,
            new Dictionary<string, int> { ["CUT"] = 1 },
            Array.Empty<string>(),
            0);
        var insert = ShapeOps.RectShape("Insert", 5, 5);
        var parts = new[]
        {
            new PartDefinition("Ring", ring.Width, ring.Height, 1, true, ring),
            new PartDefinition("Insert", insert.Width, insert.Height, 1, true, insert)
        };
        var sheet = new SheetDefinition(30, 30, 0, 1);
        var diagnostics = new NestDiagnostics();
        var ringLoops = ShapeOps.Build(ring);
        var insertLoops = ShapeOps.Build(insert);

        Assert.False(ShapeOps.Conflict(ringLoops, 0, 0, insertLoops, 6, 6, 0));
        Assert.True(ShapeOps.Conflict(ringLoops, 0, 0, insertLoops, 0, 0, 0));
        Assert.False(ShapeOps.Conflict(ringLoops, 0, 0, insertLoops, 40, 40, 1));

        var result = NestEngine.Run(parts, sheet, new NestOptions
        {
            AllowRotation = false,
            MaxSeconds = 5,
            TargetUtilizationPercent = 0,
            Diagnostics = diagnostics
        });

        Assert.Empty(result.Unplaced);
        Assert.Empty(Validator.Check(result, sheet));
        Assert.True(diagnostics.VariantGeometryMilliseconds > 0);
        Assert.True(diagnostics.VariantRasterizationMilliseconds > 0);
        Assert.True(diagnostics.RasterCellSizeMillimeters > 0);
        Assert.True(diagnostics.PrimaryVariantsBuilt > 0);
        Assert.True(diagnostics.SearchMilliseconds > 0);
        Assert.True(diagnostics.VerifyMilliseconds > 0);
        Assert.True(diagnostics.ConflictCalls > 0);
        Assert.True(diagnostics.ConflictMilliseconds >= 0);
    }

    [Fact]
    public void VerifyXIndexKeepsZeroSpacingPlacementsValid()
    {
        var block = ShapeOps.RectShape("Block", 5, 5);
        var parts = new[]
        {
            new PartDefinition("Block", block.Width, block.Height, 80, false, block)
        };
        var sheet = new SheetDefinition(50, 50, 0, 0);
        var diagnostics = new NestDiagnostics();

        var result = NestEngine.Run(parts, sheet, new NestOptions
        {
            AllowRotation = false,
            MaxSeconds = 5,
            TargetUtilizationPercent = 0,
            Diagnostics = diagnostics
        });

        Assert.Empty(result.Unplaced);
        Assert.Empty(Validator.Check(result, sheet));
        Assert.True(diagnostics.VerifyIndexCulled > 0);
    }

    [Fact]
    public void AdaptiveCellSizeAccountsForPartScaleAndSpacing()
    {
        var part = new PartDefinition("Small", 6, 6, 1, false);
        var sheet = new SheetDefinition(2500, 2500, 0, 3);
        var diagnostics = new NestDiagnostics();

        var result = NestEngine.Run(new[] { part }, sheet, new NestOptions
        {
            AllowRotation = false,
            MaxSeconds = 5,
            TargetUtilizationPercent = 0,
            Diagnostics = diagnostics
        });

        Assert.Empty(result.Unplaced);
        Assert.Empty(Validator.Check(result, sheet));
        Assert.True(diagnostics.RasterCellSizeMillimeters < 2);
        Assert.True(diagnostics.RasterCellSizeMillimeters >= 2 / Math.Sqrt(1.25) - 1e-9);
    }

    [Fact]
    public void FineRotationVariantsAreDeferredUntilTargetIsNotMet()
    {
        var shape = ShapeOps.RectShape("Contour", 10, 20);
        var part = new PartDefinition("Contour", shape.Width, shape.Height, 1, true, shape);
        var sheet = new SheetDefinition(50, 50, 0, 0);
        var targetDiagnostics = new NestDiagnostics();
        var targetResult = NestEngine.Run(new[] { part }, sheet, new NestOptions
        {
            AllowRotation = true,
            RotationStep = 45,
            MaxSeconds = 5,
            TargetUtilizationPercent = 1,
            Diagnostics = targetDiagnostics
        });

        Assert.Empty(targetResult.Unplaced);
        Assert.Empty(Validator.Check(targetResult, sheet));
        Assert.Equal(0, targetDiagnostics.FineVariantsBuilt);

        var exhaustiveDiagnostics = new NestDiagnostics();
        var exhaustiveResult = NestEngine.Run(new[] { part }, sheet, new NestOptions
        {
            AllowRotation = true,
            RotationStep = 45,
            MaxSeconds = 5,
            TargetUtilizationPercent = 0,
            Diagnostics = exhaustiveDiagnostics
        });

        Assert.Empty(exhaustiveResult.Unplaced);
        Assert.Empty(Validator.Check(exhaustiveResult, sheet));
        Assert.True(exhaustiveDiagnostics.PrimaryVariantsBuilt > 0);
        Assert.True(exhaustiveDiagnostics.FineVariantsBuilt > 0);
    }
}
