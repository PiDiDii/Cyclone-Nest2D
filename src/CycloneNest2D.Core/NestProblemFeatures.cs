namespace CycloneNest2D.Core;

/// <summary>
/// Immutable summary of a nesting problem, computed from verified geometry and preflight data.
/// All values are finite and safe against empty problems, zero-area inputs, and invalid numeric states.
/// </summary>
public sealed record NestProblemFeatures(
    int PartTypeCount,
    long InstanceCount,
    double BboxFill,
    double LowBboxFillRatio,
    double LowBboxFillRatio65,
    double LowBboxFillRatio40,
    double SizeDispersion,
    double AspectRatioDispersion,
    double LoopCount,
    double SegmentCount,
    double ComplexityP90,
    double RotationRestrictedRatio,
    double OrientationAvailability,
    double FitsSheetRatio,
    double NearSheetLimitRatio,
    double EstimatedRasterCells,
    double EstimatedRasterMemoryMb,
    double EstimatedOrientationCount,
    double EffectivePartArea,
    string? Notes = null)
{
    public bool IsEmpty => PartTypeCount == 0 || InstanceCount == 0;
}

/// <summary>
/// Extracts problem-level features from preflighted parts and the active sheet.
/// </summary>
public static class NestFeatureExtractor
{
    private const double DefaultSheetWidth = 2500.0;
    private const double DefaultSheetHeight = 1250.0;
    private const double DefaultMargin = 5.0;
    private const double DefaultSpacing = 3.0;

