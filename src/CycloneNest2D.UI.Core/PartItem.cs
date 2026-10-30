using System.Collections.ObjectModel;
using CycloneNest2D.Core;

namespace CycloneNest2D.UI;

public enum PartKind { Dxf, Rect }

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
    private bool _suspend;
    private DxfPart? _dxf;

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
    public string KindText => Kind == PartKind.Dxf ? "DXF" : "Chữ nhật";
    public string? Error { get => _error; private set { if (Set(ref _error, value)) OnPropertyChanged(nameof(IsValid)); } }
    public string Info { get => _info; private set => Set(ref _info, value); }
    public bool IsValid => Error == null && Width > 0 && Height > 0;
    public DxfShape? Geometry => _dxf?.Geometry;

    public static PartItem NewRect(string name, double w, double h, int qty = 1, bool canRotate = true) =>
        new()
        {
            Kind = PartKind.Rect,
            _name = name, _width = w, _height = h, _quantity = qty, _canRotate = canRotate,
            _info = "Hình chữ nhật nhập tay"
        };

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
        Error = null;
        Info = $"{p.Width:0.##} x {p.Height:0.##} mm · {p.UnitSource}";
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(Warnings));
    }

    private void ReloadGeometry(bool notify = true)
    {
        var names = CheckedLayerNames.ToList();
        if (names.Count == 0)
        {
            _dxf = null;
            _width = _height = 0;
            Error = "Chưa chọn layer nào";
            Info = Error;
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Height));
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
                Error = ex.Message;
                Info = ex.Message;
                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(Height));
            }
        }
        if (notify) Changed?.Invoke(this);
    }

    /// <summary>Chuyển thành định nghĩa cho engine. Chỉ gọi khi IsValid.</summary>
    public PartDefinition ToDefinition()
    {
        var shape = _dxf?.Geometry;
        if (shape != null && Flip) shape = ShapeOps.MirrorHorizontal(shape);
        return shape != null
            ? new PartDefinition(Name, shape.Width, shape.Height, Quantity, CanRotate, shape)
            : new PartDefinition(Name, Width, Height, Quantity, CanRotate);
    }
}
