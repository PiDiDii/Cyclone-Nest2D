using System.Globalization;
using System.IO;
using CycloneNest2D.Core;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace CycloneNest2D.Desktop;

internal static class PdfReportWriter
{
    private const double DipToPoint = 72d / 96d;
    private const double ContentMarginRatio = 0.06;
    private static readonly CultureInfo Culture = CultureInfo.CurrentCulture;

    public static void Write(NestingReportDocument report, string path, NestingPageOrientation orientation,
        NestingPageLayout layout, PartLabelMode partLabel, ReportPageSelection pageSelection, int fromPage, int toPage)
    {
        bool landscape = orientation == NestingPageOrientation.Landscape;
        var allPages = NestingReportPagePlan.Build(report, orientation, layout);
        var selectedPages = allPages.Select((page, index) => (Page: page, PageNumber: index + 1))
            .Where(item => pageSelection switch
            {
                ReportPageSelection.AllPages => true,
                ReportPageSelection.OddPages => item.PageNumber % 2 == 1,
                ReportPageSelection.PageRange => item.PageNumber >= fromPage && item.PageNumber <= toPage,
                _ => false
            }).ToList();
        if (selectedPages.Count == 0)
            throw new InvalidOperationException("Lựa chọn không bao gồm trang PDF nào.");

        var document = new PdfDocument();
        document.Info.Title = "Cyclone Nest2D - Báo cáo nesting theo tấm";
        document.Info.Subject = "Thông số và danh mục chi tiết theo từng tấm nesting";
        document.Info.Creator = "Cyclone Nest2D";

        foreach (var item in selectedPages)
        {
            var page = document.AddPage();
            page.Size = PageSize.A4;
            page.Orientation = landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
            using var gfx = XGraphics.FromPdfPage(page);
            double pageWidth = page.Width.Point;
            double pageHeight = page.Height.Point;
            gfx.DrawRectangle(XBrushes.White, 0, 0, pageWidth, pageHeight);
            DrawSheetReportPage(gfx, report, item.Page, orientation, layout, partLabel, pageWidth, pageHeight);
        }

        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        document.Save(path);
        using var check = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[5];
        if (check.Read(signature) != signature.Length || !signature.SequenceEqual("%PDF-"u8))
            throw new InvalidDataException("Tệp đã lưu không có chữ ký PDF hợp lệ.");
    }

    private static void DrawSheetReportPage(XGraphics gfx, NestingReportDocument report,
        NestingReportPage page, NestingPageOrientation orientation, NestingPageLayout layout,
        PartLabelMode partLabel, double pageWidth, double pageHeight)
    {
        double margin = pageWidth * ContentMarginRatio;
        double usableHeight = pageHeight - 2 * margin;
        double cardHeight = usableHeight / page.SlotCount;
        foreach (var pageRef in page.Reports)
        {
            double cardTop = margin + pageRef.Slot * cardHeight;
            DrawSheetReportCard(gfx, report, pageRef, orientation, layout, partLabel,
                cardTop, cardHeight, margin, pageWidth - 2 * margin);
        }

    }

