using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using CycloneNest2D.Core;

namespace CycloneNest2D.Desktop;

internal sealed class DxfExportDialog : Window
{
    private readonly IReadOnlyList<SheetResult> _sheets;
    private readonly SheetThumbnailView _preview;
    private readonly TextBlock _pageCount;
    private readonly CheckBox _partsSingleLayer;
    private readonly TextBox _partLayer;
    private readonly ComboBox _orientation;
    private readonly CheckBox _labels;
    private readonly CheckBox _statistics;
    private readonly CheckBox _outputSheets;
    private readonly TextBox _sheetLayer;
    private readonly RadioButton _singleFile;
    private readonly ComboBox _geometryStyle;
    private readonly CheckBox _partsAsBlocks;
    private readonly CheckBox _nestsAsBlocks;
    private readonly CheckBox _removeCommonEdges;
    private readonly ComboBox _outerDirection;
    private readonly ComboBox _innerDirection;
    private readonly CheckBox _showPreview;
    private readonly CheckBox _microJoints;
    private readonly TextBox _microJointWidth;
    private readonly TextBox _microJointCount;
    private int _sheetIndex;

    public DxfExportOptions? Options { get; private set; }

    public DxfExportDialog(IReadOnlyList<SheetResult> sheets)
    {
        _sheets = sheets;
        Title = "Xuất Nest — DXF";
        Width = 1400; Height = 900; MinWidth = 1180; MinHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#F3F6F9");
        ResizeMode = ResizeMode.CanResize;

        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var optionsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var optionsPanel = new StackPanel { Margin = new Thickness(0, 0, 3, 0) };
        _partsSingleLayer = new CheckBox { Content = "Gộp Part vào một Layer", Margin = new Thickness(4, 4, 4, 6) };
        _partLayer = TextBox("NESTED_PARTS");
        _partLayer.IsEnabled = false;
        _partsSingleLayer.Checked += (_, _) => _partLayer.IsEnabled = true;
        _partsSingleLayer.Unchecked += (_, _) => _partLayer.IsEnabled = false;
        _orientation = Combo(new[] { ("0°", 0), ("90°", 90), ("180°", 180), ("270°", 270) });
        _labels = new CheckBox { Content = "Gắn nhãn Part", Margin = new Thickness(4, 5, 4, 3) };
        _statistics = new CheckBox { Content = "Ghi thống kê Nest", Margin = new Thickness(4, 3, 4, 5) };
        optionsPanel.Children.Add(Group("Chi tiết sau khi xếp", _partsSingleLayer, FieldRow("Tên Layer Part", _partLayer), FieldRow("Hướng xoay bản vẽ", _orientation), _labels, _statistics));

        _outputSheets = new CheckBox { Content = "Có, xuất khổ tấm", IsChecked = true, Margin = new Thickness(4, 4, 4, 6) };
        _sheetLayer = TextBox("SHEET");
        _outputSheets.Checked += (_, _) => _sheetLayer.IsEnabled = true;
        _outputSheets.Unchecked += (_, _) => _sheetLayer.IsEnabled = false;
        optionsPanel.Children.Add(Group("Xuất thêm khổ tấm?", _outputSheets, FieldRow("Tên Layer tấm", _sheetLayer)));

        _singleFile = new RadioButton { Content = "Gộp vào một file", GroupName = "FileLayout", IsChecked = true, Margin = new Thickness(4, 3, 4, 5) };
        var multipleFiles = new RadioButton { Content = "Mỗi tấm một file", GroupName = "FileLayout", Margin = new Thickness(4, 3, 4, 3) };
        optionsPanel.Children.Add(Group("Bố trí nhiều khổ tấm", _singleFile, multipleFiles));

        _geometryStyle = Combo(new[]
        {
            ("Đường thẳng / cung (cơ bản)", DxfGeometryStyle.BasicLinesArcs),
            ("Đường Polyline xấp xỉ", DxfGeometryStyle.PolylineApproximation)
        });
        _partsAsBlocks = new CheckBox { Content = "Xuất Part dạng Block", Margin = new Thickness(4, 3, 4, 3) };
        _nestsAsBlocks = new CheckBox { Content = "Xuất Nest dạng Block", Margin = new Thickness(4, 3, 4, 3) };
        _removeCommonEdges = new CheckBox { Content = "Xóa cạnh chung", Margin = new Thickness(4, 3, 4, 3) };
        _partsAsBlocks.Checked += (_, _) => _removeCommonEdges.IsEnabled = false;
        _partsAsBlocks.Unchecked += (_, _) => _removeCommonEdges.IsEnabled = true;
        optionsPanel.Children.Add(Group("Tùy chọn tương thích", FieldRow("Kiểu hình học", _geometryStyle), _partsAsBlocks, _nestsAsBlocks, _removeCommonEdges));

        _microJoints = new CheckBox { Content = "Bật micro-joints (cầu giữ part trên tấm)", Margin = new Thickness(4, 4, 4, 6) };
        _microJointWidth = TextBox("1.0");
        _microJointCount = TextBox("4");
        _microJointWidth.IsEnabled = _microJointCount.IsEnabled = false;
        _microJoints.Checked += (_, _) => _microJointWidth.IsEnabled = _microJointCount.IsEnabled = true;
        _microJoints.Unchecked += (_, _) => _microJointWidth.IsEnabled = _microJointCount.IsEnabled = false;
        optionsPanel.Children.Add(Group("Micro-joints · ngắt đường cắt biên ngoài", _microJoints,
            FieldRow("Độ rộng cầu (mm)", _microJointWidth), FieldRow("Số cầu mỗi part", _microJointCount),
            new TextBlock { Text = "Các đoạn bị ngắt tạo cầu vật liệu chưa cắt; contour lỗ bên trong không bị thay đổi.", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#56677A"), Margin = new Thickness(4, 3, 4, 4), FontSize = 10 }));

        _outerDirection = Combo(new[] { ("Ngược chiều kim đồng hồ", DxfLoopDirection.CounterClockwise), ("Cùng chiều kim đồng hồ", DxfLoopDirection.Clockwise) });
        _innerDirection = Combo(new[] { ("Ngược chiều kim đồng hồ", DxfLoopDirection.CounterClockwise), ("Cùng chiều kim đồng hồ", DxfLoopDirection.Clockwise) });
        optionsPanel.Children.Add(Group("Hướng contour", FieldRow("Biên ngoài", _outerDirection), FieldRow("Biên trong", _innerDirection)));

        optionsPanel.Children.Add(Group("Định dạng cố định",
            new TextBlock { Text = "Đơn vị: mm  ·  Phiên bản: AutoCAD R12", Foreground = Brush("#344862"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 4, 4, 5) }));
        optionsScroll.Content = optionsPanel;
        root.Children.Add(optionsScroll);

        var previewGrid = new Grid();
        Grid.SetColumn(previewGrid, 2);
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var previewHeading = new TextBlock { Text = "Xem trước", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("#23364F"), Margin = new Thickness(0, 2, 0, 8) };
        previewGrid.Children.Add(previewHeading);
        var previewFrame = new Border { Background = Brushes.White, BorderBrush = Brush("#CFD8E2"), BorderThickness = new Thickness(1), Padding = new Thickness(10), CornerRadius = new CornerRadius(5) };
        Grid.SetRow(previewFrame, 1);
        var previewBox = new Viewbox { Stretch = Stretch.Uniform, Child = _preview = new SheetThumbnailView { Width = 820, Height = 560, IsHitTestVisible = false } };
        previewFrame.Child = previewBox;
        previewGrid.Children.Add(previewFrame);
        var previewFooter = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        previewFooter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        previewFooter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        previewFooter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _showPreview = new CheckBox { Content = "Hiện xem trước", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        _showPreview.Checked += (_, _) => previewFrame.Visibility = Visibility.Visible;
        _showPreview.Unchecked += (_, _) => previewFrame.Visibility = Visibility.Collapsed;
        Grid.SetRow(previewFooter, 2);
        previewFooter.Children.Add(_showPreview);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var prev = Button("‹", false); prev.Width = 34; prev.ToolTip = "Tấm trước"; prev.Click += (_, _) => SetSheet(_sheetIndex - 1);
        _pageCount = new TextBlock { Text = "1 / 1", Width = 60, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#33445B") };
        var next = Button("›", false); next.Width = 34; next.ToolTip = "Tấm tiếp theo"; next.Click += (_, _) => SetSheet(_sheetIndex + 1);
        navigation.Children.Add(prev); navigation.Children.Add(_pageCount); navigation.Children.Add(next);
        Grid.SetColumn(navigation, 2); previewFooter.Children.Add(navigation);
        previewGrid.Children.Add(previewFooter);
        root.Children.Add(previewGrid);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var cancel = Button("Hủy", false); cancel.IsCancel = true;
        var export = Button("Xuất", true); export.IsDefault = true; export.Click += Export_Click;
        footer.Children.Add(export); footer.Children.Add(cancel);
        Grid.SetColumnSpan(footer, 3);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        Content = root;
        if (_sheets.Count > 0) SetSheet(0);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        double microWidth = 0;
        int microCount = 0;
        if (_microJoints.IsChecked == true)
        {
            bool widthOk = double.TryParse(_microJointWidth.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out microWidth) ||
                           double.TryParse(_microJointWidth.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out microWidth);
            if (!widthOk || !double.IsFinite(microWidth) || microWidth <= 0 ||
                !int.TryParse(_microJointCount.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out microCount) || microCount is < 1 or > 1000)
            {
                MessageBox.Show(this, "Nhập độ rộng cầu lớn hơn 0 mm và số cầu từ 1 đến 1000.", "Thông số micro-joint không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        if (_partsSingleLayer.IsChecked == true && string.IsNullOrWhiteSpace(_partLayer.Text))
        {
            MessageBox.Show(this, "Hãy nhập tên Layer chứa chi tiết.", "Thiếu tên Layer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_outputSheets.IsChecked == true && string.IsNullOrWhiteSpace(_sheetLayer.Text))
        {
            MessageBox.Show(this, "Hãy nhập tên Layer chứa khổ tấm.", "Thiếu tên Layer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Options = new DxfExportOptions
        {
            PartsOnSingleLayer = _partsSingleLayer.IsChecked == true,
            PartLayerName = _partLayer.Text.Trim(),
            NestOrientation = SelectedValue<int>(_orientation),
            AddPartLabels = _labels.IsChecked == true,
            AddNestStatistics = _statistics.IsChecked == true,
            OutputSheets = _outputSheets.IsChecked == true,
            SheetLayerName = _sheetLayer.Text.Trim(),
            FileLayout = _singleFile.IsChecked == true ? DxfFileLayout.SingleFile : DxfFileLayout.MultipleFiles,
            GeometryStyle = SelectedValue<DxfGeometryStyle>(_geometryStyle),
            ExportPartsAsBlocks = _partsAsBlocks.IsChecked == true,
            ExportNestsAsBlocks = _nestsAsBlocks.IsChecked == true,
            RemoveCommonEdges = _removeCommonEdges.IsChecked == true,
            OuterDirection = SelectedValue<DxfLoopDirection>(_outerDirection),
            InnerDirection = SelectedValue<DxfLoopDirection>(_innerDirection),
            MicroJointWidthMm = microWidth,
            MicroJointCount = microCount,
            Units = DxfOutputUnits.Millimeters,
            TargetVersion = DxfTargetVersion.R12
        };
        DialogResult = true;
    }

    private void SetSheet(int index)
    {
        if (_sheets.Count == 0) return;
        _sheetIndex = Math.Clamp(index, 0, _sheets.Count - 1);
        _preview.Sheet = _sheets[_sheetIndex];
        _pageCount.Text = $"{_sheetIndex + 1} / {_sheets.Count}";
    }

    private static GroupBox Group(string title, params UIElement[] controls)
    {
        var panel = new StackPanel { Margin = new Thickness(5, 1, 5, 4) };
        foreach (var control in controls) panel.Children.Add(control);
        return new GroupBox { Header = title, Content = panel, Margin = new Thickness(0, 0, 0, 7), Padding = new Thickness(6, 4, 6, 5), Foreground = Brush("#344257"), FontSize = 11 };
    }

    private static Grid FieldRow(string label, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(3, 3, 3, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#56677A"), FontSize = 10 };
        Grid.SetColumn(control, 1); grid.Children.Add(text); grid.Children.Add(control);
        return grid;
    }

    private static TextBox TextBox(string value) => new() { Text = value, Padding = new Thickness(5, 3, 5, 3), BorderBrush = Brush("#D5DEE7"), Background = Brushes.White, FontSize = 10 };

    private static ComboBox Combo<T>(IEnumerable<(string Text, T Value)> values)
    {
        var combo = new ComboBox { MinWidth = 120, Padding = new Thickness(4, 2, 4, 2), FontSize = 10,
            SelectedValuePath = nameof(ComboBoxItem.Tag) };
        foreach (var value in values)
            combo.Items.Add(new ComboBoxItem { Content = value.Text, Tag = value.Value });
        combo.SelectedIndex = 0;
        return combo;
    }

    private static T SelectedValue<T>(ComboBox combo) => combo.SelectedValue is T value ? value : default!;

    private static Button Button(string text, bool primary) => new()
    {
        Content = text, Padding = new Thickness(13, 6, 13, 6), Margin = new Thickness(4, 0, 4, 0), FontSize = 11,
        Background = primary ? Brush("#11A7A1") : Brushes.White,
        Foreground = primary ? Brushes.White : Brush("#344257"),
        BorderBrush = primary ? Brush("#11A7A1") : Brush("#DCE4EB"), BorderThickness = new Thickness(1),
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));
}