    public static NestProblemFeatures Extract(IReadOnlyList<PartDefinition> parts, SheetDefinition? sheet = null)
    {
        if (parts == null || parts.Count == 0)
        {
            return Empty();
        }

        var safeSheet = sheet ?? new SheetDefinition(DefaultSheetWidth, DefaultSheetHeight, DefaultMargin, DefaultSpacing);
        var validParts = parts.Where(p => p != null && p.Quantity > 0 && double.IsFinite(p.Width) && double.IsFinite(p.Height) && p.Width > 0 && p.Height > 0).ToList();
        if (validParts.Count == 0)
        {
            return Empty();
        }

        long instanceCount = 0;
        double totalArea = 0;
        double totalBboxFillWeighted = 0;
        int below65Count = 0;
        int below40Count = 0;
        var areaSamples = new List<double>();
        var aspectSamples = new List<double>();
        double totalLoops = 0;
        double totalSegments = 0;
        var complexitySamples = new List<double>();
        int rotationRestricted = 0;
        int fitsSheetCount = 0;
        int nearSheetLimitCount = 0;
        double estimatedOrientationCount = 0;
        double totalOrientationAvailability = 0;

        foreach (var part in validParts)
        {
            var quantity = Math.Max(0, part.Quantity);
            if (quantity <= 0) continue;

            instanceCount += quantity;
            var width = EnsureFinite(part.Width, 0);
            var height = EnsureFinite(part.Height, 0);
            var area = Math.Max(0.0, width * height);
            totalArea += area * quantity;

            var partBboxFill = ComputeBboxFill(width, height, part.Shape);
            totalBboxFillWeighted += partBboxFill * quantity;
            if (partBboxFill < 0.65) below65Count++;
            if (partBboxFill < 0.40) below40Count++;

            areaSamples.Add(Math.Max(1e-9, area));
            aspectSamples.Add(Math.Max(1e-9, width / Math.Max(height, 1e-9)));

            var loops = TryGetLoopSet(part);
            var loopCountForPart = loops?.Loops.Count ?? 0;
            var segmentCountForPart = loops == null ? 0 : loops.Loops.Sum(loop => loop.Item1.Length);
            totalLoops += loopCountForPart * quantity;
            totalSegments += segmentCountForPart * quantity;
            complexitySamples.Add(Math.Max(0.0, loopCountForPart + segmentCountForPart / 10.0));

            if (!part.CanRotate)
            {
                rotationRestricted += quantity;
            }

            var orientationCount = DetermineOrientationCount(part, safeSheet);
            estimatedOrientationCount += orientationCount * quantity;
            totalOrientationAvailability += Math.Max(1.0, orientationCount) * quantity;

            if (FitsSheet(part, safeSheet)) fitsSheetCount += quantity;
            if (IsNearSheetLimit(part, safeSheet)) nearSheetLimitCount += quantity;
        }

        if (instanceCount <= 0)
        {
            return Empty();
        }

        var meanBboxFill = totalBboxFillWeighted / instanceCount;
        var lowBboxFillRatio65 = below65Count / (double)validParts.Count;
        var lowBboxFillRatio40 = below40Count / (double)validParts.Count;
        var sizeDispersion = SafeDispersion(areaSamples, LogScale: true);
        var aspectRatioDispersion = SafeDispersion(aspectSamples, LogScale: false);
        var meanLoopCount = totalLoops / instanceCount;
        var meanSegmentCount = totalSegments / instanceCount;
        var complexityP90 = Quantile(complexitySamples, 0.90);
        var rotationRestrictedRatio = rotationRestricted / (double)instanceCount;
        var orientationAvailability = totalOrientationAvailability > 0 ? estimatedOrientationCount / totalOrientationAvailability : 0;
        var fitsSheetRatio = fitsSheetCount / (double)instanceCount;
        var nearSheetLimitRatio = nearSheetLimitCount / (double)instanceCount;

        var effectivePartArea = totalArea / Math.Max(1, instanceCount);
        var estimatedRasterCellArea = Math.Max(1.0, safeSheet.Width * safeSheet.Height / 4000.0);
        var estimatedRasterCells = safeSheet.Width * safeSheet.Height / Math.Max(1.0, estimatedRasterCellArea);
        var estimatedRasterMemoryMb = Math.Max(0.0, (estimatedRasterCells * 8.0) / (1024.0 * 1024.0));

        return new NestProblemFeatures(
            PartTypeCount: validParts.Count,
            InstanceCount: instanceCount,
            BboxFill: meanBboxFill,
            LowBboxFillRatio: lowBboxFillRatio65,
            LowBboxFillRatio65: lowBboxFillRatio65,
            LowBboxFillRatio40: lowBboxFillRatio40,
            SizeDispersion: sizeDispersion,
            AspectRatioDispersion: aspectRatioDispersion,
            LoopCount: meanLoopCount,
            SegmentCount: meanSegmentCount,
            ComplexityP90: complexityP90,
            RotationRestrictedRatio: rotationRestrictedRatio,
            OrientationAvailability: orientationAvailability,
            FitsSheetRatio: fitsSheetRatio,
            NearSheetLimitRatio: nearSheetLimitRatio,
            EstimatedRasterCells: estimatedRasterCells,
            EstimatedRasterMemoryMb: estimatedRasterMemoryMb,
            EstimatedOrientationCount: estimatedOrientationCount / Math.Max(1.0, instanceCount),
            EffectivePartArea: effectivePartArea,
            Notes: CreateNotes(validParts, safeSheet));
    }

    private static NestProblemFeatures Empty() => new(
        PartTypeCount: 0,
        InstanceCount: 0,
        BboxFill: 0,
        LowBboxFillRatio: 0,
        LowBboxFillRatio65: 0,
        LowBboxFillRatio40: 0,
        SizeDispersion: 0,
        AspectRatioDispersion: 0,
        LoopCount: 0,
        SegmentCount: 0,
        ComplexityP90: 0,
        RotationRestrictedRatio: 0,
        OrientationAvailability: 0,
        FitsSheetRatio: 0,
        NearSheetLimitRatio: 0,
        EstimatedRasterCells: 0,
        EstimatedRasterMemoryMb: 0,
        EstimatedOrientationCount: 0,
        EffectivePartArea: 0,
        Notes: "No valid parts to summarize.");