    private static void DrawSheetReportCard(XGraphics gfx, NestingReportDocument report, NestingReportPageRef pageRef,
        NestingPageOrientation orientation, NestingPageLayout layout, PartLabelMode partLabel,
        double cardTop, double cardHeight, double left, double width)
    {
        var model = report.SheetSummaries[pageRef.SheetIndex];
        bool firstPage = pageRef.PartPageIndex == 0;
        bool compact = layout == NestingPageLayout.TwoPerPage;
        double titleTop = cardTop + P(compact ? 4 : 8);
        var titleFont = new XFont("Arial", compact ? 9.5 : 12.5, XFontStyleEx.Bold);
        var subtitleFont = new XFont("Arial", compact ? 6.8 : 8.3, XFontStyleEx.Bold);
        gfx.DrawString(firstPage ? "BÁO CÁO TẤM NESTING 2D" : "DANH MỤC CHI TIẾT · TIẾP THEO",
            titleFont, Color(23, 43, 77), new XRect(left, titleTop, width, P(compact ? 18 : 24)), XStringFormats.CenterLeft);
        string line = $"Nest {model.NestNumber} / {model.NestCount} · {model.Sheet.Material} · {model.Sheet.Thickness:0.##} mm · {model.Sheet.Width:0.##} × {model.Sheet.Height:0.##} mm";
        double subtitleTop = titleTop + P(compact ? 19 : 26);
        gfx.DrawString(line, FitFont(gfx, line, subtitleFont, width), Color(52, 68, 91),
            new XRect(left, subtitleTop, width, P(compact ? 16 : 19)), XStringFormats.CenterLeft);

        if (firstPage)
        {
            double metricTop = cardTop + P(compact ? 42 : 56);
            DrawMetrics(gfx, model, left, metricTop, width, compact);
            double chartTop = cardTop + P(NestingReportPagePlan.FirstChartTopDip(layout));
            double tableGap = P(NestingReportPagePlan.FirstTableGapDip(layout));
            double headerHeight = P(NestingReportPagePlan.FirstTableHeaderHeightDip(layout));
            double rowHeight = P(NestingReportPagePlan.FirstTableRowHeightDip(orientation, layout));
            int tableRows = Math.Min(NestingReportPagePlan.FirstPageRows(orientation, layout), model.Parts.Count);
            double cardHeightDip = cardHeight / DipToPoint;
            double availableChartHeight = Math.Max(0, cardHeightDip -
                NestingReportPagePlan.FirstChartTopDip(layout) -
                NestingReportPagePlan.FirstTableGapDip(layout) -
                NestingReportPagePlan.FirstTableHeaderHeightDip(layout) -
                tableRows * NestingReportPagePlan.FirstTableRowHeightDip(orientation, layout) -
                NestingReportPagePlan.FirstPageBottomPaddingDip(layout));
            double aspectRatio = model.Sheet.Height > 0 ? model.Sheet.Width / model.Sheet.Height : 1;
            double maxChartWidthDip = width / DipToPoint * 0.68;
            double chartHeightDip = Math.Min(availableChartHeight, maxChartWidthDip / Math.Max(aspectRatio, 0.01));
            double chartWidth = chartHeightDip * aspectRatio * DipToPoint;
            double chartLeft = left + (width - chartWidth) / 2;
            double chartHeight = P(chartHeightDip);
            DrawSheet(gfx, report, model.Sheet, new XRect(chartLeft, chartTop, chartWidth, chartHeight), partLabel);
            double tableTitleTop = chartTop + chartHeight + P(1);
            gfx.DrawString($"DANH MỤC CHI TIẾT TRONG TẤM NEST {model.NestNumber}",
                new XFont("Arial", compact ? 6.5 : 8, XFontStyleEx.Bold), Color(23, 43, 77),
                new XRect(left, tableTitleTop, width, Math.Max(1, tableGap - P(1))), XStringFormats.CenterLeft);
            double tableTop = chartTop + chartHeight + tableGap;
            DrawPartTable(gfx, model, pageRef.PartPageIndex, orientation, layout, left, tableTop, width);
        }
        else
        {
            double tableTitleTop = cardTop + P(NestingReportPagePlan.ContinuationTitleTopDip(layout));
            gfx.DrawString($"DANH MỤC CHI TIẾT TRONG TẤM NEST {model.NestNumber} · TIẾP THEO",
                new XFont("Arial", compact ? 6.5 : 8, XFontStyleEx.Bold), Color(23, 43, 77),
                new XRect(left, tableTitleTop, width, P(compact ? 16 : 20)), XStringFormats.CenterLeft);
            DrawPartTable(gfx, model, pageRef.PartPageIndex, orientation, layout, left,
                cardTop + P(NestingReportPagePlan.ContinuationTableTopDip(layout)), width);
        }
    }

