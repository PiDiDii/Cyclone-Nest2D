namespace Nesting2D.Core;

public enum DxfOutputUnits { Millimeters, Inches }
public enum DxfFileLayout { SingleFile, MultipleFiles }
public enum DxfGeometryStyle { BasicLinesArcs, PolylineApproximation }
public enum DxfLoopDirection { CounterClockwise, Clockwise }
public enum DxfTargetVersion { R12, AutoCAD2000, AutoCAD2007, AutoCAD2010, AutoCAD2018 }

/// <summary>Thiết lập xuất DXF độc lập với thiết lập nesting.</summary>
public sealed record DxfExportOptions
{
    public bool PartsOnSingleLayer { get; init; }
    public string PartLayerName { get; init; } = "CUT";
    public int NestOrientation { get; init; }
    public bool AddPartLabels { get; init; }
    public bool AddNestStatistics { get; init; }
    public bool OutputSheets { get; init; } = true;
    public string SheetLayerName { get; init; } = "SHEET";
    public DxfFileLayout FileLayout { get; init; } = DxfFileLayout.SingleFile;
    public DxfGeometryStyle GeometryStyle { get; init; } = DxfGeometryStyle.BasicLinesArcs;
    public bool ExportPartsAsBlocks { get; init; }
    public bool ExportNestsAsBlocks { get; init; }
    public bool RemoveCommonEdges { get; init; }
    public DxfLoopDirection OuterDirection { get; init; } = DxfLoopDirection.CounterClockwise;
    public DxfLoopDirection InnerDirection { get; init; } = DxfLoopDirection.CounterClockwise;
    /// <summary>Độ rộng cầu micro-joint theo mm; bằng 0 thì không tạo micro-joint.</summary>
    public double MicroJointWidthMm { get; init; }
    /// <summary>Số cầu micro-joint mục tiêu trên mỗi biên ngoài của part.</summary>
    public int MicroJointCount { get; init; }
    public DxfOutputUnits Units { get; init; } = DxfOutputUnits.Millimeters;
    public DxfTargetVersion TargetVersion { get; init; } = DxfTargetVersion.R12;
}
