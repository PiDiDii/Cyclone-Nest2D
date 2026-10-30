using System.Globalization;
using System.IO;
using CycloneNest2D.Core;
using CycloneNest2D.UI;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace CycloneNest2D.Desktop;

internal static class PdfReportWriter
{
    private const double Margin = 24;
    private static readonly CultureInfo Culture = CultureInfo.CurrentCulture;

    public static void Write(NestingReportDocument report, string path, NestingReportType currentType,
        NestingPageOrientation orientation, NestingPageLayout layout, ReportExportScope scope,
        ReportPageSelection pageSelection, int fromPage, int toPage)
    {
        bool landscape = orientation == NestingPageOrientation.Landscape;
        var allPages = NestingReportPagePlan.Build(report, currentType, orientation, layout, scope);
        var selectedPages = allPages.Where((_, index) => pageSelection switch
        {
            ReportPageSelection.AllPages => true,
            ReportPageSelection.OddPages => (index + 1) % 2 == 1,
            ReportPageSelection.PageRange => index + 1 >= fromPage && index + 1 <= toPage,
            _ => false
        }).ToList();
        if (selectedPages.Count == 0)
            throw new InvalidOperationException("Lựa chọn không bao gồm trang PDF nào.");

        var document = new PdfDocument();
        document.Info.Title = scope == ReportExportScope.BothReports ? "Cyclone Nest2D - Báo cáo" :
            currentType == NestingReportType.SheetParameters ? "Thông số tấm Nesting 2D" : "Danh mục chi tiết đã nest";
        document.Info.Subject = "Báo cáo Cyclone Nest2D";
        document.Info.Creator = "Cyclone Nest2D";

        foreach (var pageRef in selectedPages)
        {
            var page = document.AddPage();
            page.Size = PageSize.A4;
            page.Orientation = landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
            using var gfx = XGraphics.FromPdfPage(page);
            double pageWidth = page.Width.Point;
            double pageHeight = page.Height.Point;
            gfx.DrawRectangle(XBrushes.White, 0, 0, pageWidth, pageHeight);
            if (pageRef.Type == NestingReportType.SheetParameters)
            {
                int sheetsPerPage = layout == NestingPageLayout.TwoSheetReportsPerPage ? 2 : 1;
                DrawSheetSummaryPage(gfx, report, pageRef.SectionPageIndex, pageWidth, pageHeight, sheetsPerPage);
            }
            else
            {
                int rowsPerPage = NestingReportPagePlan.CatalogRowsPerPage(orientation);
                int catalogPages = NestingReportPagePlan.PageCount(report, NestingReportType.PartCatalog, orientation, layout);
                DrawCatalogPage(gfx, report, pageRef.SectionPageIndex, catalogPages, pageWidth, pageHeight, rowsPerPage);
            }
        }

        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        document.Save(path);
        using var check = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[5];
        if (check.Read(signature) != signature.Length || !signature.SequenceEqual("%PDF-"u8))
            throw new InvalidDataException("Tệp đã lưu không có chữ ký PDF hợp lệ.");
    }

    private static void DrawSheetSummaryPage(XGraphics gfx, NestingReportDocument report, int pageIndex,
        double pageWidth, double pageHeight, int sheetsPerPage)
    {
        double cardHeight = pageHeight / sheetsPerPage;
        for (int slot = 0; slot < sheetsPerPage; slot++)
        {
            int sheetIndex = pageIndex * sheetsPerPage + slot;
            if (sheetIndex >= report.SheetSummaries.Count) break;
            if (slot > 0)
                gfx.DrawLine(new XPen(XColor.FromArgb(203, 213, 224), 0.7), Margin, slot * cardHeight, pageWidth - Margin, slot * cardHeight);
            DrawSheetSummary(gfx, report, report.SheetSummaries[sheetIndex], Margin, slot * cardHeight + 5,
                pageWidth - 2 * Margin, cardHeight - 10);
        }
    }

