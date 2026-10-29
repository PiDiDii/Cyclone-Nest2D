using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nesting2D.Desktop;

internal sealed record PdfExportSelection(
    ReportExportScope Scope, ReportPageSelection PageSelection, int FromPage, int ToPage);

internal sealed class PdfExportOptionsWindow : Window
{
    private readonly NestingReportDocument _report;
    private readonly NestingReportType _currentType;
    private readonly NestingPageOrientation _orientation;
    private readonly NestingPageLayout _layout;
    private readonly ReportPreviewPreferences _preferences;
    private readonly ComboBox _scopeBox;
    private readonly ComboBox _selectionBox;
    private readonly TextBox _fromBox;
    private readonly TextBox _toBox;
    private readonly StackPanel _rangePanel;
    private readonly TextBlock _pageCountText;
    private int _pageCount;

    public PdfExportSelection? Selection { get; private set; }

    public PdfExportOptionsWindow(NestingReportDocument report, NestingReportType currentType,
        NestingPageOrientation orientation, NestingPageLayout layout, ReportPreviewPreferences preferences)
    {
        _report = report;
        _currentType = currentType;
        _orientation = orientation;
        _layout = layout;
        _preferences = preferences;

        Title = "Tùy chọn xuất PDF";
        Width = 570;
        Height = 430;
        MinWidth = 540;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = Brush("#F3F6FA");

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new TextBlock { Text = "XUẤT BÁO CÁO PDF", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brush("#172B4D") });
        heading.Children.Add(new TextBlock { Text = "Chọn loại báo cáo và các trang cần đưa vào tệp PDF.", FontSize = 11, Foreground = Brush("#66758A"), Margin = new Thickness(0, 4, 0, 0) });
        root.Children.Add(heading);

        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        _scopeBox = MakeCombo(new[]
        {
            ($"Chỉ báo cáo hiện tại — {ReportName(currentType)}", ReportExportScope.CurrentReport),
            ("Cả hai báo cáo — thông số tấm + danh mục chi tiết", ReportExportScope.BothReports)
        }, 480);
        _selectionBox = MakeCombo(new[]
        {
            ("Tất cả trang", ReportPageSelection.AllPages),
            ("Chỉ trang lẻ (1, 3, 5, …)", ReportPageSelection.OddPages),
            ("Từ trang này đến trang kia", ReportPageSelection.PageRange)
        }, 260);

        body.Children.Add(Field("Nội dung xuất", _scopeBox));
        _pageCountText = new TextBlock { FontSize = 11, Foreground = Brush("#40536B"), Margin = new Thickness(1, 0, 0, 13) };
        body.Children.Add(_pageCountText);
        body.Children.Add(Field("Phạm vi trang", _selectionBox));