    private static void DrawMetrics(XGraphics gfx, NestingSheetSummary model, double left, double top, double width, bool compact)
    {
        var metrics = new (string Label, string Value)[]
        {
            ("Diện tích tấm", $"{NestingReportDocument.SquareMeters(model.SheetArea)} m²"),
            ("Hiệu suất Nest", model.NestEfficiency.ToString("P2", Culture)),
            ("Tỷ lệ sử dụng", model.SheetUtilization.ToString("P2", Culture)),
            ("Tỷ lệ hao phí", model.WasteRate.ToString("P2", Culture)),
            ("Tổng số chi tiết", model.Sheet.Placements.Count.ToString(Culture)),
            ("Loại chi tiết", model.Parts.Count.ToString(Culture))
        };
        int columns = metrics.Length;
        double cellWidth = width / columns, cellHeight = P(compact ? 16 : 22);
        var labelFont = new XFont("Arial", compact ? 4.8 : 5.8, XFontStyleEx.Regular);
        var valueFont = new XFont("Arial", compact ? 5.5 : 7, XFontStyleEx.Bold);
        for (int i = 0; i < metrics.Length; i++)
        {
            int row = i / columns, column = i % columns;
            var rect = new XRect(left + column * cellWidth, top + row * cellHeight, cellWidth, cellHeight);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(248, 250, 252)), rect);
            gfx.DrawRectangle(new XPen(XColor.FromArgb(218, 224, 232), 0.4), rect);
            gfx.DrawString(metrics[i].Label, labelFont, Color(90, 104, 120),
                new XRect(rect.X + 2, rect.Y + 1, rect.Width - 4, P(compact ? 6 : 8)), XStringFormats.Center);
            gfx.DrawString(metrics[i].Value, FitFont(gfx, metrics[i].Value, valueFont, rect.Width - 6), Color(23, 43, 77),
                new XRect(rect.X + 2, rect.Y + P(compact ? 7 : 9), rect.Width - 4, P(compact ? 9 : 12)), XStringFormats.Center);
        }
    }

    private static void DrawPartTable(XGraphics gfx, NestingSheetSummary model, int partPageIndex,
        NestingPageOrientation orientation, NestingPageLayout layout, double left, double top, double width)
    {
        bool compact = layout == NestingPageLayout.TwoPerPage;
        string[] headers = { "ID", "Tên chi tiết", "Kích thước (mm)", "Độ dày (mm)", "Diện tích (m²)", "Số lượng trên tấm" };
        double[] ratios = { .06, .31, .18, .12, .15, .18 };
        double[] widths = ratios.Select(ratio => ratio * width).ToArray();
        double headerHeight = P(partPageIndex == 0
            ? NestingReportPagePlan.FirstTableHeaderHeightDip(layout)
            : NestingReportPagePlan.ContinuationTableHeaderHeightDip(layout));
        double rowHeight = P(partPageIndex == 0
            ? NestingReportPagePlan.FirstTableRowHeightDip(orientation, layout)
            : NestingReportPagePlan.ContinuationTableRowHeightDip(orientation, layout));
        var headerFont = new XFont("Arial", compact ? 5.3 : 6.5, XFontStyleEx.Bold);
        var normalFont = new XFont("Arial", compact ? 5.4 : 6.7, XFontStyleEx.Regular);
        var boldFont = new XFont("Arial", compact ? 5.4 : 6.7, XFontStyleEx.Bold);
        double x = left;
        for (int column = 0; column < headers.Length; column++)
        {
            var rect = new XRect(x, top, widths[column], headerHeight);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(220, 228, 236)), rect);
            gfx.DrawRectangle(new XPen(XColor.FromArgb(135, 148, 161), 0.4), rect);
            gfx.DrawString(headers[column], FitFont(gfx, headers[column], headerFont, rect.Width - 4), Color(35, 54, 77),
                rect, XStringFormats.Center);
            x += widths[column];
        }

        int rowsPerPage = partPageIndex == 0
            ? NestingReportPagePlan.FirstPageRows(orientation, layout)
            : NestingReportPagePlan.ContinuationPageRows(orientation, layout);
        int firstRow = partPageIndex == 0 ? 0 :
            NestingReportPagePlan.FirstPageRows(orientation, layout) +
            (partPageIndex - 1) * NestingReportPagePlan.ContinuationPageRows(orientation, layout);
        var rows = model.Parts.Skip(firstRow).Take(rowsPerPage).ToList();
        for (int row = 0; row < rows.Count; row++)
        {
            var part = rows[row];
            double y = top + headerHeight + row * rowHeight;
            string[] values =
            {
                part.Id.ToString(CultureInfo.InvariantCulture),
                part.Name,
                $"{part.Width:0.##} × {part.Height:0.##}",
                $"{part.Thickness:0.##}",
                NestingReportDocument.SquareMeters(part.AreaPerPart),
                part.QuantityOnSheet.ToString(Culture)
            };
            x = left;
            for (int column = 0; column < values.Length; column++)
            {
                var rect = new XRect(x, y, widths[column], rowHeight);
                bool idColumn = column == 0;
                gfx.DrawRectangle(new XSolidBrush(idColumn
                    ? XColor.FromArgb(180, 35, 24)
                    : row % 2 == 0 ? XColor.FromArgb(255, 255, 255) : XColor.FromArgb(245, 247, 250)), rect);
                gfx.DrawRectangle(new XPen(XColor.FromArgb(175, 185, 196), 0.35), rect);
                var font = idColumn ? boldFont : normalFont;
                XBrush textBrush = idColumn ? XBrushes.White : Color(42, 56, 72);
                gfx.DrawString(values[column], FitFont(gfx, values[column], font, rect.Width - 5), textBrush,
                    new XRect(rect.X + (column == 1 ? 4 : 2), rect.Y + 1, rect.Width - 4, rect.Height - 2),
                    column == 1 ? XStringFormats.CenterLeft : XStringFormats.Center);
                x += widths[column];
            }
        }
    }

    private static void DrawSheet(XGraphics gfx, NestingReportDocument report, SheetResult sheet,
        XRect viewport, PartLabelMode partLabel)
    {
        gfx.DrawRectangle(new XPen(XColor.FromArgb(214, 222, 230), 0.7), XBrushes.White, viewport);
        if (!(sheet.Width > 0 && sheet.Height > 0)) return;
        double pad = 7;
        double scale = Math.Min((viewport.Width - 2 * pad) / sheet.Width, (viewport.Height - 2 * pad) / sheet.Height);
        double drawnWidth = sheet.Width * scale, drawnHeight = sheet.Height * scale;
        double left = viewport.X + (viewport.Width - drawnWidth) / 2;
        double top = viewport.Y + (viewport.Height - drawnHeight) / 2;
        gfx.DrawRectangle(new XPen(XColor.FromArgb(86, 102, 119), 0.9), new XSolidBrush(XColor.FromArgb(251, 252, 253)), left, top, drawnWidth, drawnHeight);
        XPoint Map(double x, double y) => new(left + x * scale, top + drawnHeight - y * scale);

        foreach (var placement in sheet.Placements)
        {
            XColor color = PartColor(placement.PartName);
            var pen = new XPen(color, Math.Clamp(scale * 0.9, 0.45, 1.4));
            if (placement.Shape == null)
            {
                double x = left + placement.X * scale;
                double y = top + drawnHeight - (placement.Y + placement.Height) * scale;
                gfx.DrawRectangle(pen, x, y, placement.Width * scale, placement.Height * scale);
            }

            else
            {
                foreach (var segment in placement.Shape.Place(placement.X, placement.Y, placement.Rotated))
                    DrawSegment(gfx, pen, segment, Map);
            }

            string? label = partLabel switch
            {
                PartLabelMode.None => null,
                PartLabelMode.IdNumber => report.PartIds.TryGetValue(placement.PartName, out int id)
                    ? id.ToString(CultureInfo.InvariantCulture)
                    : null,
                PartLabelMode.PartName => placement.PartName,
                _ => null
            };
            if (label is null) continue;

            double maxWidth = placement.Width * scale - 4;
            double maxHeight = placement.Height * scale - 3;
            if (maxWidth < 9 || maxHeight < 8) continue;
            var font = FitFont(gfx, label,
                new XFont("Arial", partLabel == PartLabelMode.IdNumber ? 5.7 : 6, XFontStyleEx.Bold),
                maxWidth - 4);
            while (gfx.MeasureString(label, font).Width > maxWidth - 4 && label.Length > 1)
            {
                label = label[..^1].TrimEnd() + "…";
                font = FitFont(gfx, label, font, maxWidth - 4);
            }
            if (gfx.MeasureString(label, font).Width > maxWidth - 4) continue;

            var center = Map(placement.X + placement.Width / 2, placement.Y + placement.Height / 2);
            double tagWidth = Math.Min(maxWidth, Math.Max(partLabel == PartLabelMode.IdNumber ? 13 : 0,
                gfx.MeasureString(label, font).Width + 4));
            double tagHeight = Math.Min(maxHeight, Math.Max(partLabel == PartLabelMode.IdNumber ? 9.6 : font.Size + 2, 8));
            var tag = new XRect(center.X - tagWidth / 2, center.Y - tagHeight / 2, tagWidth, tagHeight);
            gfx.DrawRectangle(new XPen(XColor.FromArgb(255, 255, 255), 0.5),
                new XSolidBrush(XColor.FromArgb(180, 35, 24)), tag);
            gfx.DrawString(label, font, XBrushes.White, tag, XStringFormats.Center);
        }
    }

    private static void DrawSegment(XGraphics gfx, XPen pen, Seg segment, Func<double, double, XPoint> map)
    {
        switch (segment)
        {
            case LineSeg line:
                gfx.DrawLine(pen, map(line.X1, line.Y1), map(line.X2, line.Y2));
                break;
            case CircleSeg circle:
                DrawArcPolyline(gfx, pen, map, circle.Cx, circle.Cy, circle.R, 0, Math.PI * 2);
                break;
            case ArcSeg arc:
                DrawArcPolyline(gfx, pen, map, arc.Cx, arc.Cy, arc.R, arc.StartRad, arc.SweepRad);
                break;
        }
    }

    private static void DrawArcPolyline(XGraphics gfx, XPen pen, Func<double, double, XPoint> map,
        double cx, double cy, double radius, double start, double sweep)
    {
        int segments = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 24)), 4, 160);
        XPoint previous = map(cx + radius * Math.Cos(start), cy + radius * Math.Sin(start));
        for (int i = 1; i <= segments; i++)
        {
            double angle = start + sweep * i / segments;
            var current = map(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
            gfx.DrawLine(pen, previous, current);
            previous = current;
        }
    }

    private static XFont FitFont(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        double size = font.Size;
        var style = font.Style;
        while (size > 5 && gfx.MeasureString(text, font).Width > maxWidth)
        {
            size -= 0.4;
            font = new XFont("Arial", size, style);
        }
        return font;
    }

    private static XSolidBrush Color(byte r, byte g, byte b) => new(XColor.FromArgb(r, g, b));

    private static double P(double dip) => dip * DipToPoint;

    private static XColor PartColor(string name)
    {
        string hex = CycloneNest2D.UI.PreviewScene.Palette[CycloneNest2D.UI.PreviewScene.ColorIndexForName(name)];
        return XColor.FromArgb(Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
    }
}
