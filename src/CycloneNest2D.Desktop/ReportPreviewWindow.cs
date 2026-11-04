using System.Globalization;
using System.IO;
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
    private readonly NestingReportDocument _allReports;
    private NestingReportDocument _report;
    private readonly ScrollViewer _pageScrollViewer;
    private readonly TextBox _zoomBox;
    private readonly TextBlock _pageText;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly ComboBox _orientationBox;
    private readonly ComboBox _materialGroupBox;
    private readonly ComboBox _layoutBox;
    private readonly ComboBox _partLabelBox;
    private readonly ReportPreviewPreferences _preferences;
    private Canvas? _pageCanvas;
    private NestingPageOrientation _orientation;
    private NestingPageLayout _layout;
    private PartLabelMode _partLabel;
    private IReadOnlyList<NestingReportPage> _pages = [];
    private double _zoomPercent;
    private int _pageIndex;
    private int _pageCount;
    private bool _updatingZoomText;

    public string? ExportedPath { get; private set; }

    public ReportPreviewWindow(NestingReportDocument report)
    {
        _allReports = report;
        _materialGroupBox = ComboBox(report.MaterialGroups.Select(group => (group.Label, group)), 205);
        _materialGroupBox.SelectedIndex = 0;
        _report = report.MaterialGroups.Count > 0
            ? report.ForGroup(report.MaterialGroups[0])
            : report;
        _preferences = ReportPreviewPreferences.Load();
        _zoomPercent = _preferences.ZoomPercent;
        _layout = _preferences.Layout;
        _partLabel = _preferences.PartLabel;
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
        heading.Children.Add(new TextBlock { Text = "Chọn nhóm vật liệu/độ dày, hướng giấy, bố cục và nhãn chi tiết.", FontSize = 11, Foreground = Brush("#66758A"), Margin = new Thickness(0, 4, 0, 0) });
        headerGrid.Children.Add(heading);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(LabeledControl("Vật liệu · độ dày", _materialGroupBox));
        _orientationBox = ComboBox(new[]
        {
            ("A4 ngang", NestingPageOrientation.Landscape),
            ("A4 dọc", NestingPageOrientation.Portrait)
        }, 125);
        _orientationBox.SelectedValue = _preferences.Orientation;
        if (_orientationBox.SelectedValue is not NestingPageOrientation) _orientationBox.SelectedIndex = 0;
        _orientation = (NestingPageOrientation)_orientationBox.SelectedValue;

        _layoutBox = ComboBox(new[]
        {
            ("1 báo cáo / trang", NestingPageLayout.OnePerPage),
            ("2 báo cáo / trang", NestingPageLayout.TwoPerPage)
        }, 155);
        _layoutBox.SelectedValue = _layout;
        if (_layoutBox.SelectedValue is not NestingPageLayout) _layoutBox.SelectedIndex = 0;
        _layout = (NestingPageLayout)_layoutBox.SelectedValue;
        _partLabelBox = ComboBox(new[]
        {
            ("None", PartLabelMode.None),
            ("ID Number", PartLabelMode.IdNumber),
            ("Part Name", PartLabelMode.PartName)
        }, 125);
        _partLabelBox.SelectedValue = _partLabel;
        if (_partLabelBox.SelectedValue is not PartLabelMode) _partLabelBox.SelectedIndex = 1;
        _partLabel = (PartLabelMode)_partLabelBox.SelectedValue;

        controls.Children.Add(LabeledControl("Hướng giấy", _orientationBox));
        controls.Children.Add(LabeledControl("Bố cục", _layoutBox));
        controls.Children.Add(LabeledControl("Part Label", _partLabelBox));
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

        _orientationBox.SelectionChanged += ReportSettings_Changed;
        _layoutBox.SelectionChanged += ReportSettings_Changed;
        _partLabelBox.SelectionChanged += ReportSettings_Changed;
        _materialGroupBox.SelectionChanged += ReportGroup_Changed;
        RefreshPages();
    }

    private void ReportGroup_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_materialGroupBox.SelectedValue is not NestingReportMaterialGroup group) return;
        _report = _allReports.ForGroup(group);
        RefreshPages();
    }

    private void ReportSettings_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_orientationBox.SelectedValue is not NestingPageOrientation orientation ||
            _layoutBox.SelectedValue is not NestingPageLayout layout ||
            _partLabelBox.SelectedValue is not PartLabelMode partLabel) return;
        _orientation = orientation;
        _layout = layout;
        _partLabel = partLabel;
        _preferences.Orientation = orientation;
        _preferences.Layout = layout;
        _preferences.PartLabel = partLabel;
        _preferences.Save();
        RefreshPages();
    }

    private void RefreshPages()
    {
        _pages = NestingReportPagePlan.Build(_report, _orientation, _layout);
        _pageCount = _pages.Count;
        _pageIndex = Math.Clamp(_pageIndex, 0, _pageCount - 1);
        ShowPage(_pageIndex);
    }

    private int FirstPageRows => NestingReportPagePlan.FirstPageRows(_orientation, _layout);
    private int ContinuationPageRows => NestingReportPagePlan.ContinuationPageRows(_orientation, _layout);
    private double PageWidth => NestingReportPagePlan.PageWidthDip(_orientation);
    private double PageHeight => NestingReportPagePlan.PageHeightDip(_orientation);
    private double ContentMargin => NestingReportPagePlan.ContentMarginDip(_orientation);
    private double ContentVerticalMargin => NestingReportPagePlan.ContentVerticalMarginDip(_orientation);

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
        DrawSheetReportPage(canvas, _pages[pageIndex], pageIndex);
        return canvas;
    }

    private void DrawSheetReportPage(Canvas canvas, NestingReportPage page, int pageIndex)
    {
        double cardHeight = NestingReportPagePlan.CardHeightDip(_orientation, page.SlotCount);
        bool compact = _layout == NestingPageLayout.TwoPerPage;
        foreach (var reportPage in page.Reports)
        {
            double cardTop = ContentVerticalMargin + reportPage.Slot * cardHeight;
            DrawSheetReportCard(canvas, reportPage, pageIndex, cardTop, cardHeight, compact);
        }
    }

    private void DrawSheetReportCard(Canvas canvas, NestingReportPageRef page, int pageIndex,
        double cardTop, double cardHeight, bool compact)
    {
        var model = _report.SheetSummaries[page.SheetIndex];
        bool firstPage = page.PartPageIndex == 0;
        double left = ContentMargin, width = PageWidth - 2 * ContentMargin;
        double titleTop = cardTop + (compact ? 4 : 8);
        AddText(canvas, firstPage ? "BÁO CÁO TẤM NESTING 2D" : "DANH MỤC CHI TIẾT · TIẾP THEO",
            left, titleTop, width, compact ? 18 : 24, compact ? 13 : 17, true, "#172B4D");
        double metadataTop = titleTop + (compact ? 19 : 26);
        double metadataLineHeight = compact ? 12 : 14;
        double leftColumnWidth = width * NestingReportPagePlan.MetadataLeftColumnRatio;
        AddText(canvas, $"Dự án: {_report.ProjectName}", left, metadataTop,
            leftColumnWidth, metadataLineHeight, compact ? 8 : 10, true, "#344862");
        AddText(canvas, $"Tên tấm: {model.PlateName}", left, metadataTop + metadataLineHeight,
            leftColumnWidth, metadataLineHeight, compact ? 8 : 10, true, "#344862");
        AddText(canvas, $"Khổ tấm: {model.Sheet.Width:0.##} × {model.Sheet.Height:0.##} mm", left, metadataTop + metadataLineHeight * 2,
            leftColumnWidth, metadataLineHeight, compact ? 8 : 10, true, "#344862");
        double rightColumnLeft = left + width * NestingReportPagePlan.MetadataRightColumnStartRatio;
        double rightColumnWidth = width * (1 - NestingReportPagePlan.MetadataRightColumnStartRatio);
        AddText(canvas, $"Vật liệu: {model.Sheet.Material}", rightColumnLeft, metadataTop,
            rightColumnWidth, metadataLineHeight, compact ? 8 : 10, true, "#344862");
        AddText(canvas, $"Độ dày: {model.Sheet.Thickness:0.##} mm", rightColumnLeft, metadataTop + metadataLineHeight,
            rightColumnWidth, metadataLineHeight, compact ? 8 : 10, true, "#344862");
        AddText(canvas, $"Số lượng tấm: {model.PlateQuantity}", rightColumnLeft, metadataTop + metadataLineHeight * 2,
            rightColumnWidth, metadataLineHeight, compact ? 8 : 10, true, "#344862");

        if (firstPage)
        {
            var metrics = new (string Label, string Value)[]
            {
                ("Diện tích tấm", $"{NestingReportDocument.SquareMeters(model.SheetArea)} m²"),
                ("Hiệu suất Nest", model.NestEfficiency.ToString("P2", CultureInfo.CurrentCulture)),
                ("Tỷ lệ sử dụng", model.SheetUtilization.ToString("P2", CultureInfo.CurrentCulture)),
                ("Tỷ lệ hao phí", model.WasteRate.ToString("P2", CultureInfo.CurrentCulture)),
                ("Số chi tiết", $"{model.Sheet.Placements.Count}"),
                ("Loại chi tiết", $"{model.Parts.Count}")
            };
            int columns = metrics.Length;
            double cellWidth = width / columns;
            double metricTop = cardTop + NestingReportPagePlan.MetricsTopDip(_layout);
            double metricCellHeight = NestingReportPagePlan.MetricsCellHeightDip(_layout);
            for (int i = 0; i < metrics.Length; i++)
            {
                int row = i / columns, col = i % columns;
                var border = new Border { Width = cellWidth, Height = metricCellHeight, Background = Brush("#F8FAFC"), BorderBrush = Brush("#D8E0E8"), BorderThickness = new Thickness(0.55) };
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };
                stack.Children.Add(new TextBlock { Text = metrics[i].Label, FontSize = compact ? 7.2 : 9, FontWeight = FontWeights.Bold, Foreground = Brush("#5A6878"), TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                stack.Children.Add(new TextBlock { Text = metrics[i].Value, FontSize = compact ? 8.2 : 10.5, FontWeight = FontWeights.Bold, Foreground = Brush("#172B4D"), TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                border.Child = stack;
                Canvas.SetLeft(border, left + col * cellWidth); Canvas.SetTop(border, metricTop + row * metricCellHeight); canvas.Children.Add(border);
            }
        }

        double tableTop;
        if (firstPage)
        {
            double previewTop = cardTop + NestingReportPagePlan.FirstChartTopDip(_layout);
            double tableGap = NestingReportPagePlan.FirstTableGapDip(_layout);
            double headerHeight = NestingReportPagePlan.FirstTableHeaderHeightDip(_layout);
            double rowHeight = NestingReportPagePlan.FirstTableRowHeightDip(_orientation, _layout);
            int tableRows = Math.Min(FirstPageRows, model.Parts.Count);
            double bottomPadding = NestingReportPagePlan.FirstPageBottomPaddingDip(_layout);
            double availableChartHeight = Math.Max(0, cardHeight - previewTop + cardTop - tableGap -
                headerHeight - tableRows * rowHeight - bottomPadding);
            double maxPreviewWidth = width * 0.68;
            double aspectRatio = model.Sheet.Height > 0 ? model.Sheet.Width / model.Sheet.Height : 1;
            double previewHeight = Math.Min(availableChartHeight, maxPreviewWidth / Math.Max(aspectRatio, 0.01));
            double previewWidth = Math.Min(maxPreviewWidth, previewHeight * aspectRatio);
            double previewLeft = left + (width - previewWidth) / 2;
            var frame = new Border { Width = previewWidth, Height = previewHeight, Background = Brushes.White, BorderBrush = Brush("#D6DEE6"), BorderThickness = new Thickness(1), Padding = new Thickness(4) };
            frame.Child = new SheetThumbnailView
            {
                Sheet = model.Sheet,
                PartIds = _report.PartIds,
                LabelMode = _partLabel,
                Width = previewWidth - 10,
                Height = previewHeight - 10,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(frame, previewLeft); Canvas.SetTop(frame, previewTop); canvas.Children.Add(frame);
            AddText(canvas, $"DANH MỤC CHI TIẾT TRONG {model.PlateName}", left,
                previewTop + previewHeight + 1, width, tableGap - 1, compact ? 8 : 10, true, "#172B4D");
            tableTop = previewTop + previewHeight + tableGap;
        }
        else
        {
            double tableTitleTop = cardTop + NestingReportPagePlan.ContinuationTitleTopDip(_layout);
            AddText(canvas, $"DANH MỤC CHI TIẾT TRONG {model.PlateName} · TIẾP THEO",
                left, tableTitleTop, width, compact ? 16 : 20, compact ? 9 : 11, true, "#172B4D");
            tableTop = cardTop + NestingReportPagePlan.ContinuationTableTopDip(_layout);
        }

        DrawPartTable(canvas, model, page.PartPageIndex, tableTop, compact);
    }

    private void DrawPartTable(Canvas canvas, NestingSheetSummary model, int partPageIndex, double tableTop, bool compact)
    {
        double left = ContentMargin, width = PageWidth - 2 * ContentMargin;
        string[] headers = { "ID", "Tên chi tiết", "Kích thước (mm)", "Độ dày (mm)", "Diện tích (m²)", "Số lượng trên tấm" };
        double[] ratios = { .06, .31, .18, .12, .15, .18 };
        double[] widths = ratios.Select(ratio => ratio * width).ToArray();
        double headerHeight = partPageIndex == 0
            ? NestingReportPagePlan.FirstTableHeaderHeightDip(_layout)
            : NestingReportPagePlan.ContinuationTableHeaderHeightDip(_layout);
        double rowHeight = partPageIndex == 0
            ? NestingReportPagePlan.FirstTableRowHeightDip(_orientation, _layout)
            : NestingReportPagePlan.ContinuationTableRowHeightDip(_orientation, _layout);
        double fontSize = compact ? 7 : 8.5;
        double x = left;
        for (int i = 0; i < headers.Length; i++)
        {
            AddCell(canvas, headers[i], x, tableTop, widths[i], headerHeight, "#DCE4EC", true, true, compact ? 7.5 : 9);
            x += widths[i];
        }
        int rowsPerPage = partPageIndex == 0 ? FirstPageRows : ContinuationPageRows;
        int firstRow = partPageIndex == 0 ? 0 : FirstPageRows + (partPageIndex - 1) * ContinuationPageRows;
        var rows = model.Parts.Skip(firstRow).Take(rowsPerPage).ToList();
        for (int r = 0; r < rows.Count; r++)
        {
            var part = rows[r];
            double y = tableTop + headerHeight + r * rowHeight;
            x = left;
            string[] values =
            {
                part.Id.ToString(CultureInfo.InvariantCulture), part.Name,
                $"{part.Width:0.##} × {part.Height:0.##}",
                $"{part.Thickness:0.##}",
                NestingReportDocument.SquareMeters(part.AreaPerPart),
                part.QuantityOnSheet.ToString(CultureInfo.CurrentCulture)
            };
            for (int c = 0; c < widths.Length; c++)
            {
                string background = c == 0 ? "#B42318" : r % 2 == 0 ? "#FFFFFF" : "#F5F7FA";
                AddCell(canvas, values[c], x, y, widths[c], rowHeight, background, c == 0, c != 1, fontSize);
                x += widths[c];
            }
        }
    }

    private void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        var options = new PdfExportOptionsWindow(_report, _orientation, _layout, _preferences) { Owner = this };
        if (options.ShowDialog() != true || options.Selection is not { } selection) return;
        var dialog = new SaveFileDialog
        {
            Title = "Xuất báo cáo PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            FileName = BuildDefaultPdfFileName(),
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            PdfReportWriter.Write(_report, dialog.FileName, _orientation, _layout, _partLabel,
                selection.PageSelection, selection.FromPage, selection.ToPage);
            ExportedPath = dialog.FileName;
            MessageBox.Show(this, $"Đã tạo PDF hợp lệ:\n{dialog.FileName}", "Xuất PDF thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không tạo được PDF: " + ex.Message, "Lỗi xuất PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string BuildDefaultPdfFileName()
    {
        var firstSheet = _report.SheetSummaries[0].Sheet;
        string baseName = $"bao_cao_{_report.ProjectName}_{firstSheet.Material}_{firstSheet.Thickness:0.###}mm";
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(baseName.Select(character =>
            Array.IndexOf(invalid, character) >= 0 ? '_' : character)) + ".pdf";
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
            dialog.PrintDocument(document.DocumentPaginator, "Cyclone Nest2D - Báo cáo nesting theo tấm");
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

    private static ComboBox ComboBox<T>(IEnumerable<(string Label, T Value)> items, double width)
    {
        var combo = new ComboBox { Width = width, Height = 29, FontSize = 11, Padding = new Thickness(5, 3, 5, 3), SelectedValuePath = nameof(ComboBoxItem.Tag) };
        foreach (var (label, value) in items) combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        return combo;
    }

    private static void AddCell(Canvas canvas, string text, double x, double y, double width, double height, string background, bool bold, bool center, double fontSize)
    {
        var border = new Border { Width = width, Height = height, Background = Brush(background), BorderBrush = Brush("#ADB8C4"), BorderThickness = new Thickness(0.45), Padding = new Thickness(center ? 1 : 5, 1, 2, 1) };
        border.Child = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = background == "#B42318" ? Brushes.White : Brush("#2A3848"),
            TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Canvas.SetLeft(border, x); Canvas.SetTop(border, y); canvas.Children.Add(border);
    }

    private static void AddText(Canvas canvas, string text, double x, double y, double width, double height, double size, bool bold, string color,
        TextAlignment alignment = TextAlignment.Left)
    {
        var block = new TextBlock
        {
            Text = text,
            Width = width,
            Height = height,
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Brush(color),
            TextAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Canvas.SetLeft(block, x); Canvas.SetTop(block, y); canvas.Children.Add(block);
    }

    private static Button Button(string text, bool primary) => new()
    {
        Content = text,
        Padding = new Thickness(13, 7, 13, 7),
        Margin = new Thickness(4, 0, 4, 0),
        FontSize = 11,
        Background = primary ? Brush("#11A7A1") : Brushes.White,
        Foreground = primary ? Brushes.White : Brush("#344257"),
        BorderBrush = primary ? Brush("#11A7A1") : Brush("#DCE4EB"),
        BorderThickness = new Thickness(1),
        Cursor = Cursors.Hand
    };

    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
}
