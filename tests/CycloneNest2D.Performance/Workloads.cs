using CycloneNest2D.Core;

internal sealed record BenchmarkProfile(string Name, double RotationStep, double TargetUtilizationPercent)
{
    public static IReadOnlyList<BenchmarkProfile> All { get; } =
    [
        new("Balanced-90", 90, 85),
        new("Balanced-45", 45, 85),
        new("Exhaustive-90", 90, 0),
        new("Exhaustive-45", 45, 0)
    ];
}

internal sealed record BenchmarkWorkload(string Name, string Description, IReadOnlyList<PartDefinition> Parts, SheetDefinition Sheet);

internal static class Workloads
{
    public static List<BenchmarkWorkload> Create(BenchmarkConfig config)
    {
        var workloads = config.Full
            ? new List<BenchmarkWorkload>
            {
                Rectangles("Rect-60", 60, seed: 101, 1250, 2500, 3),
                Rectangles("Rect-250", 250, seed: 102, 1250, 2500, 3),
                Rectangles("Rect-500-large-sheet", 500, seed: 103, 3000, 6000, 3),
                MixedGeometry("Mixed-60", 60, spacing: 3),
                MixedGeometry("Mixed-120", 120, spacing: 3),
                MixedGeometry("Mixed-zero-gap", 60, spacing: 0),
                ManyGeometryTypes("Geometry-40-types", 40, spacing: 3)
            }
            : new List<BenchmarkWorkload>
            {
                Rectangles("Rect-60", 60, seed: 101, 1250, 2500, 3),
                MixedGeometry("Mixed-60", 60, spacing: 3),
                ManyGeometryTypes("Geometry-16-types", 32, spacing: 3)
            };

        if (config.DxfPath is not null)
            workloads.Add(ReadDxfWorkload(config));
        return workloads;
    }

    private static BenchmarkWorkload Rectangles(string name, int count, int seed,
        double sheetWidth, double sheetHeight, double spacing)
    {
        var random = new Random(seed);
        int typeCount = Math.Min(40, count);
        var parts = Enumerable.Range(0, typeCount).Select(index =>
        {
            double width = 60 + random.Next(0, 260);
            double height = 35 + random.Next(0, 180);
            int quantity = count / typeCount + (index < count % typeCount ? 1 : 0);
            return new PartDefinition($"Rect-{index + 1:00}", width, height, quantity);
        }).ToList();
        return new BenchmarkWorkload(name, $"{count} chi tiết chữ nhật / {typeCount} loại",
            parts, new SheetDefinition(sheetWidth, sheetHeight, 5, spacing));
    }

    private static BenchmarkWorkload MixedGeometry(string name, int count, double spacing)
    {
        var parts = new List<PartDefinition>
        {
            new("Panel", 300, 180, count * 30 / 100, true, Geometry.Rectangle("Panel", 300, 180)),
            new("Bracket-L", 220, 180, count * 25 / 100, true, Geometry.LShape("Bracket-L", 220, 180, 55)),
            new("Round", 150, 150, count * 25 / 100, true, Geometry.Ring("Round", 75, 38)),
            new("Plate", 120, 80, 0, true, Geometry.Rectangle("Plate", 120, 80)),
            new("Long-slot", 300, 65, 0, true, Geometry.LShape("Long-slot", 300, 65, 18))
        };
        int assigned = parts.Sum(part => part.Quantity);
        parts[3] = parts[3] with { Quantity = count - assigned };
        parts[4] = parts[4] with { Quantity = Math.Max(0, parts[4].Quantity) };
        return new BenchmarkWorkload(name, $"{count} chi tiết mix contour chữ nhật/L/ring, spacing {spacing:0.##} mm",
            parts.Where(part => part.Quantity > 0).ToList(), new SheetDefinition(1250, 2500, 5, spacing));
    }

    private static BenchmarkWorkload ManyGeometryTypes(string name, int count, double spacing)
    {
        var random = new Random(404);
        int typeCount = Math.Min(count, Math.Max(8, count / 2));
        var parts = Enumerable.Range(0, typeCount).Select(index =>
        {
            double width = 80 + random.Next(0, 150);
            double height = 60 + random.Next(0, 120);
            DxfShape shape = (index % 3) switch
            {
                0 => Geometry.LShape($"Contour-{index + 1}", width, height, Math.Min(width, height) * 0.3),
                1 => Geometry.Ring($"Contour-{index + 1}", Math.Min(width, height) / 2, Math.Min(width, height) / 5),
                _ => Geometry.Rectangle($"Contour-{index + 1}", width, height)
            };
            int quantity = count / typeCount + (index < count % typeCount ? 1 : 0);
            return new PartDefinition($"Contour-{index + 1:00}", shape.Width, shape.Height, quantity, true, shape);
        }).ToList();
        return new BenchmarkWorkload(name, $"{count} chi tiết contour thật / {typeCount} loại, xoay 45° khi profile cho phép",
            parts, new SheetDefinition(1250, 2500, 5, spacing));
    }

    private static BenchmarkWorkload ReadDxfWorkload(BenchmarkConfig config)
    {
        string[] paths = File.Exists(config.DxfPath!)
            ? [Path.GetFullPath(config.DxfPath!)]
            : Directory.GetFiles(config.DxfPath!, "*.dxf", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) throw new InvalidDataException($"Không tìm thấy file DXF trong '{config.DxfPath}'.");

        var parts = new List<PartDefinition>();
        foreach (string path in paths)
        {
            DxfPart part = DxfReader.Read(path);
            parts.Add(part.ToDefinition());
        }
        if (parts.Count == 0) throw new InvalidDataException("Không có chi tiết DXF nào để chạy benchmark.");
        return new BenchmarkWorkload("Real-DXF", $"{paths.Length} file DXF, {parts.Sum(part => part.Quantity)} instances",
            parts, new SheetDefinition(config.SheetWidth, config.SheetHeight, config.Margin, config.Spacing));
    }
}

internal static class Geometry
{
    private static readonly IReadOnlyDictionary<string, int> Layers = new Dictionary<string, int> { ["CUT"] = 1 };

    public static DxfShape Rectangle(string name, double width, double height) =>
        Polygon(name, [(0, 0), (width, 0), (width, height), (0, height)]);

    public static DxfShape LShape(string name, double width, double height, double thickness)
    {
        double t = Math.Clamp(thickness, 1, Math.Min(width, height) - 1);
        return Polygon(name, [(0, 0), (width, 0), (width, t), (t, t), (t, height), (0, height)]);
    }

    public static DxfShape Ring(string name, double outerRadius, double innerRadius)
    {
        var segments = new Seg[]
        {
            new CircleSeg(outerRadius, outerRadius, outerRadius, "CUT"),
            new CircleSeg(outerRadius, outerRadius, innerRadius, "CUT")
        };
        return new DxfShape(name, segments, 1, Layers, Array.Empty<string>(), 0);
    }

    private static DxfShape Polygon(string name, (double X, double Y)[] points)
    {
        var segments = new List<Seg>(points.Length);
        for (int i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            segments.Add(new LineSeg(a.X, a.Y, b.X, b.Y, "CUT"));
        }
        return new DxfShape(name, segments, 1, Layers, Array.Empty<string>(), 0);
    }
}
