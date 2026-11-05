using System.Globalization;
using System.Text;

namespace CycloneNest2D.Core;

/// <summary>DXF exporter configurable for sheet layout, units, layers, blocks and compatibility.</summary>
public static class DxfExportWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const double SheetGapMm = 500;
    private const double CommonEdgeToleranceMm = 0.001;
    private const double CommonEdgeAngleBucket = 0.001;

    private sealed record BlockDefinition(string Name, string Contents);

    private sealed class CommonEdgeTracker
    {
        private readonly double _tolerance;
        private readonly Dictionary<(long Angle, long Offset), List<(double X1, double Y1, double X2, double Y2)>> _segments = new();

        public CommonEdgeTracker(double tolerance) => _tolerance = tolerance;

        public IReadOnlyList<(double X1, double Y1, double X2, double Y2)> AddAndGetUncovered(
            double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1, dy = y2 - y1;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= _tolerance)
                return Array.Empty<(double, double, double, double)>();

            double ux = dx / length, uy = dy / length;
            double keyUx = ux, keyUy = uy;
            if (keyUx < 0 || (Math.Abs(keyUx) <= CommonEdgeAngleBucket && keyUy < 0))
            {
                keyUx = -keyUx;
                keyUy = -keyUy;
            }
            double angle = Math.Atan2(keyUy, keyUx);
            if (angle < 0) angle += Math.PI;
            double offset = -keyUy * x1 + keyUx * y1;
            long angleBucket = (long)Math.Round(angle / CommonEdgeAngleBucket);
            long offsetBucket = (long)Math.Round(offset / _tolerance);
            var remaining = new List<(double Start, double End)> { (0, length) };
            for (long angleDelta = -1; angleDelta <= 1; angleDelta++)
            for (long offsetDelta = -1; offsetDelta <= 1; offsetDelta++)
            {
                if (!_segments.TryGetValue((angleBucket + angleDelta, offsetBucket + offsetDelta), out var bucket))
                    continue;
                foreach (var existing in bucket)
                {
                    double ex = existing.X2 - existing.X1, ey = existing.Y2 - existing.Y1;
                    double existingLength = Math.Sqrt(ex * ex + ey * ey);
                    if (existingLength <= _tolerance) continue;

                    double distance1 = Math.Abs((existing.X1 - x1) * uy - (existing.Y1 - y1) * ux);
                    double distance2 = Math.Abs((existing.X2 - x1) * uy - (existing.Y2 - y1) * ux);
                    if (distance1 > _tolerance || distance2 > _tolerance)
                        continue;

                    double t1 = (existing.X1 - x1) * ux + (existing.Y1 - y1) * uy;
                    double t2 = (existing.X2 - x1) * ux + (existing.Y2 - y1) * uy;
                    double overlapStart = Math.Max(0, Math.Min(t1, t2));
                    double overlapEnd = Math.Min(length, Math.Max(t1, t2));
                    if (overlapEnd - overlapStart <= _tolerance) continue;

                    var next = new List<(double Start, double End)>();
                    foreach (var interval in remaining)
                    {
                        if (overlapEnd <= interval.Start + _tolerance ||
                            overlapStart >= interval.End - _tolerance)
                        {
                            next.Add(interval);
                            continue;
                        }
                        if (overlapStart > interval.Start + _tolerance)
                            next.Add((interval.Start, overlapStart));
                        if (overlapEnd < interval.End - _tolerance)
                            next.Add((overlapEnd, interval.End));
                    }
                    remaining = next;
                    if (remaining.Count == 0) break;
                }
                if (remaining.Count == 0) break;
            }

            if (!_segments.TryGetValue((angleBucket, offsetBucket), out var currentBucket))
            {
                currentBucket = new List<(double, double, double, double)>();
                _segments.Add((angleBucket, offsetBucket), currentBucket);
            }
            currentBucket.Add((x1, y1, x2, y2));
            return remaining
                .Where(interval => interval.End - interval.Start > _tolerance)
                .Select(interval =>
                {
                    double start = interval.Start / length, end = interval.End / length;
                    return (x1 + dx * start, y1 + dy * start, x1 + dx * end, y1 + dy * end);
                })
                .ToArray();
        }
    }

    public static void Write(IReadOnlyList<SheetResult> sheets, string path, DxfExportOptions options, double margin = 0)
    {
        if (sheets.Count == 0) throw new ArgumentException("Không có tấm nest để xuất.", nameof(sheets));
        options = options with { NestOrientation = ((options.NestOrientation % 360) + 360) % 360 };
        if (options.NestOrientation is not (0 or 90 or 180 or 270))
            throw new ArgumentOutOfRangeException(nameof(options.NestOrientation), "Hướng nest chỉ hỗ trợ 0°, 90°, 180° hoặc 270°.");
        if (options.ExportPartsAsBlocks && options.RemoveCommonEdges)
            throw new ArgumentException("Không thể vừa xuất Part dạng Block vừa xóa cạnh chung.", nameof(options));
        if (options.EnableMicroJoints &&
            (!double.IsFinite(options.MicroJointWidthMm) || options.MicroJointWidthMm <= 0 ||
             options.MicroJointCount is < 1 or > 1000 ||
             !double.IsFinite(options.MicroJointSmallMaxLengthMm) || options.MicroJointSmallMaxLengthMm <= 0 ||
             !double.IsFinite(options.MicroJointSmallMaxWidthMm) || options.MicroJointSmallMaxWidthMm <= 0 ||
             !double.IsFinite(options.MicroJointLargeMinLengthMm) ||
             options.MicroJointLargeMinLengthMm < options.MicroJointSmallMaxLengthMm ||
             !double.IsFinite(options.MicroJointLargeMinWidthMm) ||
             options.MicroJointLargeMinWidthMm < options.MicroJointSmallMaxWidthMm))
            throw new ArgumentOutOfRangeException(nameof(options.MicroJointWidthMm), "Thông số micro-joint hoặc ngưỡng phân loại contour không hợp lệ.");

        double unit = options.Units == DxfOutputUnits.Inches ? 1d / 25.4d : 1d;
        string acadVersion = VersionCode(options.TargetVersion);
        var layers = new Dictionary<string, DxfLayerColor>(StringComparer.OrdinalIgnoreCase)
        {
            ["0"] = new(7)
        };
        foreach (var shape in sheets.SelectMany(sheet => sheet.Placements).Select(placement => placement.Shape).OfType<DxfShape>())
        {
            foreach (var layer in shape.LayerColors) layers[layer.Key] = layer.Value;
            foreach (string layer in shape.Segments.Select(segment => segment.Layer).Distinct(StringComparer.OrdinalIgnoreCase))
                layers.TryAdd(layer, new DxfLayerColor(7));
            foreach (string layer in shape.Texts.Select(text => text.Layer).Distinct(StringComparer.OrdinalIgnoreCase))
                layers.TryAdd(layer, new DxfLayerColor(7));
        }
        string partLayer = AddLayer(SafeLayer(options.PartLayerName, "CUT"), new DxfLayerColor(7), layers);
        string rectLayer = options.PartsOnSingleLayer
            ? partLayer
            : AddLayer("PARTS", new DxfLayerColor(7), layers);
        string sheetLayer = AddLayer(SafeLayer(options.SheetLayerName, "SHEET"), new DxfLayerColor(4), layers);
        string labelLayer = AddLayer("LABELS", new DxfLayerColor(3), layers);
        string statsLayer = AddLayer("NEST_STATS", new DxfLayerColor(6), layers);
        var blocks = new List<BlockDefinition>();
        var modelEntities = new StringBuilder();
        var pageWidths = sheets.Select(s => OrientedSize(s.Width, s.Height, options.NestOrientation).Width).ToArray();
        double offsetMm = 0;

        for (int sheetIndex = 0; sheetIndex < sheets.Count; sheetIndex++)
        {
            var sheet = sheets[sheetIndex];
            var size = OrientedSize(sheet.Width, sheet.Height, options.NestOrientation);
            double modelOffset = options.ExportNestsAsBlocks ? 0 : offsetMm;
            var body = new StringBuilder();
            var commonLines = new CommonEdgeTracker(CommonEdgeToleranceMm * unit);

            if (options.OutputSheets)
                WriteRectangle(body, sheetLayer, 0, 0, size.Width, size.Height, unit, modelOffset, null);

            for (int placementIndex = 0; placementIndex < sheet.Placements.Count; placementIndex++)
            {
                var placement = sheet.Placements[placementIndex];
                string layer = options.PartsOnSingleLayer ? partLayer : placement.Shape == null ? rectLayer : "0";
                var segments = GetPlacedSegments(placement);
                var sourceTexts = placement.Shape?.Texts ?? Array.Empty<DxfTextEntity>();
                if (options.EnableMicroJoints)
                    segments = MicroJointProcessor.Apply(placement, segments, options.MicroJointWidthMm,
                        options.MicroJointCount, options.MicroJointSmallMaxLengthMm, options.MicroJointSmallMaxWidthMm,
                        options.MicroJointLargeMinLengthMm, options.MicroJointLargeMinWidthMm);

                if (options.ExportPartsAsBlocks)
                {
                    string blockName = $"P_{sheetIndex + 1:000}_{placementIndex + 1:000}";
                    var partBlockBody = new StringBuilder();
                    foreach (var segment in segments)
                    {
                        var local = TransformPartSegment(segment, placement, options.NestOrientation);
                        string segmentLayer = options.PartsOnSingleLayer ? partLayer : placement.Shape == null ? rectLayer : segment.Layer;
                        WriteSegment(partBlockBody, segmentLayer, local, options.GeometryStyle, unit, 0, null);
                    }
                    foreach (var sourceText in sourceTexts)
                    {
                        var placed = PlaceText(sourceText, placement);
                        var local = TransformPartText(placed, placement, options.NestOrientation);
                        if (options.PartsOnSingleLayer) local = local with { Layer = partLayer };
                        WriteTextEntity(partBlockBody, local, unit, 0, options.TargetVersion);
                    }
                    blocks.Add(new BlockDefinition(blockName, partBlockBody.ToString()));
                    var anchor = TransformPlacementAnchor(placement, sheet.Width, sheet.Height, options.NestOrientation);
                    WriteInsert(body, layer, blockName, anchor.X, anchor.Y, unit, modelOffset);
                }
                else
                {
                    foreach (var segment in segments)
                    {
                        var output = TransformSheetSegment(segment, sheet.Width, sheet.Height, options.NestOrientation);
                        string segmentLayer = options.PartsOnSingleLayer ? partLayer : placement.Shape == null ? rectLayer : segment.Layer;
                        WriteSegment(body, segmentLayer, output, options.GeometryStyle, unit, modelOffset,
                            options.RemoveCommonEdges ? commonLines : null);
                    }
                    foreach (var sourceText in sourceTexts)
                    {
                        var positioned = PlaceText(sourceText, placement);
                        var output = TransformSheetText(positioned, sheet.Width, sheet.Height, options.NestOrientation);
                        if (options.PartsOnSingleLayer) output = output with { Layer = partLayer };
                        WriteTextEntity(body, output, unit, modelOffset, options.TargetVersion);
                    }
                }

                if (options.AddPartLabels)
                {
                    var anchor = TransformPlacementAnchor(placement, sheet.Width, sheet.Height, options.NestOrientation);
                    double labelHeight = Math.Clamp(Math.Min(placement.Width, placement.Height) / 5, 1.5, 20);
                    string label = DxfWriter.ToAscii(placement.PartName) + (placement.IsRotated ? " (R)" : "");
                    WriteText(body, labelLayer, anchor.X + labelHeight * 0.4, anchor.Y + Math.Min(placement.Width, placement.Height) / 2,
                        labelHeight, label, unit, modelOffset);
                }
            }

            if (options.AddNestStatistics)
            {
                string stats = DxfWriter.ToAscii($"Nest {sheet.Index} | {sheet.Material} | {sheet.Thickness:0.###} mm | {sheet.Placements.Count} parts | {sheet.Utilization:P1}");
                WriteText(body, statsLayer, 0, size.Height + 18, 10, stats, unit, modelOffset);
            }

            if (options.ExportNestsAsBlocks)
            {
                string nestBlockName = $"NEST_{sheetIndex + 1:000}";
                blocks.Add(new BlockDefinition(nestBlockName, body.ToString()));
                WriteInsert(modelEntities, "0", nestBlockName, offsetMm, 0, unit, 0);
            }
            else modelEntities.Append(body);

            offsetMm += pageWidths[sheetIndex] + SheetGapMm;
        }

        var text = BuildDocument(layers, blocks, modelEntities.ToString(), acadVersion, options, unit);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static string BuildDocument(Dictionary<string, DxfLayerColor> layers, List<BlockDefinition> blocks,
        string entities, string acadVersion, DxfExportOptions options, double unit)
    {
        var sb = new StringBuilder(entities.Length + 32_000);
        void G(int code, string value) { sb.Append(code.ToString(Inv).PadLeft(3)).Append("\r\n").Append(value).Append("\r\n"); }
        void N(int code, double value) => G(code, value.ToString("0.########", Inv));

        G(0, "SECTION"); G(2, "HEADER"); G(9, "$ACADVER"); G(1, acadVersion);
        if (options.TargetVersion != DxfTargetVersion.R12)
        {
            G(9, "$DWGCODEPAGE"); G(3, "UTF-8");
            G(9, "$INSUNITS"); G(70, options.Units == DxfOutputUnits.Inches ? "1" : "4");
            G(9, "$MEASUREMENT"); G(70, options.Units == DxfOutputUnits.Inches ? "0" : "1");
        }
        G(0, "ENDSEC");

        G(0, "SECTION"); G(2, "TABLES");
        G(0, "TABLE"); G(2, "LTYPE"); G(70, "1");
        G(0, "LTYPE"); G(2, "CONTINUOUS"); G(70, "0"); G(3, "Solid line"); G(72, "65"); G(73, "0"); N(40, 0);
        G(0, "ENDTAB");
        G(0, "TABLE"); G(2, "LAYER"); G(70, layers.Count.ToString(Inv));
        foreach (var (name, color) in layers)
        {
            G(0, "LAYER"); G(2, name); G(70, "0"); G(62, color.Aci.ToString(Inv)); G(6, "CONTINUOUS");
            if (options.TargetVersion != DxfTargetVersion.R12 && color.TrueColor is { } trueColor)
                G(420, trueColor.ToString(Inv));
        }
        G(0, "ENDTAB");
        if (blocks.Count > 0 && options.TargetVersion != DxfTargetVersion.R12)
        {
            G(0, "TABLE"); G(2, "BLOCK_RECORD"); G(70, (blocks.Count + 2).ToString(Inv));
            foreach (string name in new[] { "*Model_Space", "*Paper_Space" }.Concat(blocks.Select(b => b.Name)))
            {
                G(0, "BLOCK_RECORD"); G(2, name); G(70, "0"); G(280, "1"); G(281, "0");
            }
            G(0, "ENDTAB");
        }
        G(0, "ENDSEC");

        if (blocks.Count > 0)
        {
            G(0, "SECTION"); G(2, "BLOCKS");
            foreach (var block in blocks)
            {
                G(0, "BLOCK"); G(8, "0"); G(2, block.Name); G(70, "0"); N(10, 0); N(20, 0); N(30, 0); G(3, block.Name); G(1, "");
                sb.Append(block.Contents);
                G(0, "ENDBLK"); G(8, "0");
            }
            G(0, "ENDSEC");
        }
        G(0, "SECTION"); G(2, "ENTITIES"); sb.Append(entities); G(0, "ENDSEC"); G(0, "EOF");
        return sb.ToString();
    }

    private static List<Seg> GetPlacedSegments(Placement p)
    {
        if (p.Shape != null) return p.Shape.Place(p.X, p.Y, p.Rotated).ToList();
        double x = p.X, y = p.Y, right = p.X + p.Width, top = p.Y + p.Height;
        return new List<Seg>
        {
            new LineSeg(x, y, right, y), new LineSeg(right, y, right, top),
            new LineSeg(right, top, x, top), new LineSeg(x, top, x, y)
        };
    }

    private static (double Width, double Height) OrientedSize(double width, double height, int angle) => angle is 90 or 270 ? (height, width) : (width, height);

    private static (double X, double Y) TransformSheetPoint(double x, double y, double sheetWidth, double sheetHeight, int angle) => angle switch
    {
        90 => (y, sheetWidth - x),
        180 => (sheetWidth - x, sheetHeight - y),
        270 => (sheetHeight - y, x),
        _ => (x, y)
    };

    private static Seg TransformSheetSegment(Seg segment, double sheetWidth, double sheetHeight, int angle)
    {
        (double X, double Y) T(double x, double y) => TransformSheetPoint(x, y, sheetWidth, sheetHeight, angle);
        double radians = angle * Math.PI / 180;
        return segment switch
        {
            LineSeg l => TransformLine(l, T),
            CircleSeg c => TransformCircle(c, T),
            ArcSeg a => TransformArc(a, T, radians),
            _ => segment
        };
    }

    private static (double X, double Y) TransformPlacementAnchor(Placement p, double sheetWidth, double sheetHeight, int angle)
    {
        var corners = new[]
        {
            TransformSheetPoint(p.X, p.Y, sheetWidth, sheetHeight, angle),
            TransformSheetPoint(p.X + p.Width, p.Y, sheetWidth, sheetHeight, angle),
            TransformSheetPoint(p.X, p.Y + p.Height, sheetWidth, sheetHeight, angle),
            TransformSheetPoint(p.X + p.Width, p.Y + p.Height, sheetWidth, sheetHeight, angle)
        };
        return (corners.Min(p => p.X), corners.Min(p => p.Y));
    }

    private static Seg TransformPartSegment(Seg segment, Placement p, int angle)
    {
        (double X, double Y) T(double x, double y)
        {
            double dx = x - p.X, dy = y - p.Y;
            return angle switch
            {
                90 => (dy, p.Width - dx),
                180 => (p.Width - dx, p.Height - dy),
                270 => (p.Height - dy, dx),
                _ => (dx, dy)
            };
        }
        return segment switch
        {
            LineSeg l => TransformLine(l, T),
            CircleSeg c => TransformCircle(c, T),
            ArcSeg a => TransformArc(a, T, angle * Math.PI / 180),
            _ => segment
        };
    }

    private static DxfTextEntity PlaceText(DxfTextEntity text, Placement p)
    {
        if (p.Shape is not { } shape)
            return text with
            {
                X = text.X + p.X,
                Y = text.Y + p.Y,
                AlignmentX = text.AlignmentX is { } alignX ? alignX + p.X : null,
                AlignmentY = text.AlignmentY is { } alignY ? alignY + p.Y : null
            };

        (double X, double Y) Transform(double x, double y)
        {
            double localX = x - shape.MinX, localY = y - shape.MinY;
            return p.Rotated
                ? (p.X + shape.Height - localY, p.Y + localX)
                : (p.X + localX, p.Y + localY);
        }

        var point = Transform(text.X, text.Y);
        double? alignmentX = null, alignmentY = null;
        if (text.AlignmentX is { } sourceX && text.AlignmentY is { } sourceY)
        {
            var alignment = Transform(sourceX, sourceY);
            alignmentX = alignment.X;
            alignmentY = alignment.Y;
        }
        return text with
        {
            X = point.X,
            Y = point.Y,
            Rotation = text.Rotation + (p.Rotated ? 90 : 0),
            AlignmentX = alignmentX,
            AlignmentY = alignmentY
        };
    }

    private static DxfTextEntity TransformPartText(DxfTextEntity text, Placement p, int angle)
        => TransformText(text, (x, y) =>
        {
            double dx = x - p.X, dy = y - p.Y;
            return angle switch
            {
                90 => (dy, p.Width - dx),
                180 => (p.Width - dx, p.Height - dy),
                270 => (p.Height - dy, dx),
                _ => (dx, dy)
            };
        }, angle);

    private static DxfTextEntity TransformSheetText(DxfTextEntity text, double sheetWidth, double sheetHeight, int angle)
    {
        (double X, double Y) Transform(double x, double y) =>
            TransformSheetPoint(x, y, sheetWidth, sheetHeight, angle);
        return TransformText(text, Transform, angle);
    }

    private static DxfTextEntity TransformText(DxfTextEntity text,
        Func<double, double, (double X, double Y)> transform, int angle)
    {
        var point = transform(text.X, text.Y);
        double? alignmentX = null, alignmentY = null;
        if (text.AlignmentX is { } sourceX && text.AlignmentY is { } sourceY)
        {
            var alignment = transform(sourceX, sourceY);
            alignmentX = alignment.X;
            alignmentY = alignment.Y;
        }
        return text with
        {
            X = point.X,
            Y = point.Y,
            Rotation = text.Rotation - angle,
            AlignmentX = alignmentX,
            AlignmentY = alignmentY
        };
    }

    private static LineSeg TransformLine(LineSeg line, Func<double, double, (double X, double Y)> t)
    {
        var a = t(line.X1, line.Y1); var b = t(line.X2, line.Y2);
        return new LineSeg(a.X, a.Y, b.X, b.Y, line.Layer);
    }

    private static CircleSeg TransformCircle(CircleSeg circle, Func<double, double, (double X, double Y)> t)
    {
        var c = t(circle.Cx, circle.Cy);
        return new CircleSeg(c.X, c.Y, circle.R, circle.Layer);
    }

    private static ArcSeg TransformArc(ArcSeg arc, Func<double, double, (double X, double Y)> t, double rotation)
    {
        var c = t(arc.Cx, arc.Cy);
        return new ArcSeg(c.X, c.Y, arc.R, arc.StartRad - rotation, arc.SweepRad, arc.Layer);
    }

    private static void WriteSegment(StringBuilder sb, string layer, Seg segment, DxfGeometryStyle style,
        double unit, double offset, CommonEdgeTracker? commonLines)
    {
        switch (segment)
        {
            case LineSeg line:
                WriteLine(sb, layer, line.X1, line.Y1, line.X2, line.Y2, unit, offset, commonLines);
                break;
            case CircleSeg circle when style == DxfGeometryStyle.BasicLinesArcs:
                Entity(sb, "CIRCLE", layer); Number(sb, 10, (circle.Cx + offset) * unit); Number(sb, 20, circle.Cy * unit); Number(sb, 30, 0); Number(sb, 40, circle.R * unit);
                break;
            case ArcSeg arc when style == DxfGeometryStyle.BasicLinesArcs:
                WriteArc(sb, layer, arc, unit, offset);
                break;
            case CircleSeg circle:
                WriteCurveAsLines(sb, layer, circle.Cx, circle.Cy, circle.R, 0, Math.PI * 2, unit, offset, commonLines);
                break;
            case ArcSeg arc:
                WriteCurveAsLines(sb, layer, arc.Cx, arc.Cy, arc.R, arc.StartRad, arc.SweepRad, unit, offset, commonLines);
                break;
        }
    }

    private static void WriteCurveAsLines(StringBuilder sb, string layer, double cx, double cy, double radius,
        double start, double sweep, double unit, double offset, CommonEdgeTracker? commonLines)
    {
        int count = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 36)), 8, 360);
        double x0 = cx + radius * Math.Cos(start), y0 = cy + radius * Math.Sin(start);
        for (int i = 1; i <= count; i++)
        {
            double angle = start + sweep * i / count;
            double x1 = cx + radius * Math.Cos(angle), y1 = cy + radius * Math.Sin(angle);
            WriteLine(sb, layer, x0, y0, x1, y1, unit, offset, commonLines);
            x0 = x1; y0 = y1;
        }
    }

    private static void WriteArc(StringBuilder sb, string layer, ArcSeg arc, double unit, double offset)
    {
        double start = arc.SweepRad >= 0 ? arc.StartRad : arc.StartRad + arc.SweepRad;
        double sweep = Math.Abs(arc.SweepRad);
        double end = start + sweep;
        Entity(sb, "ARC", layer);
        Number(sb, 10, (arc.Cx + offset) * unit); Number(sb, 20, arc.Cy * unit); Number(sb, 30, 0); Number(sb, 40, arc.R * unit);
        Number(sb, 50, NormalizeDegrees(start * 180 / Math.PI)); Number(sb, 51, NormalizeDegrees(end * 180 / Math.PI));
    }

    private static void WriteRectangle(StringBuilder sb, string layer, double x, double y, double width, double height,
        double unit, double offset, CommonEdgeTracker? commonLines)
    {
        WriteLine(sb, layer, x, y, x + width, y, unit, offset, commonLines);
        WriteLine(sb, layer, x + width, y, x + width, y + height, unit, offset, commonLines);
        WriteLine(sb, layer, x + width, y + height, x, y + height, unit, offset, commonLines);
        WriteLine(sb, layer, x, y + height, x, y, unit, offset, commonLines);
    }

    private static void WriteLine(StringBuilder sb, string layer, double x1, double y1, double x2, double y2,
        double unit, double offset, CommonEdgeTracker? commonLines)
    {
        double ax = (x1 + offset) * unit, ay = y1 * unit, bx = (x2 + offset) * unit, by = y2 * unit;
        if (commonLines != null)
        {
            foreach (var line in commonLines.AddAndGetUncovered(ax, ay, bx, by))
                WriteLineEntity(sb, layer, line.X1, line.Y1, line.X2, line.Y2);
            return;
        }
        WriteLineEntity(sb, layer, ax, ay, bx, by);
    }

    private static void WriteLineEntity(StringBuilder sb, string layer, double ax, double ay, double bx, double by)
    {
        Entity(sb, "LINE", layer); Number(sb, 10, ax); Number(sb, 20, ay); Number(sb, 30, 0); Number(sb, 11, bx); Number(sb, 21, by); Number(sb, 31, 0);
    }

    private static void WriteText(StringBuilder sb, string layer, double x, double y, double height, string text, double unit, double offset)
    {
        Entity(sb, "TEXT", layer); Number(sb, 10, (x + offset) * unit); Number(sb, 20, y * unit); Number(sb, 30, 0); Number(sb, 40, height * unit); Pair(sb, 1, text);
    }

    private static void WriteTextEntity(StringBuilder sb, DxfTextEntity text, double unit, double offset, DxfTargetVersion version)
    {
        if (text.Type == "MTEXT" && version == DxfTargetVersion.R12)
        {
            string plainText = StripMTextFormatting(text.Content);
            string[] lines = plainText.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                WriteSourceText(sb, text, lines[i], text.X, text.Y - i * text.Height * 1.5, unit, offset);
            return;
        }

        if (text.Type == "MTEXT")
        {
            Entity(sb, "MTEXT", text.Layer);
            Number(sb, 10, (text.X + offset) * unit);
            Number(sb, 20, text.Y * unit);
            Number(sb, 30, 0);
            Number(sb, 40, text.Height * unit);
            Number(sb, 41, text.Width * unit);
            Pair(sb, 71, text.Attachment.ToString(Inv));
            Pair(sb, 72, "1");
            Pair(sb, 7, text.Style);
            Number(sb, 50, NormalizeDegrees(text.Rotation));
            WriteMTextContent(sb, text.Content);
            return;
        }

        WriteSourceText(sb, text, text.Content, text.X, text.Y, unit, offset);
    }

    private static void WriteSourceText(StringBuilder sb, DxfTextEntity text, string content, double x, double y,
        double unit, double offset)
    {
        Entity(sb, "TEXT", text.Layer);
        Number(sb, 10, (x + offset) * unit);
        Number(sb, 20, y * unit);
        Number(sb, 30, 0);
        Number(sb, 40, text.Height * unit);
        Pair(sb, 1, content);
        Pair(sb, 7, text.Style);
        Number(sb, 50, NormalizeDegrees(text.Rotation));
        if (text.AlignmentX is { } alignX && text.AlignmentY is { } alignY)
        {
            Pair(sb, 72, text.HorizontalAlignment.ToString(Inv));
            Pair(sb, 73, text.VerticalAlignment.ToString(Inv));
            Number(sb, 11, (alignX + offset) * unit);
            Number(sb, 21, alignY * unit);
            Number(sb, 31, 0);
        }
    }

    private static void WriteMTextContent(StringBuilder sb, string content)
    {
        for (int offset = 0; offset < content.Length;)
        {
            int length = Math.Min(250, content.Length - offset);
            bool last = offset + length == content.Length;
            Pair(sb, last ? 1 : 3, content.Substring(offset, length));
            offset += length;
        }
        if (content.Length == 0) Pair(sb, 1, "");
    }

    private static string StripMTextFormatting(string content)
    {
        var result = new StringBuilder(content.Length);
        for (int i = 0; i < content.Length; i++)
        {
            char current = content[i];
            if (current == '{' || current == '}') continue;
            if (current != '\\' || i + 1 >= content.Length)
            {
                result.Append(current);
                continue;
            }

            char command = content[++i];
            if (command == 'P') { result.Append('\n'); continue; }
            if (command == '~') { result.Append(' '); continue; }
            if (command == '\\' || command == '{' || command == '}') { result.Append(command); continue; }
            if (command is 'L' or 'l' or 'O' or 'o') continue;
            if (command == 'U' && i + 1 < content.Length && content[i + 1] == '+')
            {
                result.Append("\\U+");
                i++;
                while (i + 1 < content.Length && Uri.IsHexDigit(content[i + 1])) result.Append(content[++i]);
                continue;
            }
            if ("ACFHQTWSfp".Contains(command))
            {
                while (i + 1 < content.Length && content[i + 1] != ';' && content[i + 1] != '\\') i++;
                if (i + 1 < content.Length && content[i + 1] == ';') i++;
                continue;
            }
            result.Append('\\').Append(command);
        }
        return result.ToString();
    }

    private static void WriteInsert(StringBuilder sb, string layer, string name, double x, double y, double unit, double offset)
    {
        Entity(sb, "INSERT", layer); Pair(sb, 2, name); Number(sb, 10, (x + offset) * unit); Number(sb, 20, y * unit); Number(sb, 30, 0);
        Number(sb, 41, 1); Number(sb, 42, 1); Number(sb, 43, 1); Number(sb, 50, 0);
    }

    private static void Entity(StringBuilder sb, string type, string layer) { Pair(sb, 0, type); Pair(sb, 8, layer); }
    private static void Pair(StringBuilder sb, int code, string value) => sb.Append(code.ToString(Inv).PadLeft(3)).Append("\r\n").Append(value).Append("\r\n");
    private static void Number(StringBuilder sb, int code, double value) => Pair(sb, code, value.ToString("0.########", Inv));

    private static double NormalizeDegrees(double value)
    {
        value %= 360;
        return value < 0 ? value + 360 : value;
    }

    private static string SafeLayer(string? name, string fallback)
    {
        string value = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        foreach (char c in "<>/\\\":;?*|=") value = value.Replace(c, '_');
        return value.Length > 31 ? value[..31] : value;
    }

    private static string AddLayer(string name, DxfLayerColor color, Dictionary<string, DxfLayerColor> layers)
    {
        string candidate = name;
        int suffix = 2;
        while (layers.ContainsKey(candidate))
        {
            string tail = $"_{suffix++}";
            candidate = name[..Math.Min(name.Length, 31 - tail.Length)] + tail;
        }
        layers.Add(candidate, color);
        return candidate;
    }

    private static string VersionCode(DxfTargetVersion version) => version switch
    {
        DxfTargetVersion.AutoCAD2000 => "AC1015",
        DxfTargetVersion.AutoCAD2007 => "AC1021",
        DxfTargetVersion.AutoCAD2010 => "AC1024",
        DxfTargetVersion.AutoCAD2018 => "AC1032",
        _ => "AC1009"
    };

}