    private static void DrawSheetSummary(XGraphics gfx, NestingReportDocument report,
        NestingSheetSummary model, double left, double top, double width, double height)
    {
        var title = new XFont("Arial", 14, XFontStyleEx.Bold);
        var subtitle = new XFont("Arial", 8, XFontStyleEx.Bold);
        var small = new XFont("Arial", 6.8, XFontStyleEx.Regular);
        gfx.DrawString("THÔNG SỐ TẤM NESTING 2D", title, Color(23, 43, 77),
            new XRect(left, top + 5, width, 21), XStringFormats.CenterLeft);
        string line = $"Nest {model.NestNumber} / {model.NestCount} · {model.Sheet.Material} · {model.Sheet.Thickness:0.##} mm · {model.Sheet.Width:0.##} × {model.Sheet.Height:0.##} mm";
        gfx.DrawString(line, FitFont(gfx, line, subtitle, width), Color(52, 68, 91),
            new XRect(left, top + 28, width, 13), XStringFormats.CenterLeft);
        string completed = report.CompletedAt?.ToString("dd/MM/yyyy HH:mm:ss", Culture) ?? "Không có dữ liệu";
        gfx.DrawString($"Ngày giờ nesting hoàn tất: {completed}", small, Color(102, 117, 138),
            new XRect(left, top + 41, width, 11), XStringFormats.CenterLeft);

        var metrics = new (string Label, string Value)[]
        {
            ("STT Nest", $"{model.NestNumber} / {model.NestCount}"),
            ("Vật liệu", model.Sheet.Material),
            ("Độ dày", $"{model.Sheet.Thickness:0.##} mm"),
            ("Khổ tấm", $"{model.Sheet.Width:0.##} × {model.Sheet.Height:0.##} mm"),
            ("Diện tích tấm", $"{NestingReportDocument.SquareMeters(model.SheetArea)} m²"),
            ("Chiều dài sử dụng", $"{model.UsedLength:0.##} mm"),
            ("Diện tích Part", $"{NestingReportDocument.SquareMeters(model.UsedPartArea)} m²"),
            ("Hiệu suất Nest", model.NestEfficiency.ToString("P2", Culture)),
            ("Tỷ lệ sử dụng", model.SheetUtilization.ToString("P2", Culture)),
            ("Tỷ lệ hao phí", model.WasteRate.ToString("P2", Culture)),
            ("Số Part trong nest", $"{model.Sheet.Placements.Count} hình")
        };
        double metricsTop = top + 56;
        double cellWidth = width / 4, cellHeight = 23;
        var labelFont = new XFont("Arial", 6.1, XFontStyleEx.Regular);
        var valueFont = new XFont("Arial", 7.2, XFontStyleEx.Bold);
        for (int i = 0; i < metrics.Length; i++)
        {
            int row = i / 4, col = i % 4;
            var rect = new XRect(left + col * cellWidth, metricsTop + row * cellHeight, cellWidth, cellHeight);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(248, 250, 252)), rect);
            gfx.DrawRectangle(new XPen(XColor.FromArgb(218, 224, 232), 0.4), rect);
            gfx.DrawString(metrics[i].Label, labelFont, Color(90, 104, 120),
                new XRect(rect.X + 2, rect.Y + 1, rect.Width - 4, 8), XStringFormats.Center);
            gfx.DrawString(metrics[i].Value, FitFont(gfx, metrics[i].Value, valueFont, rect.Width - 6), Color(23, 43, 77),
                new XRect(rect.X + 2, rect.Y + 9, rect.Width - 4, 12), XStringFormats.Center);
        }

        double chartTop = top + 132;
        double chartHeight = Math.Max(40, top + height - chartTop - 14);
        var chart = new XRect(left, chartTop, width, chartHeight);
        DrawSheet(gfx, report, model.Sheet, chart);
        gfx.DrawString("Cyclone Nest2D · Báo cáo thông số tấm", small, Color(128, 144, 161),
            new XRect(left, top + height - 11, width, 9), XStringFormats.CenterLeft);
    }

    private static void DrawCatalogPage(XGraphics gfx, NestingReportDocument report, int pageIndex, int pageCount,
        double pageWidth, double pageHeight, int rowsPerPage)
    {
        double left = Margin, width = pageWidth - Margin * 2;
        var title = new XFont("Arial", 16, XFontStyleEx.Bold);
        var subtitle = new XFont("Arial", 7.8, XFontStyleEx.Regular);
        gfx.DrawString("DANH MỤC CHI TIẾT ĐÃ NEST", title, Color(23, 43, 77),
            new XRect(left, 20, width, 24), XStringFormats.CenterLeft);
        string completed = report.CompletedAt?.ToString("dd/MM/yyyy HH:mm:ss", Culture) ?? "Không có dữ liệu";
        string details = $"Toàn bộ tác vụ · {report.Parts.Count} loại Part · {report.TotalProduced} đã nest / {report.TotalRequired} yêu cầu · {report.SheetSummaries.Count} tấm · Hoàn tất: {completed}";
        gfx.DrawString(details, FitFont(gfx, details, subtitle, width), Color(82, 101, 122),
            new XRect(left, 47, width, 15), XStringFormats.CenterLeft);
        gfx.DrawString($"Trang {pageIndex + 1} / {pageCount}", subtitle, Color(102, 117, 138),
            new XRect(left, 64, width, 12), XStringFormats.CenterLeft);

        string[] headers = { "ID", "Xem trước", "Tên Part", "Rộng (mm)", "Dài (mm)", "Diện tích (m²)", "Đã nest" };
        double[] ratios = { .06, .11, .39, .10, .10, .13, .11 };
        double[] widths = ratios.Select(r => r * width).ToArray();
        double headerTop = 83, headerHeight = 21, rowHeight = 21, x = left;
        var headerFont = new XFont("Arial", 6.2, XFontStyleEx.Bold);
        for (int i = 0; i < headers.Length; i++)
        {
            var rect = new XRect(x, headerTop, widths[i], headerHeight);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(220, 228, 236)), rect);
            gfx.DrawRectangle(new XPen(XColor.FromArgb(135, 148, 161), 0.4), rect);
            gfx.DrawString(headers[i], FitFont(gfx, headers[i], headerFont, widths[i] - 4), Color(35, 54, 77), rect, XStringFormats.Center);
            x += widths[i];
        }

        var normal = new XFont("Arial", 6.6, XFontStyleEx.Regular);
        var bold = new XFont("Arial", 6.6, XFontStyleEx.Bold);
        var rows = report.Parts.Skip(pageIndex * rowsPerPage).Take(rowsPerPage).ToList();
        for (int r = 0; r < rows.Count; r++)
        {
            var part = rows[r];
            double y = headerTop + headerHeight + r * rowHeight;
            x = left;
            string[] values =
            {
                part.Id.ToString(CultureInfo.InvariantCulture), "", part.Name,
                part.Width.ToString("0.##", Culture), part.Height.ToString("0.##", Culture),
                NestingReportDocument.SquareMeters(part.AreaPerPart),
                $"{part.ProducedInJob}/{part.RequiredInJob}"
            };
            for (int c = 0; c < widths.Length; c++)
            {
                var rect = new XRect(x, y, widths[c], rowHeight);
                bool idColumn = c == 0;
                if (idColumn) gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(180, 35, 24)), rect);
                else gfx.DrawRectangle(new XSolidBrush(r % 2 == 0 ? XColor.FromArgb(255, 255, 255) : XColor.FromArgb(245, 247, 250)), rect);
                gfx.DrawRectangle(new XPen(XColor.FromArgb(175, 185, 196), 0.35), rect);
                if (c == 1) DrawMiniPart(gfx, part, rect);
                else
                {
                    var font = idColumn ? bold : normal;
                    XBrush brush = idColumn ? XBrushes.White : Color(42, 56, 72);
                    gfx.DrawString(values[c], FitFont(gfx, values[c], font, widths[c] - 5), brush,
                        new XRect(rect.X + (c == 2 ? 4 : 2), rect.Y + 1, rect.Width - 4, rect.Height - 2),
                        c == 2 ? XStringFormats.CenterLeft : XStringFormats.Center);
                }
                x += widths[c];
            }
        }
        var footerFont = new XFont("Arial", 6.5, XFontStyleEx.Regular);
        gfx.DrawString("Cyclone Nest2D · Báo cáo danh mục Part", footerFont, Color(128, 144, 161),
            new XRect(left, pageHeight - 28, width, 12), XStringFormats.CenterLeft);
    }

    private static void DrawSheet(XGraphics gfx, NestingReportDocument report, SheetResult sheet, XRect viewport)
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

            if (report.PartIds.TryGetValue(placement.PartName, out int id))
            {
                var center = Map(placement.X + placement.Width / 2, placement.Y + placement.Height / 2);
                var tag = new XRect(center.X - 6.5, center.Y - 4.8, 13, 9.6);
                gfx.DrawRectangle(new XPen(XColor.FromArgb(255, 255, 255), 0.5), new XSolidBrush(XColor.FromArgb(180, 35, 24)), tag);
                gfx.DrawString(id.ToString(CultureInfo.InvariantCulture), new XFont("Arial", 5.7, XFontStyleEx.Bold), XBrushes.White, tag, XStringFormats.Center);
            }
        }
        gfx.DrawString($"{sheet.Width:0.##} × {sheet.Height:0.##} mm", new XFont("Arial", 5.6, XFontStyleEx.Regular),
            Color(92, 106, 122), new XRect(viewport.X + 2, viewport.Bottom - 11, viewport.Width - 4, 9), XStringFormats.CenterRight);
    }

    private static void DrawMiniPart(XGraphics gfx, NestingReportPart part, XRect cell)
    {
        double scale = Math.Min((cell.Width - 10) / Math.Max(1, part.Width), (cell.Height - 6) / Math.Max(1, part.Height));
        double drawnWidth = part.Width * scale, drawnHeight = part.Height * scale;
        double left = cell.X + (cell.Width - drawnWidth) / 2;
        double top = cell.Y + (cell.Height - drawnHeight) / 2;
        XColor color = PartColor(part.Name);
        var pen = new XPen(color, 0.8);
        var shape = part.SourcePart?.Geometry;
        if (shape != null)
        {
            if (part.SourcePart!.Flip) shape = ShapeOps.MirrorHorizontal(shape);
            XPoint Map(double x, double y) => new(left + (x - shape.MinX) * scale, top + drawnHeight - (y - shape.MinY) * scale);
            foreach (var segment in shape.Segments) DrawSegment(gfx, pen, segment, Map);
        }
        else
        {
            gfx.DrawRectangle(pen, new XSolidBrush(XColor.FromArgb(110, color.R, color.G, color.B)),
                left, top, Math.Max(1, drawnWidth), Math.Max(1, drawnHeight));
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

    private static XColor PartColor(string name)
    {
        string hex = PreviewScene.Palette[PreviewScene.ColorIndexForName(name)];
        return XColor.FromArgb(Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
    }
}