    private static double ComputeBboxFill(double width, double height, DxfShape? shape)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return 0.0;
        }

        var area = width * height;
        if (area <= 0 || !double.IsFinite(area))
        {
            return 0.0;
        }

        if (shape == null)
        {
            return 1.0;
        }

        var loops = ShapeOps.Build(shape);
        if (!loops.Reliable || loops.Area <= 0)
        {
            return 0.0;
        }

        var candidate = loops.Area / area;
        return Math.Clamp(candidate, 0.0, 1.0);
    }

    private static LoopSet? TryGetLoopSet(PartDefinition part)
    {
        if (part.Shape == null)
        {
            return LoopSet.FromRect(part.Width, part.Height);
        }

        var loops = ShapeOps.Build(part.Shape);
        return loops.Reliable ? loops : null;
    }

    private static bool FitsSheet(PartDefinition part, SheetDefinition sheet)
    {
        if (!double.IsFinite(sheet.Width) || !double.IsFinite(sheet.Height) || sheet.Width <= 0 || sheet.Height <= 0)
        {
            return false;
        }

        var width = EnsureFinite(part.Width, 0);
        var height = EnsureFinite(part.Height, 0);
        return width <= sheet.Width && height <= sheet.Height;
    }

    private static bool IsNearSheetLimit(PartDefinition part, SheetDefinition sheet)
    {
        var width = EnsureFinite(part.Width, 0);
        var height = EnsureFinite(part.Height, 0);
        var sheetMin = Math.Min(sheet.Width, sheet.Height);
        var partMax = Math.Max(width, height);
        return sheetMin > 0 && partMax / sheetMin >= 0.8;
    }

    private static int DetermineOrientationCount(PartDefinition part, SheetDefinition sheet)
    {
        if (!part.CanRotate)
        {
            return 1;
        }

        var width = EnsureFinite(part.Width, 0);
        var height = EnsureFinite(part.Height, 0);
        if (width <= 0 || height <= 0)
        {
            return 1;
        }

        var minSide = Math.Min(width, height);
        var maxSide = Math.Max(width, height);
        if (minSide <= 0 || maxSide <= 0)
        {
            return 1;
        }

        if (sheet.Width <= 0 || sheet.Height <= 0)
        {
            return 2;
        }

        var fitWithoutRotation = width <= sheet.Width && height <= sheet.Height;
        var fitRotated = height <= sheet.Width && width <= sheet.Height;
        return fitWithoutRotation || fitRotated ? 2 : 1;
    }

    private static double SafeDispersion(List<double> values, bool LogScale)
    {
        if (values.Count == 0) return 0.0;

        var finite = values.Where(v => double.IsFinite(v) && v > 0).Select(v => LogScale ? Math.Log(v) : v).ToList();
        if (finite.Count == 0) return 0.0;

        var mean = finite.Average();
        var variance = finite.Sum(v => (v - mean) * (v - mean)) / finite.Count;
        return Math.Sqrt(Math.Max(0.0, variance));
    }

    private static double Quantile(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0.0;

        var ordered = values.Where(v => double.IsFinite(v)).OrderBy(v => v).ToList();
        if (ordered.Count == 0) return 0.0;

        if (ordered.Count == 1) return ordered[0];

        var index = Math.Clamp((ordered.Count - 1) * percentile, 0, ordered.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        if (lower == upper)
        {
            return ordered[lower];
        }

        var fractional = index - lower;
        return ordered[lower] + (ordered[upper] - ordered[lower]) * fractional;
    }

    private static double EnsureFinite(double value, double fallback)
    {
        return double.IsFinite(value) ? value : fallback;
    }

    private static string CreateNotes(IReadOnlyList<PartDefinition> parts, SheetDefinition sheet)
    {
        var typeCount = parts.Count(p => p.Quantity > 0);
        var shapeComplexity = parts.Count(p => p.Shape != null && p.Quantity > 0);
        return $"{typeCount} part types; {shapeComplexity} with verified contour; sheet {sheet.Width:0.##}x{sheet.Height:0.##} mm.";
    }
}
