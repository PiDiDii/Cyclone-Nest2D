using System.Collections.ObjectModel;
using CycloneNest2D.Core;

namespace CycloneNest2D.UI;

public enum PartKind { Dxf, Rect, Template }
public enum StandardPartShape { Rectangle, Circle, Triangle, Polygon }

public sealed class LayerItem : ObservableObject
{
    private bool _isChecked;
    public LayerItem(string name, bool isChecked, string note)
    {
        Name = name;
        _isChecked = isChecked;
        Note = note;
    }
    public string Name { get; }
    /// <summary>Mô tả ngắn, ví dụ "loại tự động" hoặc số hình.</summary>
    public string Note { get; }
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
}

/// <summary>Một dòng trong bảng chi tiết: file DXF hoặc hình chữ nhật nhập tay.</summary>
public sealed class PartItem : ObservableObject
{
    private string _name = "";
    private int _quantity = 1;
    private int? _remainingQuantity;
    private bool _canRotate = true;
    private bool _flip;
    private double _thickness;
    private double _width, _height;
    private int _sequence;
    private string _material = "";
    private SheetPreset? _sheetPreset;
    private string? _error;
    private string _info = "";
    private string _textInfo = "";
    private bool _suspend;
    private DxfPart? _dxf;
    private DxfShape? _templateGeometry;
    private StandardPartShape? _templateShape;
    private int _templateSides;
    private double _templateSize;

    public event Action<PartItem>? Changed;

    public PartKind Kind { get; private init; }
    public string SourcePath { get; private init; } = "";
    public ObservableCollection<LayerItem> Layers { get; } = new();
    public IReadOnlyList<string> Warnings { get; private set; } = Array.Empty<string>();
    public bool HasLayers => Kind == PartKind.Dxf && Layers.Count > 1;

    public string Name { get => _name; set { if (Set(ref _name, value)) Changed?.Invoke(this); } }
    public int Sequence { get => _sequence; private set => Set(ref _sequence, value); }
    public void SetSequence(int value) => Sequence = value;
    public string Material
    {
        get => _material;
        set { if (Set(ref _material, (value ?? "").Trim())) { OnPropertyChanged(nameof(MaterialThicknessLabel)); Changed?.Invoke(this); } }
    }
    public string MaterialThicknessLabel => string.IsNullOrWhiteSpace(Material) || Thickness <= 0
        ? "Chọn vật liệu / độ dày" : $"{Material} · {Thickness:0.###} mm";
    public SheetPreset? AssignedPreset
    {
        get => _sheetPreset;
        set
        {
            if (!Set(ref _sheetPreset, value)) return;
            if (value != null)
            {
                Material = value.Material;
                Thickness = value.Thickness;
            }
            else
            {
                Material = "";
                Thickness = 0;
            }
            OnPropertyChanged(nameof(MaterialThicknessLabel));
            Changed?.Invoke(this);
        }
    }
    public void AssignPreset(SheetPreset preset) => AssignedPreset = preset;
    public void RestoreMaterialThickness(string material, double thickness)
    {
        _sheetPreset = null;
        Material = material;
        Thickness = thickness;
        OnPropertyChanged(nameof(AssignedPreset));
        OnPropertyChanged(nameof(MaterialThicknessLabel));
    }
    public int Quantity { get => _quantity; set { if (Set(ref _quantity, Math.Max(0, value))) { OnPropertyChanged(nameof(RemainingQuantity)); Changed?.Invoke(this); } } }
    /// <summary>Instances currently remaining to place; UI projection only, not persisted as source quantity.</summary>
    public int RemainingQuantity => Math.Clamp(_remainingQuantity ?? Quantity, 0, Quantity);
    internal void SetRemainingQuantity(int value)
    {
        int next = Math.Clamp(value, 0, Quantity);
        if (_remainingQuantity == next) return;
        _remainingQuantity = next;
        OnPropertyChanged(nameof(RemainingQuantity));
    }
    public bool CanRotate { get => _canRotate; set { if (Set(ref _canRotate, value)) Changed?.Invoke(this); } }
    public bool Flip { get => _flip; set { if (Set(ref _flip, value)) Changed?.Invoke(this); } }
    public double Thickness
    {
        get => _thickness;
        set { if (Set(ref _thickness, Math.Max(0, value))) { OnPropertyChanged(nameof(MaterialThicknessLabel)); Changed?.Invoke(this); } }
    }

    /// <summary>Chữ nhật: sửa được. DXF: tính từ hình, chỉ đọc.</summary>
    public double Width
    {
        get => _width;
        set { if (Kind == PartKind.Rect && Set(ref _width, Math.Max(0, value))) Changed?.Invoke(this); }
    }
    public double Height
    {
        get => _height;
        set { if (Kind == PartKind.Rect && Set(ref _height, Math.Max(0, value))) Changed?.Invoke(this); }
    }

