using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Printing;
using Microsoft.Win32;
using CycloneNest2D.UI;

namespace CycloneNest2D.Desktop;

internal sealed class ReportPreviewWindow : Window
{
    private readonly NestingReportDocument _report;
    private readonly ScrollViewer _pageScrollViewer;
    private readonly TextBox _zoomBox;
    private readonly TextBlock _pageText;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly ComboBox _reportTypeBox;
    private readonly ComboBox _orientationBox;
    private readonly ComboBox _layoutBox;
    private readonly ReportPreviewPreferences _preferences;
    private Canvas? _pageCanvas;
    private NestingReportType _reportType;
    private NestingPageOrientation _orientation;
    private NestingPageLayout _layout;
    private double _zoomPercent;
    private int _pageIndex;
    private int _pageCount;
    private bool _updatingZoomText;

    public string? ExportedPath { get; private set; }

    public ReportPreviewWindow(NestingReportDocument report)
    {
        _report = report;
        _preferences = ReportPreviewPreferences.Load();
        _zoomPercent = _preferences.ZoomPercent;
        Title = "Xem trước báo cáo PDF";
        Width = 1480;
        Height = 960;
        MinWidth = 1080;
        MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#F3F6FA");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border { Background = Brushes.White, BorderBrush = Brush("#E0E6ED"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(18, 11, 18, 11) };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock { Text = "XEM TRƯỚC BÁO CÁO PDF", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#172B4D") });
        heading.Children.Add(new TextBlock { Text = "Chọn báo cáo, hướng giấy và bố cục; duyệt từng trang trước khi xuất hoặc in.", FontSize = 11, Foreground = Brush("#66758A"), Margin = new Thickness(0, 4, 0, 0) });
        headerGrid.Children.Add(heading);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _reportTypeBox = ComboBox(new[]
        {
            ("Thông số từng tấm", NestingReportType.SheetParameters),
            ("Danh mục chi tiết đã nest", NestingReportType.PartCatalog)
        }, 230);
        _orientationBox = ComboBox(new[]
        {
            ("A4 ngang", NestingPageOrientation.Landscape),
            ("A4 dọc", NestingPageOrientation.Portrait)
        }, 125);
        _layoutBox = ComboBox(new[]
        {
            ("1 báo cáo thông số / trang", NestingPageLayout.OnePerPage),
            ("2 báo cáo thông số / trang", NestingPageLayout.TwoSheetReportsPerPage)
        }, 220);
        _reportTypeBox.SelectedValue = _preferences.ReportType;
        _orientationBox.SelectedValue = _preferences.Orientation;
        _layoutBox.SelectedValue = _preferences.Layout;
        if (_reportTypeBox.SelectedValue is not NestingReportType) _reportTypeBox.SelectedIndex = 0;
        if (_orientationBox.SelectedValue is not NestingPageOrientation) _orientationBox.SelectedIndex = 0;
        if (_layoutBox.SelectedValue is not NestingPageLayout) _layoutBox.SelectedIndex = 0;
        _reportType = (NestingReportType)_reportTypeBox.SelectedValue;
        _orientation = (NestingPageOrientation)_orientationBox.SelectedValue;
        _layout = (NestingPageLayout)_layoutBox.SelectedValue;
        _layoutBox.IsEnabled = _reportType == NestingReportType.SheetParameters;

        controls.Children.Add(LabeledControl("Báo cáo", _reportTypeBox));
        controls.Children.Add(LabeledControl("Hướng giấy", _orientationBox));
        controls.Children.Add(LabeledControl("Bố cục", _layoutBox));
        var zoomTools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var zoomOut = Button("−", false); zoomOut.Width = 34; zoomOut.Click += (_, _) => SetZoom(_zoomPercent - 10);
        var zoomIn = Button("+", false); zoomIn.Width = 34; zoomIn.Click += (_, _) => SetZoom(_zoomPercent + 10);
        _zoomBox = new TextBox { Width = 70, Height = 29, Text = FormatZoom(_zoomPercent), VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(2, 0, 2, 0), ToolTip = "Nhập mức thu phóng từ 10% đến 500%" };
        _zoomBox.KeyDown += ZoomBox_KeyDown;
        _zoomBox.LostKeyboardFocus += (_, _) => ApplyZoomText();
        var fit = Button("Fit", false); fit.Click += ZoomFit_Click;
        zoomTools.Children.Add(zoomOut); zoomTools.Children.Add(_zoomBox); zoomTools.Children.Add(zoomIn); zoomTools.Children.Add(fit);
        controls.Children.Add(LabeledControl("Thu phóng", zoomTools));
        Grid.SetColumn(controls, 1);
        headerGrid.Children.Add(controls);
        header.Child = headerGrid;
        root.Children.Add(header);

        var stage = new Border { Margin = new Thickness(16, 12, 16, 10), Background = Brush("#E9EEF4"), BorderBrush = Brush("#D9E2EB"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12) };
        Grid.SetRow(stage, 1);
        _pageScrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false,
            Background = Brush("#E9EEF4"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Focusable = true
        };
        _pageScrollViewer.PreviewMouseWheel += PreviewScrollViewer_MouseWheel;
        stage.Child = _pageScrollViewer;
        root.Children.Add(stage);

        var footer = new Border { Background = Brushes.White, BorderBrush = Brush("#E0E6ED"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 9, 16, 9) };
        Grid.SetRow(footer, 2);
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _previous = Button("‹ Trang trước", false);
        _previous.Click += (_, _) => ShowPage(_pageIndex - 1);
        _pageText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 110, TextAlignment = TextAlignment.Center, Foreground = Brush("#40536B"), FontWeight = FontWeights.SemiBold };
        _next = Button("Trang sau ›", false);
        _next.Click += (_, _) => ShowPage(_pageIndex + 1);
        navigation.Children.Add(_previous); navigation.Children.Add(_pageText); navigation.Children.Add(_next);
        footerGrid.Children.Add(navigation);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Button("Đóng", false); cancel.IsCancel = true;
        var print = Button("In / thiết lập máy in...", false); print.Click += PrintReport_Click;
        var save = Button("Xuất file PDF...", true); save.Click += SavePdf_Click;
        actions.Children.Add(print); actions.Children.Add(cancel); actions.Children.Add(save);
        Grid.SetColumn(actions, 2); footerGrid.Children.Add(actions);
        footer.Child = footerGrid;
        root.Children.Add(footer);
        Content = root;

