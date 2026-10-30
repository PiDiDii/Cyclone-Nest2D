using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CycloneNest2D.UI;

namespace CycloneNest2D.Desktop;

public sealed class StandardShapeLibraryWindow : Window
{
    private readonly ComboBox _shapeBox;
    private readonly TextBox _widthBox;
    private readonly TextBox _heightBox;
    private readonly TextBox _sidesBox;
    private readonly TextBlock _widthLabel;
    private readonly StackPanel _heightRow;
    private readonly StackPanel _sidesRow;
    private readonly TextBlock _error;

    public StandardPartShape Shape { get; private set; }
    public double ShapeWidth { get; private set; }
    public double ShapeHeight { get; private set; }
    public int Sides { get; private set; }

    public StandardShapeLibraryWindow(Window owner)
    {
        Owner = owner;
        Title = "Thư viện mẫu";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 430;
        FontFamily = owner.FontFamily;
        FontSize = owner.FontSize;
        SetResourceReference(BackgroundProperty, "BgBrush");

        var panel = new StackPanel { Margin = new Thickness(22), MinWidth = 390 };
        panel.Children.Add(new TextBlock
        {
            Text = "Chọn hình tiêu chuẩn và nhập kích thước trước khi thêm vào danh sách chi tiết.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
            Foreground = Brush(owner, "TextSecondaryBrush")
        });

        _shapeBox = new ComboBox
        {
            ItemsSource = new[]
            {
                new ShapeOption(StandardPartShape.Rectangle, "Chữ nhật"),
                new ShapeOption(StandardPartShape.Circle, "Hình tròn"),
                new ShapeOption(StandardPartShape.Triangle, "Tam giác"),
                new ShapeOption(StandardPartShape.Polygon, "Đa giác đều")
            },
            DisplayMemberPath = nameof(ShapeOption.Label),
            SelectedIndex = 0,
            Padding = new Thickness(7, 5, 7, 5),
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(new TextBlock { Text = "Hình dạng", Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(_shapeBox);

        _widthLabel = new TextBlock { Text = "Rộng (mm)", Width = 150, VerticalAlignment = VerticalAlignment.Center };
        _widthBox = new TextBox { Text = "200", Padding = new Thickness(7, 5, 7, 5) };
        panel.Children.Add(CreateRow(_widthLabel, _widthBox));

        _heightRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        _heightRow.Children.Add(new TextBlock { Text = "Cao (mm)", Width = 150, VerticalAlignment = VerticalAlignment.Center });
        _heightBox = new TextBox { Text = "100", Padding = new Thickness(7, 5, 7, 5) };
        _heightRow.Children.Add(_heightBox);
        panel.Children.Add(_heightRow);

        _sidesRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        _sidesRow.Children.Add(new TextBlock { Text = "Số cạnh (3–24)", Width = 150, VerticalAlignment = VerticalAlignment.Center });
        _sidesBox = new TextBox { Text = "6", Padding = new Thickness(7, 5, 7, 5) };
        _sidesRow.Children.Add(_sidesBox);
        panel.Children.Add(_sidesRow);

        _error = new TextBlock
        {
            Foreground = Brush(owner, "DangerBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };
        panel.Children.Add(_error);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(new Button { Content = "Hủy", IsCancel = true, MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) });
        var add = new Button { Content = "Thêm chi tiết", IsDefault = true, MinWidth = 120, Padding = new Thickness(12, 6, 12, 6) };
        add.SetResourceReference(StyleProperty, "PrimaryButton");
        add.Click += Add_Click;
        buttons.Children.Add(add);
        panel.Children.Add(buttons);

        Content = panel;
        _shapeBox.SelectionChanged += Shape_SelectionChanged;
        Shape_SelectionChanged(this, null!);
    }

    private void Shape_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
    {
        if (_shapeBox.SelectedItem is not ShapeOption option) return;
        _widthLabel.Text = option.Shape is StandardPartShape.Circle or StandardPartShape.Polygon
            ? "Đường kính (mm)"
            : "Rộng (mm)";
        _heightRow.Visibility = option.Shape is StandardPartShape.Circle or StandardPartShape.Polygon
            ? Visibility.Collapsed
            : Visibility.Visible;
        _sidesRow.Visibility = option.Shape == StandardPartShape.Polygon
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_shapeBox.SelectedItem is not ShapeOption option) return;
        if (!TryPositive(_widthBox.Text, out double width))
        {
            _error.Text = "Kích thước phải là số dương hữu hạn.";
            return;
        }

        double height = width;
        if (option.Shape is not (StandardPartShape.Circle or StandardPartShape.Polygon) &&
            !TryPositive(_heightBox.Text, out height))
        {
            _error.Text = "Chiều cao phải là số dương hữu hạn.";
            return;
        }
        int sides = 0;
        if (option.Shape == StandardPartShape.Polygon &&
            (!int.TryParse(_sidesBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out sides) || sides is < 3 or > 24))
        {
            _error.Text = "Đa giác cần từ 3 đến 24 cạnh.";
            return;
        }

        Shape = option.Shape;
        ShapeWidth = width;
        ShapeHeight = height;
        Sides = sides;
        DialogResult = true;
    }

    private static bool TryPositive(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result) &&
        double.IsFinite(result) && result > 0;

    private static StackPanel CreateRow(TextBlock label, TextBox input)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(label);
        row.Children.Add(input);
        return row;
    }

    private static Brush Brush(Window owner, string key) =>
        owner.TryFindResource(key) as Brush ?? SystemColors.ControlTextBrush;

    private sealed record ShapeOption(StandardPartShape Shape, string Label);
}
