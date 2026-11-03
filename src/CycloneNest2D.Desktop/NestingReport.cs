using System.Globalization;
using CycloneNest2D.Core;
using CycloneNest2D.UI;

namespace CycloneNest2D.Desktop;

internal enum NestingPageOrientation
{
    Landscape,
    Portrait
}

internal enum NestingPageLayout
{
    OnePerPage,
    TwoPerPage
}

internal enum ReportPageSelection
{
    AllPages,
    OddPages,
    PageRange
}

internal sealed record NestingReportPart(
    int Id, string Name, double Width, double Height, double Thickness, double AreaPerPart, int QuantityOnSheet);

internal sealed class NestingSheetSummary
{
    public required SheetResult Sheet { get; init; }
    public required int NestNumber { get; init; }
    public required int NestCount { get; init; }
    public required double SheetArea { get; init; }
    public required double UsedPartArea { get; init; }
    public required double NestEfficiency { get; init; }
    public required double SheetUtilization { get; init; }
    public required double WasteRate { get; init; }
    public required IReadOnlyList<NestingReportPart> Parts { get; init; }
}

internal sealed class NestingReportDocument
{
    public required IReadOnlyList<NestingSheetSummary> SheetSummaries { get; init; }
    public required IReadOnlyDictionary<string, int> PartIds { get; init; }
    public DateTime? CompletedAt { get; init; }

    public static string SquareMeters(double squareMillimeters) =>
        (squareMillimeters / 1_000_000d).ToString("0.00", CultureInfo.CurrentCulture);
}

internal static class NestingReportBuilder
{
    public static NestingReportDocument Build(MainViewModel vm)
    {
        var result = vm.Result ?? throw new InvalidOperationException("Chưa có kết quả nesting.");
        if (result.Sheets.Count == 0) throw new InvalidOperationException("Kết quả nesting không có tấm.");

        var allPlacements = result.Sheets.SelectMany(sheet => sheet.Placements).ToList();
        var names = allPlacements.Select(placement => placement.PartName)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ids = names.Select((name, index) => (name, id: index + 1))
            .ToDictionary(item => item.name, item => item.id, StringComparer.OrdinalIgnoreCase);
        var sourceParts = vm.Parts.GroupBy(part => part.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var summaries = new List<NestingSheetSummary>(result.Sheets.Count);
        for (int sheetIndex = 0; sheetIndex < result.Sheets.Count; sheetIndex++)
        {
            var sheet = result.Sheets[sheetIndex];
            double sheetArea = sheet.Width * sheet.Height;
            bool lengthAlongX = sheet.Width >= sheet.Height;
            double maxLengthEdge = sheet.Placements.Count == 0 ? 0 : sheet.Placements.Max(placement =>
                lengthAlongX ? placement.X + placement.Width : placement.Y + placement.Height);
            double usedLength = Math.Max(0, maxLengthEdge - vm.Settings.Margin);
            double usableShortSide = Math.Max(0, Math.Min(sheet.Width, sheet.Height) - 2 * vm.Settings.Margin);
            double envelopeArea = usableShortSide * usedLength;
            double utilization = sheetArea > 0 ? Math.Clamp(sheet.UsedArea / sheetArea, 0, 1) : 0;
            double efficiency = envelopeArea > 0 ? Math.Clamp(sheet.UsedArea / envelopeArea, 0, 1) : 0;
            var sheetParts = sheet.Placements
                .GroupBy(placement => placement.PartName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => ids.GetValueOrDefault(group.Key, int.MaxValue))
                .Select(group =>
                {
                    sourceParts.TryGetValue(group.Key, out var source);
                    int count = group.Count();
                    return new NestingReportPart(
                        ids.GetValueOrDefault(group.Key), group.Key,
                        source?.Width ?? group.First().Width,
                        source?.Height ?? group.First().Height,
                        source?.Thickness ?? 0,
                        group.Sum(placement => placement.NetArea) / Math.Max(1, count),
                        count);
                }).ToList();

            summaries.Add(new NestingSheetSummary
            {
                Sheet = sheet,
                NestNumber = sheetIndex + 1,
                NestCount = result.Sheets.Count,
                SheetArea = sheetArea,
                UsedPartArea = sheet.UsedArea,
                NestEfficiency = efficiency,
                SheetUtilization = utilization,
                WasteRate = 1 - utilization,
                Parts = sheetParts
            });
        }

        return new NestingReportDocument
        {
            SheetSummaries = summaries,
            PartIds = ids,
            CompletedAt = vm.LastNestAt
        };
    }
}

internal sealed record NestingReportPageRef(int SheetIndex, int PartPageIndex, int Slot);
internal sealed record NestingReportPage(IReadOnlyList<NestingReportPageRef> Reports, int SlotCount);

internal static class NestingReportPagePlan
{
    public static double PageWidthDip(NestingPageOrientation orientation) =>
        orientation == NestingPageOrientation.Landscape ? 1122 : 794;

    public static double PageHeightDip(NestingPageOrientation orientation) =>
        orientation == NestingPageOrientation.Landscape ? 794 : 1122;

    public static double ContentMarginDip(NestingPageOrientation orientation) => PageWidthDip(orientation) * 0.06;

    public static double CardHeightDip(NestingPageOrientation orientation, int slotCount) =>
        (PageHeightDip(orientation) - 2 * ContentMarginDip(orientation)) /
        slotCount;

    public static double FirstChartTopDip(NestingPageLayout layout) =>
        layout == NestingPageLayout.TwoPerPage ? 66 : 78;