    public bool IsDxf => Kind == PartKind.Dxf;
    public bool IsTemplate => Kind == PartKind.Template;
    public StandardPartShape? TemplateShape => _templateShape;
    public int TemplateSides => _templateSides;
    public double TemplateSize => _templateSize;
    public string KindText => Kind switch
    {
        PartKind.Dxf => "DXF",
        PartKind.Template => _templateShape switch
        {
            StandardPartShape.Circle => "Hình tròn",
            StandardPartShape.Triangle => "Tam giác",
            StandardPartShape.Polygon => $"Đa giác {_templateSides} cạnh",
            _ => "Chữ nhật"
        },
        _ => "Chữ nhật"
    };
    public string? Error { get => _error; private set { if (Set(ref _error, value)) OnPropertyChanged(nameof(IsValid)); } }
    public string Info { get => _info; private set => Set(ref _info, value); }
    public bool IsValid => Error == null && Width > 0 && Height > 0;
    public DxfShape? Geometry => _dxf?.Geometry ?? _templateGeometry;
    public string TextInfo { get => _textInfo; private set => Set(ref _textInfo, value); }

    public static PartItem NewRect(string name, double w, double h, int qty = 1, bool canRotate = true) =>
        new()
        {
            Kind = PartKind.Rect,
            _name = name,
            _width = w,
            _height = h,
            _quantity = qty,
            _canRotate = canRotate,
            _info = "Hình chữ nhật nhập tay"
        };

    public static PartItem NewTemplate(string name, StandardPartShape shape, double width, double height,
        int sides = 6, int qty = 1, bool canRotate = true)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Kích thước mẫu phải là số dương hữu hạn.");
        if (shape is StandardPartShape.Circle or StandardPartShape.Polygon && Math.Abs(width - height) > 1e-6)
            throw new ArgumentException("Hình tròn và đa giác đều cần kích thước rộng và cao bằng nhau.");
        if (shape == StandardPartShape.Polygon && sides is < 3 or > 24)
            throw new ArgumentOutOfRangeException(nameof(sides), "Đa giác cần từ 3 đến 24 cạnh.");

