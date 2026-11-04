using System.Globalization;
using System.Text;
using CycloneNest2D.Core;

namespace CycloneNest2D.UI;

public sealed class NestingPatternItem
{
    public NestingPatternItem(int number, string name, IReadOnlyList<SheetItem> sheets)
    {
        Number = number;
        Name = name;
        Sheets = sheets;
        Representative = sheets[0];
    }

    public int Number { get; }
    public string Name { get; }
    public IReadOnlyList<SheetItem> Sheets { get; }
    public SheetItem Representative { get; }
    public SheetResult Sheet => Representative.Sheet;
    public string Material => Sheet.Material;
    public double Thickness => Sheet.Thickness;
    public int Quantity => Sheets.Count;
    public int Revision => Representative.Revision;
}

public sealed record NestingSheetGroup(string Material, double Thickness, IReadOnlyList<SheetItem> Sheets)
{
    public IReadOnlyList<NestingPatternItem> Patterns { get; init; } = Array.Empty<NestingPatternItem>();
    public int PatternCount => Patterns.Count;
    public string Label => $"{Material} · {Thickness:0.###} mm ({PatternCount} mẫu / {Sheets.Count} tấm)";
}

public static class NestingPatternGrouping
{
    public static IReadOnlyList<NestingPatternItem> Group(IEnumerable<SheetItem> sheets, int firstNumber = 1)
    {
        var groups = sheets.GroupBy(item => Signature(item.Sheet), StringComparer.Ordinal)
            .Select(group => group.ToList())
            .ToList();

        return groups.Select((group, index) =>
        {
            var sheet = group[0].Sheet;
            string material = string.IsNullOrWhiteSpace(sheet.Material) ? "Chưa phân loại" : sheet.Material.Trim();
            int number = firstNumber + index;
            string name = $"{number.ToString("0000", CultureInfo.InvariantCulture)}-{material} " +
                          $"{sheet.Thickness.ToString("0.###", CultureInfo.InvariantCulture)}-{group.Count}";
            return new NestingPatternItem(number, name, group);
        }).ToList();
    }

    private static string Signature(SheetResult sheet)
    {
        var builder = new StringBuilder();
        Append(builder, (sheet.Material ?? "").Trim().ToUpperInvariant());
        Append(builder, Math.Round(sheet.Thickness, 4));
        Append(builder, sheet.Width);
        Append(builder, sheet.Height);

        var placements = sheet.Placements.Select(PlacementSignature)
            .OrderBy(value => value, StringComparer.Ordinal);
        foreach (var placement in placements) Append(builder, placement);
        return builder.ToString();
    }

    private static string PlacementSignature(Placement placement)
    {
        var builder = new StringBuilder();
        Append(builder, placement.PartName.Trim().ToUpperInvariant());
        Append(builder, placement.X);
        Append(builder, placement.Y);
        Append(builder, placement.Width);
        Append(builder, placement.Height);
        Append(builder, placement.Rotated ? "1" : "0");
        Append(builder, placement.AngleDeg);
        Append(builder, placement.Shape?.Name ?? "");
        if (placement.Shape is { } shape)
        {
            Append(builder, shape.UnitScale);
            foreach (var segment in shape.Segments.Select(SegmentSignature).OrderBy(value => value, StringComparer.Ordinal))
                Append(builder, segment);
            foreach (var text in shape.Texts.Select(value => value.ToString()).OrderBy(value => value, StringComparer.Ordinal))
                Append(builder, text);
        }
        return builder.ToString();
    }

    private static string SegmentSignature(Seg segment) => segment switch
    {
        LineSeg line => $"L|{line.Layer}|{Format(line.X1)}|{Format(line.Y1)}|{Format(line.X2)}|{Format(line.Y2)}",
        CircleSeg circle => $"C|{circle.Layer}|{Format(circle.Cx)}|{Format(circle.Cy)}|{Format(circle.R)}",
        ArcSeg arc => $"A|{arc.Layer}|{Format(arc.Cx)}|{Format(arc.Cy)}|{Format(arc.R)}|{Format(arc.StartRad)}|{Format(arc.SweepRad)}",
        _ => segment.ToString() ?? segment.GetType().Name
    };

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static void Append(StringBuilder builder, double value) => Append(builder, Format(value));

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value).Append(';');
}
