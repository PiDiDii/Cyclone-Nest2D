namespace CycloneNest2D.Core;

public enum DxfOutputUnits { Millimeters, Inches }
public enum DxfFileLayout { SingleFile, MultipleFiles }
public enum DxfGeometryStyle { BasicLinesArcs, PolylineApproximation }
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
    public bool EnableMicroJoints { get; init; } = true;
    /// <summary>Độ rộng cầu micro-joint theo mm.</summary>
    public double MicroJointWidthMm { get; init; } = 0.4;
    /// <summary>Tổng số cầu mục tiêu phân bổ trên các contour đủ kích thước của mỗi part.</summary>
    public int MicroJointCount { get; init; } = 6;
    public double MicroJointSmallMaxLengthMm { get; init; } = 50;
    public double MicroJointSmallMaxWidthMm { get; init; } = 50;
    public double MicroJointLargeMinLengthMm { get; init; } = 500;
    public double MicroJointLargeMinWidthMm { get; init; } = 500;
    public DxfOutputUnits Units { get; init; } = DxfOutputUnits.Millimeters;
    public DxfTargetVersion TargetVersion { get; init; } = DxfTargetVersion.R12;
}