        var item = new PartItem
        {
            Kind = PartKind.Template,
            _name = name,
            _width = width,
            _height = height,
            _quantity = qty,
            _canRotate = canRotate,
            _templateShape = shape,
            _templateSides = shape == StandardPartShape.Polygon ? sides : 0,
            _templateSize = shape is StandardPartShape.Circle or StandardPartShape.Polygon ? width : 0,
            _info = $"Thư viện mẫu · {ShapeLabel(shape, sides)}"
        };
        item._templateGeometry = BuildTemplateGeometry(name, shape, width, height, item._templateSides);
        item._width = item._templateGeometry.Width;
        item._height = item._templateGeometry.Height;
        return item;
    }

    public void ResizeTemplate(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Kích thước mẫu phải là số dương hữu hạn.");
        if (Kind == PartKind.Rect)
        {
            Width = width;
            Height = height;
            return;
        }
        if (Kind != PartKind.Template || _templateShape == null)
            throw new InvalidOperationException("Chỉ mẫu tiêu chuẩn mới có thể đổi kích thước.");
        if (_templateShape is StandardPartShape.Circle or StandardPartShape.Polygon &&
            Math.Abs(width - height) > 1e-6)
            throw new ArgumentException("Hình tròn và đa giác đều cần kích thước rộng và cao bằng nhau.");

        _templateGeometry = BuildTemplateGeometry(Name, _templateShape.Value, width, height, _templateSides);
        _templateSize = _templateShape is StandardPartShape.Circle or StandardPartShape.Polygon ? width : 0;
        _width = _templateGeometry.Width;
        _height = _templateGeometry.Height;
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        Changed?.Invoke(this);
    }

    private static string ShapeLabel(StandardPartShape shape, int sides) => shape switch
    {
        StandardPartShape.Circle => "Hình tròn",
        StandardPartShape.Triangle => "Tam giác",
        StandardPartShape.Polygon => $"Đa giác {sides} cạnh",
        _ => "Chữ nhật"
    };

    private static DxfShape BuildTemplateGeometry(string name, StandardPartShape shape, double width, double height, int sides)
    {
        var segments = new List<Seg>();
        if (shape == StandardPartShape.Circle)
        {
            segments.Add(new CircleSeg(width / 2, height / 2, width / 2, "PARTS"));
        }
        else
        {
            int count = shape switch
            {
                StandardPartShape.Rectangle => 4,
                StandardPartShape.Triangle => 3,
                StandardPartShape.Polygon => sides,
                _ => throw new ArgumentOutOfRangeException(nameof(shape))
            };
            var points = new (double X, double Y)[count];
            if (shape == StandardPartShape.Rectangle)
                points = [(0, 0), (width, 0), (width, height), (0, height)];
            else if (shape == StandardPartShape.Triangle)
                points = [(0, 0), (width, 0), (width / 2, height)];
            else
            {
                double radius = width / 2;
                for (int i = 0; i < count; i++)
                {
                    double angle = -Math.PI / 2 + i * Math.PI * 2 / count;
                    points[i] = (width / 2 + radius * Math.Cos(angle), height / 2 + radius * Math.Sin(angle));
                }
            }
            for (int i = 0; i < points.Length; i++)
            {
                var start = points[i];
                var end = points[(i + 1) % points.Length];
                segments.Add(new LineSeg(start.X, start.Y, end.X, end.Y, "PARTS"));
            }
        }

        return new DxfShape(name, segments, 1,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["PARTS"] = segments.Count },
            Array.Empty<string>(), 0);
    }

    /// <summary>Đọc file DXF. Ném ngoại lệ nếu file không đọc được (để bên gọi báo lỗi và bỏ qua file).</summary>
    public static PartItem FromDxf(string path, IEnumerable<string>? checkedLayers = null)
    {
        var item = new PartItem { Kind = PartKind.Dxf, SourcePath = path };
        var first = DxfReader.Read(path);
        item._name = first.Name;
        item._quantity = first.Quantity;
        item._canRotate = first.CanRotate;

        foreach (var l in first.OutputLayers)
            item.Layers.Add(new LayerItem(l, true, CountText(first, l)));
        foreach (var l in first.ExcludedLayers)
            item.Layers.Add(new LayerItem(l, false, "loại tự động"));
        foreach (var l in item.Layers) l.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LayerItem.IsChecked) && !item._suspend) item.ReloadGeometry();
        };

        if (checkedLayers != null)
        {
            var set = new HashSet<string>(checkedLayers, StringComparer.OrdinalIgnoreCase);
            item._suspend = true;
            foreach (var l in item.Layers) l.IsChecked = set.Contains(l.Name);
            item._suspend = false;
            item.ReloadGeometry(notify: false);
        }
        else item.Apply(first);
        return item;
    }

    public IEnumerable<string> CheckedLayerNames => Layers.Where(l => l.IsChecked).Select(l => l.Name);

    private static string CountText(DxfPart p, string layer) =>
        p.Geometry.Layers.TryGetValue(layer, out var n) ? $"{n} hình" : "";

    private void Apply(DxfPart p)
    {
        _dxf = p;
        Warnings = p.Warnings.ToList();
        _width = p.Width;
        _height = p.Height;
        var texts = p.Geometry.Texts;
        if (texts.Count == 0)
            TextInfo = "";
        else
        {
            int textCount = texts.Count(text => text.Type == "TEXT");
            int mtextCount = texts.Count(text => text.Type == "MTEXT");
            var loops = ShapeOps.Build(p.Geometry);
            int innerCount = texts.Count(text => ShapeOps.ContainsInnerContour(loops, text.AnchorX, text.AnchorY));
            TextInfo = $"Giữ lại {textCount} TEXT, {mtextCount} MTEXT khi xuất" +
                       (innerCount > 0 ? $" · {innerCount} chữ nằm trong biên dạng trong, không dùng làm đường cắt." : ".");
        }
        Error = null;
        Info = $"{p.Width:0.##} x {p.Height:0.##} mm";
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(TextInfo));
    }

    private void ReloadGeometry(bool notify = true)
    {
        var names = CheckedLayerNames.ToList();
        if (names.Count == 0)
        {
            _dxf = null;
            _width = _height = 0;
            Warnings = Array.Empty<string>();
            TextInfo = "";
            Error = "Chưa chọn layer nào";
            Info = Error;
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Height));
            OnPropertyChanged(nameof(Warnings));
        }
        else
        {
            try
            {
                Apply(DxfReader.Read(SourcePath, new DxfReadOptions { IncludeLayers = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase) }));
            }
            catch (Exception ex)
            {
                _dxf = null;
                _width = _height = 0;
                Warnings = Array.Empty<string>();
                TextInfo = "";
                Error = ex.Message;
                Info = ex.Message;
                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(Height));
                OnPropertyChanged(nameof(Warnings));
            }
        }
        if (notify) Changed?.Invoke(this);
    }

    /// <summary>Chuyển thành định nghĩa cho engine. Chỉ gọi khi IsValid.</summary>
    public PartDefinition ToDefinition()
    {
        var shape = Geometry;
        if (shape != null && Flip) shape = ShapeOps.MirrorHorizontal(shape);
        return shape != null
            ? new PartDefinition(Name, shape.Width, shape.Height, Quantity, CanRotate, shape)
            : new PartDefinition(Name, Width, Height, Quantity, CanRotate);
    }
}
