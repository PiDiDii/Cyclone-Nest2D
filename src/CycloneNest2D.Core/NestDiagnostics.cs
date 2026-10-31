using System.Diagnostics;

namespace CycloneNest2D.Core;

public sealed class NestDiagnostics
{
    private long _variantGeometryTicks;
    private long _variantRasterizationTicks;
    private long _rasterCellSizeBits;
    private long _primaryVariantsBuilt;
    private long _fineVariantsBuilt;
    private long _searchTicks;
    private long _verifyTicks;
    private long _verifyIndexCulled;
    private long _verifyPlacementChecks;
    private long _verifyBoundsRejected;
    private long _conflictTicks;
    private long _conflictCalls;

    public double VariantGeometryMilliseconds => ToMilliseconds(Interlocked.Read(ref _variantGeometryTicks));
    public double VariantRasterizationMilliseconds => ToMilliseconds(Interlocked.Read(ref _variantRasterizationTicks));
    public double RasterCellSizeMillimeters => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _rasterCellSizeBits));
    public long PrimaryVariantsBuilt => Interlocked.Read(ref _primaryVariantsBuilt);
    public long FineVariantsBuilt => Interlocked.Read(ref _fineVariantsBuilt);
    public double SearchMilliseconds => ToMilliseconds(Interlocked.Read(ref _searchTicks));
    public double VerifyMilliseconds => ToMilliseconds(Interlocked.Read(ref _verifyTicks));
    public long VerifyIndexCulled => Interlocked.Read(ref _verifyIndexCulled);
    public long VerifyPlacementChecks => Interlocked.Read(ref _verifyPlacementChecks);
    public long VerifyBoundsRejected => Interlocked.Read(ref _verifyBoundsRejected);
    public double ConflictMilliseconds => ToMilliseconds(Interlocked.Read(ref _conflictTicks));
    public long ConflictCalls => Interlocked.Read(ref _conflictCalls);

    internal void AddVariantGeometryTicks(long ticks) => Interlocked.Add(ref _variantGeometryTicks, ticks);
    internal void AddVariantRasterizationTicks(long ticks) => Interlocked.Add(ref _variantRasterizationTicks, ticks);
    internal void RecordRasterCellSize(double millimeters) =>
        Interlocked.Exchange(ref _rasterCellSizeBits, BitConverter.DoubleToInt64Bits(millimeters));
    internal void RecordVariantBuilt(bool primary)
    {
        if (primary) Interlocked.Increment(ref _primaryVariantsBuilt);
        else Interlocked.Increment(ref _fineVariantsBuilt);
    }
    internal void AddSearchTicks(long ticks) => Interlocked.Add(ref _searchTicks, ticks);
    internal void AddVerifyTicks(long ticks) => Interlocked.Add(ref _verifyTicks, ticks);
    internal void RecordVerifyIndexRange(long totalItems, long candidates) =>
        Interlocked.Add(ref _verifyIndexCulled, totalItems - candidates);
    internal void RecordVerifyPlacementCheck() => Interlocked.Increment(ref _verifyPlacementChecks);
    internal void RecordVerifyBoundsRejected() => Interlocked.Increment(ref _verifyBoundsRejected);

    internal void RecordConflict(long ticks)
    {
        Interlocked.Increment(ref _conflictCalls);
        Interlocked.Add(ref _conflictTicks, ticks);
    }

    private static double ToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
}