        _rangePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10), VerticalAlignment = VerticalAlignment.Center };
        _rangePanel.Children.Add(new TextBlock { Text = "Từ trang", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#526174"), Margin = new Thickness(1, 0, 7, 0) });
        _fromBox = NumberBox(preferences.FromPage);
        _toBox = NumberBox(preferences.ToPage);
        _rangePanel.Children.Add(_fromBox);
        _rangePanel.Children.Add(new TextBlock { Text = "đến", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#526174"), Margin = new Thickness(10, 0, 7, 0) });
        _rangePanel.Children.Add(_toBox);
        _rangePanel.Children.Add(new TextBlock { Text = "(bao gồm cả hai đầu mút)", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#66758A"), FontSize = 10, Margin = new Thickness(9, 0, 0, 0) });
        body.Children.Add(_rangePanel);
        body.Children.Add(new TextBlock
        {
            Text = "Khi xuất cả hai báo cáo, số trang được tính liên tục: báo cáo thông số trước, danh mục chi tiết sau.",
            FontSize = 10, Foreground = Brush("#66758A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 3, 0, 0)
        });
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = MakeButton("Hủy", false);
        cancel.IsCancel = true;
        var export = MakeButton("Tiếp tục chọn nơi lưu...", true);
        export.IsDefault = true;
        export.Click += Confirm_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(export);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;

        _scopeBox.SelectionChanged += (_, _) => UpdatePageCount();
        _selectionBox.SelectionChanged += (_, _) => UpdateRangeVisibility();
        _scopeBox.SelectedValue = preferences.ExportScope;
        if (_scopeBox.SelectedValue is not ReportExportScope) _scopeBox.SelectedIndex = 0;
        _selectionBox.SelectedValue = preferences.PageSelection;
        if (_selectionBox.SelectedValue is not ReportPageSelection) _selectionBox.SelectedIndex = 0;
        UpdatePageCount();
        UpdateRangeVisibility();
    }

    private void UpdatePageCount()
    {
        if (_scopeBox.SelectedValue is not ReportExportScope scope) return;
        var plan = NestingReportPagePlan.Build(_report, _currentType, _orientation, _layout, scope);
        _pageCount = plan.Count;
        _pageCountText.Text = $"Tổng số trang cho lựa chọn này: {_pageCount}";
        if (int.TryParse(_fromBox.Text, out int from) && from > _pageCount) _fromBox.Text = _pageCount.ToString(CultureInfo.InvariantCulture);
        if (int.TryParse(_toBox.Text, out int to) && to > _pageCount) _toBox.Text = _pageCount.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateRangeVisibility()
    {
        _rangePanel.IsEnabled = _selectionBox.SelectedValue is ReportPageSelection.PageRange;
        _rangePanel.Opacity = _rangePanel.IsEnabled ? 1 : 0.48;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_scopeBox.SelectedValue is not ReportExportScope scope || _selectionBox.SelectedValue is not ReportPageSelection pageSelection)
            return;
        int from = 1, to = _pageCount;
        if (pageSelection == ReportPageSelection.PageRange)
        {
            if (!int.TryParse(_fromBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out from) ||
                !int.TryParse(_toBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out to) ||
                from < 1 || to < from || to > _pageCount)
            {
                MessageBox.Show(this, $"Nhập khoảng trang hợp lệ từ 1 đến {_pageCount}; trang bắt đầu phải nhỏ hơn hoặc bằng trang kết thúc.",
                    "Khoảng trang không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        Selection = new PdfExportSelection(scope, pageSelection, from, to);
        _preferences.ExportScope = scope;
        _preferences.PageSelection = pageSelection;
        _preferences.FromPage = from;
        _preferences.ToPage = to;
        _preferences.Save();
        DialogResult = true;
    }

    private static ComboBox MakeCombo<T>(IEnumerable<(string Label, T Value)> values, double width) where T : struct, Enum
    {
        var combo = new ComboBox { Width = width, Height = 32, Margin = new Thickness(0, 4, 0, 13), SelectedValuePath = "Tag", VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var (label, value) in values)
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value, Padding = new Thickness(7, 4, 7, 4) });
        return combo;
    }

    private static StackPanel Field(string label, Control control)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        stack.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush("#344862") });
        stack.Children.Add(control);
        return stack;
    }

    private static TextBox NumberBox(int value) => new()
    {
        Text = Math.Max(1, value).ToString(CultureInfo.InvariantCulture), Width = 58, Height = 28,
        VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
        Padding = new Thickness(4), Margin = new Thickness(0, 1, 0, 1)
    };

    private static Button MakeButton(string text, bool primary)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(6, 0, 0, 0), MinWidth = 70 };
        if (primary) { button.Background = Brush("#1769AA"); button.Foreground = Brushes.White; button.BorderBrush = Brush("#1769AA"); }
        return button;
    }

    private static string ReportName(NestingReportType type) => type == NestingReportType.SheetParameters ? "Thông số từng tấm" : "Danh mục chi tiết";

    private static Brush Brush(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
}
