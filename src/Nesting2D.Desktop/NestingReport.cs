using System.Globalization;
using Nesting2D.Core;
using Nesting2D.UI;

namespace Nesting2D.Desktop;

internal enum NestingReportType
{
    SheetParameters,
    PartCatalog
}

internal enum NestingPageOrientation
{
    Landscape,
    Portrait
}

internal enum NestingPageLayout
{
    OnePerPage,
    TwoSheetReportsPerPage
}

internal enum ReportExportScope
{
    CurrentReport,
    BothReports
}

internal enum ReportPageSelection
{
    AllPages,
    OddPages,
    PageRange
}

internal sealed record NestingReportPart(
    int Id, string Name, double Width, double Height, double AreaPerPart,
    int ProducedInJob, int RequiredInJob, PartItem? SourcePart);

internal sealed class NestingSheetSummary
{
    public required SheetResult Sheet { get; init; }
    public required int NestNumber { get; init; }
    public required int NestCount { get; init; }
    public required double SheetArea { get; init; }
    public required double UsedLength { get; init; }
    public required double UsedPartArea { get; init; }
    public required double NestEfficiency { get; init; }
    public required double SheetUtilization { get; init; }
    public required double WasteRate { get; init; }
}

internal sealed class NestingReportDocument
{
    public required IReadOnlyList<NestingSheetSummary> SheetSummaries { get; init; }
    public required IReadOnlyList<NestingReportPart> Parts { get; init; }
    public required IReadOnlyDictionary<string, int> PartIds { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int TotalRequired => Parts.Sum(p => p.RequiredInJob);
    public int TotalProduced => Parts.Sum(p => p.ProducedInJob);

    public static string SquareMeters(double squareMillimeters) =>
        (squareMillimeters / 1_000_000d).ToString("0.00", CultureInfo.CurrentCulture);
}

internal static class NestingReportBuilder
{
    public static NestingReportDocument Build(MainViewModel vm)
    {
        var result = vm.Result ?? throw new InvalidOperationException("Chưa có kết quả nesting.");
        if (result.Sheets.Count == 0) throw new InvalidOperationException("Kết quả nesting không có tấm.");

        var allPlacements = result.Sheets.SelectMany(s => s.Placements).ToList();
        var names = allPlacements.Select(p => p.PartName)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ids = names.Select((name, index) => (name, id: index + 1))
            .ToDictionary(x => x.name, x => x.id, StringComparer.OrdinalIgnoreCase);
        var produced = allPlacements.GroupBy(p => p.PartName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var sourceParts = vm.Parts.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var required = vm.Parts.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(p => Math.Max(0, p.Quantity)), StringComparer.OrdinalIgnoreCase);

        var summaries = new List<NestingSheetSummary>(result.Sheets.Count);
        for (int sheetIndex = 0; sheetIndex < result.Sheets.Count; sheetIndex++)
        {
            var sheet = result.Sheets[sheetIndex];
            double sheetArea = sheet.Width * sheet.Height;
            bool lengthAlongX = sheet.Width >= sheet.Height;
            double maxLengthEdge = sheet.Placements.Count == 0 ? 0 : sheet.Placements.Max(p =>
                lengthAlongX ? p.X + p.Width : p.Y + p.Height);
            double usedLength = Math.Max(0, maxLengthEdge - vm.Settings.Margin);
            double usableShortSide = Math.Max(0, Math.Min(sheet.Width, sheet.Height) - 2 * vm.Settings.Margin);
            double envelopeArea = usableShortSide * usedLength;
            double utilization = sheetArea > 0 ? Math.Clamp(sheet.UsedArea / sheetArea, 0, 1) : 0;
            double efficiency = envelopeArea > 0 ? Math.Clamp(sheet.UsedArea / envelopeArea, 0, 1) : 0;

            summaries.Add(new NestingSheetSummary
            {
                Sheet = sheet,
                NestNumber = sheetIndex + 1,
                NestCount = result.Sheets.Count,
                SheetArea = sheetArea,
                UsedLength = usedLength,
                UsedPartArea = sheet.UsedArea,
                NestEfficiency = efficiency,
                SheetUtilization = utilization,
                WasteRate = 1 - utilization
            });
        }

        var catalog = allPlacements.GroupBy(p => p.PartName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => ids.TryGetValue(g.Key, out int id) ? id : int.MaxValue)
            .Select(g =>
            {
                sourceParts.TryGetValue(g.Key, out var source);
                int count = g.Count();
                return new NestingReportPart(
                    ids.GetValueOrDefault(g.Key), g.Key,
                    source?.Width ?? g.First().Width,
                    source?.Height ?? g.First().Height,
                    g.Sum(p => p.NetArea) / Math.Max(1, count),
                    produced.GetValueOrDefault(g.Key, count),
                    required.GetValueOrDefault(g.Key, produced.GetValueOrDefault(g.Key, count)),
                    source);
            }).ToList();

        return new NestingReportDocument
        {
            SheetSummaries = summaries,
            Parts = catalog,
            PartIds = ids,
            CompletedAt = vm.LastNestAt
        };
    }
}

internal sealed record NestingReportPageRef(NestingReportType Type, int SectionPageIndex);

internal static class NestingReportPagePlan
{
    public static int CatalogRowsPerPage(NestingPageOrientation orientation) =>
        orientation == NestingPageOrientation.Landscape ? 20 : 33;

    public static int PageCount(NestingReportDocument report, NestingReportType type,
        NestingPageOrientation orientation, NestingPageLayout layout)
    {
        if (type == NestingReportType.SheetParameters)
        {
            int perPage = layout == NestingPageLayout.TwoSheetReportsPerPage ? 2 : 1;
            return Math.Max(1, (int)Math.Ceiling(report.SheetSummaries.Count / (double)perPage));
        }
        return Math.Max(1, (int)Math.Ceiling(report.Parts.Count / (double)CatalogRowsPerPage(orientation)));
    }

    public static IReadOnlyList<NestingReportPageRef> Build(NestingReportDocument report,
        NestingReportType currentType, NestingPageOrientation orientation, NestingPageLayout layout,
        ReportExportScope scope)
    {
        var types = scope == ReportExportScope.BothReports
            ? new[] { NestingReportType.SheetParameters, NestingReportType.PartCatalog }
            : new[] { currentType };
        var pages = new List<NestingReportPageRef>();
        foreach (var type in types)
            for (int page = 0; page < PageCount(report, type, orientation, layout); page++)
                pages.Add(new NestingReportPageRef(type, page));
        return pages;
    }
}