    public static double FirstChartMinimumHeightDip(NestingPageOrientation orientation, NestingPageLayout layout) =>
        layout == NestingPageLayout.TwoPerPage
            ? orientation == NestingPageOrientation.Landscape ? 105 : 175
            : orientation == NestingPageOrientation.Landscape ? 190 : 270;

    public static double FirstTableGapDip(NestingPageLayout layout) =>
        layout == NestingPageLayout.TwoPerPage ? 14 : 18;

    public static double FirstTableHeaderHeightDip(NestingPageLayout layout) =>
        layout == NestingPageLayout.TwoPerPage ? 18 : 22;

    public static double FirstTableRowHeightDip(NestingPageOrientation orientation, NestingPageLayout layout) =>
        layout == NestingPageLayout.TwoPerPage
            ? orientation == NestingPageOrientation.Landscape ? 13 : 15
            : orientation == NestingPageOrientation.Landscape ? 19 : 18;

    public static double FirstPageBottomPaddingDip(NestingPageLayout layout) =>
        layout == NestingPageLayout.TwoPerPage ? 22 : 28;

    public static double ContinuationTitleTopDip(NestingPageLayout layout) =>
        layout == NestingPageLayout.TwoPerPage ? 28 : 48;

    public static double ContinuationTableTopDip(NestingPageLayout layout) =>
        layout == NestingPageLayout.TwoPerPage ? 50 : 75;

    public static double ContinuationTableHeaderHeightDip(NestingPageLayout layout) =>
        layout == NestingPageLayout.TwoPerPage ? 16 : 20;

    public static double ContinuationTableRowHeightDip(NestingPageOrientation orientation, NestingPageLayout layout) =>
        layout == NestingPageLayout.TwoPerPage
            ? orientation == NestingPageOrientation.Landscape ? 12 : 14
            : orientation == NestingPageOrientation.Landscape ? 17 : 16;

    public static int FirstPageRows(NestingPageOrientation orientation, NestingPageLayout layout)
    {
        double height = CardHeightDip(orientation, layout == NestingPageLayout.TwoPerPage ? 2 : 1);
        double availableRows = height - FirstChartTopDip(layout) -
            FirstChartMinimumHeightDip(orientation, layout) - FirstTableGapDip(layout) -
            FirstTableHeaderHeightDip(layout) - FirstPageBottomPaddingDip(layout);
        return Math.Max(1, (int)Math.Floor(availableRows / FirstTableRowHeightDip(orientation, layout)) - 1);
    }

    public static int ContinuationPageRows(NestingPageOrientation orientation, NestingPageLayout layout)
    {
        double height = CardHeightDip(orientation, layout == NestingPageLayout.TwoPerPage ? 2 : 1);
        double availableRows = height - ContinuationTableTopDip(layout) -
            ContinuationTableHeaderHeightDip(layout) - FirstPageBottomPaddingDip(layout);
        return Math.Max(1, (int)Math.Floor(availableRows / ContinuationTableRowHeightDip(orientation, layout)) - 1);
    }

    public static IReadOnlyList<NestingReportPage> Build(
        NestingReportDocument report, NestingPageOrientation orientation, NestingPageLayout layout)
    {
        int firstPageRows = FirstPageRows(orientation, layout);
        int continuationPageRows = ContinuationPageRows(orientation, layout);
        var pages = new List<NestingReportPage>();

        if (layout == NestingPageLayout.OnePerPage)
        {
            for (int sheetIndex = 0; sheetIndex < report.SheetSummaries.Count; sheetIndex++)
            {
                int remainingRows = Math.Max(0, report.SheetSummaries[sheetIndex].Parts.Count - firstPageRows);
                pages.Add(new NestingReportPage([new NestingReportPageRef(sheetIndex, 0, 0)], 1));
                int continuationPageIndex = 1;
                while (remainingRows > 0)
                {
                    pages.Add(new NestingReportPage(
                        [new NestingReportPageRef(sheetIndex, continuationPageIndex++, 0)], 1));
                    remainingRows -= continuationPageRows;
                }
            }
            return pages;
        }

        for (int firstSheet = 0; firstSheet < report.SheetSummaries.Count; firstSheet += 2)
        {
            int secondSheet = firstSheet + 1;
            int firstSheetPages = SheetPageCount(report.SheetSummaries[firstSheet], firstPageRows, continuationPageRows);
            int secondSheetPages = secondSheet < report.SheetSummaries.Count
                ? SheetPageCount(report.SheetSummaries[secondSheet], firstPageRows, continuationPageRows)
                : 0;
            int pairedPages = Math.Max(firstSheetPages, secondSheetPages);
            for (int partPage = 0; partPage < pairedPages; partPage++)
            {
                var reports = new List<NestingReportPageRef>(2);
                if (partPage < firstSheetPages)
                    reports.Add(new NestingReportPageRef(firstSheet, partPage, 0));
                if (partPage < secondSheetPages)
                    reports.Add(new NestingReportPageRef(secondSheet, partPage, 1));
                pages.Add(new NestingReportPage(reports, secondSheet < report.SheetSummaries.Count ? 2 : 1));
            }
        }
        return pages;
    }

    private static int SheetPageCount(NestingSheetSummary sheet, int firstPageRows, int continuationPageRows)
    {
        int continuationRows = Math.Max(0, sheet.Parts.Count - firstPageRows);
        return 1 + (int)Math.Ceiling(continuationRows / (double)continuationPageRows);
    }
}