        _reportTypeBox.SelectionChanged += ReportSettings_Changed;
        _orientationBox.SelectionChanged += ReportSettings_Changed;
        _layoutBox.SelectionChanged += ReportSettings_Changed;
        RefreshPages();
    }

    private void ReportSettings_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_reportTypeBox.SelectedValue is not NestingReportType type ||
            _orientationBox.SelectedValue is not NestingPageOrientation orientation ||
            _layoutBox.SelectedValue is not NestingPageLayout layout) return;
        _reportType = type;
        _orientation = orientation;
        _layout = layout;
        _layoutBox.IsEnabled = _reportType == NestingReportType.SheetParameters;
        _preferences.ReportType = type;
        _preferences.Orientation = orientation;
        _preferences.Layout = layout;
        _preferences.Save();
        RefreshPages();
    }

    private void RefreshPages()
    {
        _pageCount = NestingReportPagePlan.PageCount(_report, _reportType, _orientation, _layout);
        _pageIndex = Math.Clamp(_pageIndex, 0, _pageCount - 1);
        ShowPage(_pageIndex);
    }

    private int SheetsPerPage => _layout == NestingPageLayout.TwoSheetReportsPerPage ? 2 : 1;
    private int CatalogRowsPerPage => NestingReportPagePlan.CatalogRowsPerPage(_orientation);
    private double PageWidth => _orientation == NestingPageOrientation.Landscape ? 1122 : 794;
    private double PageHeight => _orientation == NestingPageOrientation.Landscape ? 794 : 1122;

    private void ShowPage(int index)
    {
        _pageIndex = Math.Clamp(index, 0, Math.Max(0, _pageCount - 1));
        _pageCanvas = BuildPage(_pageIndex);
        _pageCanvas.LayoutTransform = new ScaleTransform(_zoomPercent / 100d, _zoomPercent / 100d);
        _pageScrollViewer.Content = _pageCanvas;
        _pageText.Text = $"Trang {_pageIndex + 1} / {_pageCount}";
        _previous.IsEnabled = _pageIndex > 0;
        _next.IsEnabled = _pageIndex < _pageCount - 1;
    }

    private Canvas BuildPage(int pageIndex)
    {
        var canvas = new Canvas { Width = PageWidth, Height = PageHeight, Background = Brushes.White, ClipToBounds = true };
        if (_reportType == NestingReportType.SheetParameters)
        {
            int perPage = SheetsPerPage;
            double cardHeight = PageHeight / perPage;
            for (int slot = 0; slot < perPage; slot++)
            {
                int sheetIndex = pageIndex * perPage + slot;
                if (sheetIndex >= _report.SheetSummaries.Count) break;
                DrawSheetSummary(canvas, _report.SheetSummaries[sheetIndex], slot * cardHeight, cardHeight);
                if (slot > 0)
                {
                    var line = new Border { Width = PageWidth - 64, Height = 1, Background = Brush("#CBD5E0") };
                    Canvas.SetLeft(line, 32); Canvas.SetTop(line, slot * cardHeight); canvas.Children.Add(line);
                }
            }
        }
        else DrawCatalogPage(canvas, pageIndex);
        return canvas;
    }

    private void DrawSheetSummary(Canvas canvas, NestingSheetSummary model, double top, double height)
    {
        double left = 32, width = PageWidth - 64;
        AddText(canvas, "THÔNG SỐ TẤM NESTING 2D", left, top + 15, width, 28, 18, true, "#172B4D");
        AddText(canvas, $"Nest {model.NestNumber} / {model.NestCount}  ·  {model.Sheet.Material}  ·  {model.Sheet.Thickness:0.##} mm  ·  {model.Sheet.Width:0.##} × {model.Sheet.Height:0.##} mm",
            left, top + 47, width, 22, 12, true, "#344862");
        string completed = _report.CompletedAt?.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture) ?? "Không có dữ liệu";
        AddText(canvas, $"Ngày giờ nesting hoàn tất: {completed}", left, top + 70, width, 16, 9, false, "#66758A");

        var metrics = new (string Label, string Value)[]
        {
            ("STT Nest", $"{model.NestNumber} / {model.NestCount}"),
            ("Vật liệu", model.Sheet.Material),
            ("Độ dày", $"{model.Sheet.Thickness:0.##} mm"),
            ("Khổ tấm", $"{model.Sheet.Width:0.##} × {model.Sheet.Height:0.##} mm"),
            ("Diện tích tấm", $"{NestingReportDocument.SquareMeters(model.SheetArea)} m²"),
            ("Chiều dài sử dụng", $"{model.UsedLength:0.##} mm"),
            ("Diện tích Part", $"{NestingReportDocument.SquareMeters(model.UsedPartArea)} m²"),
            ("Hiệu suất Nest", model.NestEfficiency.ToString("P2", CultureInfo.CurrentCulture)),
            ("Tỷ lệ sử dụng", model.SheetUtilization.ToString("P2", CultureInfo.CurrentCulture)),
            ("Tỷ lệ hao phí", model.WasteRate.ToString("P2", CultureInfo.CurrentCulture)),
            ("Số Part trong nest", $"{model.Sheet.Placements.Count} hình")
        };
        double cellWidth = width / 4, cellHeight = 27, metricTop = top + 91;
        for (int i = 0; i < metrics.Length; i++)
        {
            int row = i / 4, col = i % 4;
            var border = new Border { Width = cellWidth, Height = cellHeight, Background = Brush("#F8FAFC"), BorderBrush = Brush("#D8E0E8"), BorderThickness = new Thickness(0.55) };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(3, 1, 3, 1) };
            stack.Children.Add(new TextBlock { Text = metrics[i].Label, FontSize = 8, Foreground = Brush("#5A6878"), TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            stack.Children.Add(new TextBlock { Text = metrics[i].Value, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brush("#172B4D"), TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            border.Child = stack;
            Canvas.SetLeft(border, left + col * cellWidth); Canvas.SetTop(border, metricTop + row * cellHeight); canvas.Children.Add(border);
        }

        double previewTop = top + 181, previewHeight = Math.Max(80, height - 195);
        var frame = new Border { Width = width, Height = previewHeight, Background = Brushes.White, BorderBrush = Brush("#D6DEE6"), BorderThickness = new Thickness(1), Padding = new Thickness(4) };
        var sheetView = new SheetThumbnailView
        {
            Sheet = model.Sheet, PartIds = _report.PartIds, ShowPartIds = true,
            Width = width - 10, Height = previewHeight - 10, IsHitTestVisible = false
        };
        frame.Child = sheetView;
        Canvas.SetLeft(frame, left); Canvas.SetTop(frame, previewTop); canvas.Children.Add(frame);
        AddText(canvas, "Cyclone Nest2D · Báo cáo thông số tấm", left, top + height - 18, width, 12, 8, false, "#8090A1");
    }

    private void DrawCatalogPage(Canvas canvas, int pageIndex)
    {
        double left = 32, width = PageWidth - 64;
        AddText(canvas, "DANH MỤC CHI TIẾT ĐÃ NEST", left, 24, width, 30, 20, true, "#172B4D");
        string completed = _report.CompletedAt?.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.CurrentCulture) ?? "Không có dữ liệu";
        AddText(canvas, $"Toàn bộ tác vụ  ·  {_report.Parts.Count} loại Part  ·  {_report.TotalProduced}/{_report.TotalRequired} đã nest  ·  {_report.SheetSummaries.Count} tấm  ·  Hoàn tất: {completed}",
            left, 58, width, 20, 10, false, "#52657A");
        AddText(canvas, $"Trang {pageIndex + 1} / {_pageCount}", left, 81, width, 16, 9, false, "#66758A");

        string[] headers = { "ID", "Xem trước", "Tên Part", "Rộng (mm)", "Dài (mm)", "Diện tích (m²)", "Đã nest" };
        double[] ratios = { .06, .11, .39, .10, .10, .13, .11 };
        double[] widths = ratios.Select(r => r * width).ToArray();
        const double tableTop = 112, headerHeight = 30, rowHeight = 29;
        double x = left;
        for (int i = 0; i < headers.Length; i++)
        {
            AddCell(canvas, headers[i], x, tableTop, widths[i], headerHeight, "#DCE4EC", true, true, 9);
            x += widths[i];
        }
        int first = pageIndex * CatalogRowsPerPage;
        var rows = _report.Parts.Skip(first).Take(CatalogRowsPerPage).ToList();
        for (int r = 0; r < rows.Count; r++)
        {
            var part = rows[r];
            double y = tableTop + headerHeight + r * rowHeight;
            x = left;
            string[] values =
            {
                part.Id.ToString(CultureInfo.InvariantCulture), "", part.Name,
                part.Width.ToString("0.##", CultureInfo.CurrentCulture),
                part.Height.ToString("0.##", CultureInfo.CurrentCulture),
                NestingReportDocument.SquareMeters(part.AreaPerPart),
                $"{part.ProducedInJob}/{part.RequiredInJob}"
            };
            for (int c = 0; c < widths.Length; c++)
            {
                string background = c == 0 ? "#B42318" : r % 2 == 0 ? "#FFFFFF" : "#F5F7FA";
                AddCell(canvas, values[c], x, y, widths[c], rowHeight, background, c == 0, c != 2, 8.5);
                if (c == 1 && part.SourcePart != null)
                {
                    var preview = new PartPreviewView { Part = part.SourcePart, Width = widths[c] - 8, Height = rowHeight - 4, IsHitTestVisible = false, Focusable = false };
                    Canvas.SetLeft(preview, x + 4); Canvas.SetTop(preview, y + 2); canvas.Children.Add(preview);
                }
                x += widths[c];
            }
        }
        AddText(canvas, "Cyclone Nest2D · Báo cáo danh mục Part", left, PageHeight - 35, width, 14, 8, false, "#8090A1");
    }

    private void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        var options = new PdfExportOptionsWindow(_report, _reportType, _orientation, _layout, _preferences) { Owner = this };
        if (options.ShowDialog() != true || options.Selection is not { } selection) return;
        var dialog = new SaveFileDialog
        {
            Title = "Xuất báo cáo PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            FileName = selection.Scope == ReportExportScope.BothReports ? "bao_cao_nesting_day_du.pdf" :
                _reportType == NestingReportType.SheetParameters ? "thong_so_tung_tam.pdf" : "danh_muc_chi_tiet_da_nest.pdf",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            PdfReportWriter.Write(_report, dialog.FileName, _reportType, _orientation, _layout,
                selection.Scope, selection.PageSelection, selection.FromPage, selection.ToPage);
            ExportedPath = dialog.FileName;
            MessageBox.Show(this, $"Đã tạo PDF hợp lệ:\n{dialog.FileName}", "Xuất PDF thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không tạo được PDF: " + ex.Message, "Lỗi xuất PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PrintReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new PrintDialog();
            dialog.PrintTicket ??= new PrintTicket();
            dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
            dialog.PrintTicket.PageOrientation = _orientation == NestingPageOrientation.Landscape
                ? System.Printing.PageOrientation.Landscape : System.Printing.PageOrientation.Portrait;
            if (dialog.ShowDialog() != true) return;

            if (dialog.PrintTicket?.PageOrientation == System.Printing.PageOrientation.Portrait)
                _orientationBox.SelectedValue = NestingPageOrientation.Portrait;
            else if (dialog.PrintTicket?.PageOrientation == System.Printing.PageOrientation.Landscape)
                _orientationBox.SelectedValue = NestingPageOrientation.Landscape;

            var document = new FixedDocument();
            double printableWidth = dialog.PrintableAreaWidth;
            double printableHeight = dialog.PrintableAreaHeight;
            if (!(printableWidth > 0 && printableHeight > 0))
            {
                printableWidth = PageWidth * 96 / 72;
                printableHeight = PageHeight * 96 / 72;
            }
            for (int i = 0; i < _pageCount; i++)
            {
                var fixedPage = new FixedPage { Width = printableWidth, Height = printableHeight, Background = Brushes.White };
                var view = new Viewbox { Width = printableWidth, Height = printableHeight, Stretch = Stretch.Uniform, Child = BuildPage(i) };
                fixedPage.Children.Add(view);
                fixedPage.Measure(new Size(printableWidth, printableHeight));
                fixedPage.Arrange(new Rect(0, 0, printableWidth, printableHeight));
                fixedPage.UpdateLayout();
                var content = new PageContent();
                ((IAddChild)content).AddChild(fixedPage);
                document.Pages.Add(content);
            }
            dialog.PrintDocument(document.DocumentPaginator, _reportType == NestingReportType.SheetParameters ? "Cyclone Nest2D - Thông số tấm" : "Cyclone Nest2D - Danh mục Part");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không thể in báo cáo: " + ex.Message, "Lỗi in", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoomPercent + 10);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoomPercent - 10);

    private void ZoomFit_Click(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            _pageScrollViewer.UpdateLayout();
            double availableWidth = Math.Max(50, _pageScrollViewer.ViewportWidth - 12);
            double availableHeight = Math.Max(50, _pageScrollViewer.ViewportHeight - 12);
            double fit = Math.Min(availableWidth / PageWidth, availableHeight / PageHeight) * 100;
            SetZoom(fit);
        }));
    }

    private void ZoomBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyZoomText();
        e.Handled = true;
    }

    private void ApplyZoomText()
    {
        if (_updatingZoomText) return;
        string input = _zoomBox.Text.Trim().TrimEnd('%').Trim();
        if (double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
            double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            SetZoom(value);
        else
            UpdateZoomText();
    }

    private void PreviewScrollViewer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetZoom(_zoomPercent + (e.Delta > 0 ? 10 : -10));
        e.Handled = true;
    }

    private void SetZoom(double percent)
    {
        _zoomPercent = Math.Round(Math.Clamp(double.IsFinite(percent) ? percent : 100, 10, 500), 1);
        if (_pageCanvas != null)
        {
            _pageCanvas.LayoutTransform = new ScaleTransform(_zoomPercent / 100d, _zoomPercent / 100d);
            _pageScrollViewer.UpdateLayout();
        }
        UpdateZoomText();
        _preferences.ZoomPercent = _zoomPercent;
        _preferences.Save();
    }

    private void UpdateZoomText()
    {
        _updatingZoomText = true;
        _zoomBox.Text = FormatZoom(_zoomPercent);
        _updatingZoomText = false;
    }

    private static string FormatZoom(double percent) => $"{percent:0.#}%";

    private static FrameworkElement LabeledControl(string label, FrameworkElement control)
    {
        var stack = new StackPanel { Margin = new Thickness(5, 0, 5, 0) };
        stack.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = Brush("#66758A"), Margin = new Thickness(1, 0, 0, 3) });
        control.VerticalAlignment = VerticalAlignment.Center;
        stack.Children.Add(control);
        return stack;
    }

    private static ComboBox ComboBox<T>(IEnumerable<(string Label, T Value)> items, double width) where T : struct, Enum
    {
        var combo = new ComboBox { Width = width, Height = 29, FontSize = 11, Padding = new Thickness(5, 3, 5, 3), SelectedValuePath = nameof(ComboBoxItem.Tag) };
        foreach (var (label, value) in items) combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        return combo;
    }

    private static void AddCell(Canvas canvas, string text, double x, double y, double width, double height, string background, bool bold, bool center, double fontSize)
    {
        var border = new Border { Width = width, Height = height, Background = Brush(background), BorderBrush = Brush("#ADB8C4"), BorderThickness = new Thickness(0.45), Padding = new Thickness(center ? 1 : 5, 1, 2, 1) };
        border.Child = new TextBlock { Text = text, FontSize = fontSize, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = background == "#B42318" ? Brushes.White : Brush("#2A3848"),
            TextAlignment = center ? TextAlignment.Center : TextAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis };
        Canvas.SetLeft(border, x); Canvas.SetTop(border, y); canvas.Children.Add(border);
    }

    private static void AddText(Canvas canvas, string text, double x, double y, double width, double height, double size, bool bold, string color)
    {
        var block = new TextBlock { Text = text, Width = width, Height = height, FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = Brush(color),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Canvas.SetLeft(block, x); Canvas.SetTop(block, y); canvas.Children.Add(block);
    }

    private static Button Button(string text, bool primary) => new()
    {
        Content = text, Padding = new Thickness(13, 7, 13, 7), Margin = new Thickness(4, 0, 4, 0), FontSize = 11,
        Background = primary ? Brush("#11A7A1") : Brushes.White,
        Foreground = primary ? Brushes.White : Brush("#344257"),
        BorderBrush = primary ? Brush("#11A7A1") : Brush("#DCE4EB"), BorderThickness = new Thickness(1),
        Cursor = Cursors.Hand
    };

    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
}
