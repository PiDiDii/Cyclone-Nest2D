using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CycloneNest2D.Core;

namespace CycloneNest2D.UI;

public sealed record SheetPreset(string Name, double Width, double Height,
                                 string Material = "Chưa phân loại", double Thickness = 0)
{
    public string Note { get; init; } = "";
    public string AssignmentLabel => $"{Material} · {Thickness:0.##} mm · {Width:0.##} × {Height:0.##} mm";
    public override string ToString() => AssignmentLabel;
}

public sealed record DirectionOption(NestDirection Direction, string Text)
{
    public override string ToString() => Text;
}

/// <summary>Thông số khổ tấm và thuật toán nesting hình dạng thật. Có thể lưu/nạp JSON.</summary>
public sealed class NestSettings : ObservableObject
{
    private double _sheetWidth = 2500, _sheetHeight = 1250, _margin = 5, _spacing = 3;
    private NestDirection _direction = NestDirection.FillWidth;
    private bool _allowRotation = true;
    private double _maxSeconds = 30, _targetUtilizationPercent = 85, _rotationStep = 90;

    public ObservableCollection<SheetPreset> SheetPresets { get; } = new(Presets);

    public event Action? Changed;
    private bool Upd<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!Set(ref field, value, name)) return false;
        Changed?.Invoke();
        return true;
    }

    public static IReadOnlyList<SheetPreset> Presets { get; } = new[]
    {
        new SheetPreset("1250 x 2500", 2500, 1250),
        new SheetPreset("1500 x 3000", 3000, 1500),
        new SheetPreset("1000 x 2000", 2000, 1000),
        new SheetPreset("1220 x 2440 (4x8 ft)", 2440, 1220),
        new SheetPreset("1500 x 6000", 6000, 1500),
        new SheetPreset("2000 x 6000", 6000, 2000),
    };

    public static IReadOnlyList<DirectionOption> Directions { get; } = new[]
    {
        new DirectionOption(NestDirection.FillWidth, "Ưu tiên lấp theo chiều rộng (giảm chiều dài dùng)"),
        new DirectionOption(NestDirection.FillLength, "Ưu tiên lấp theo chiều dài (giảm bề rộng dùng)"),
    };

    public static IReadOnlyList<double> RotationSteps { get; } = new double[] { 90, 45, 30, 15 };

    public double SheetWidth { get => _sheetWidth; set => Upd(ref _sheetWidth, value); }
    public double SheetHeight { get => _sheetHeight; set => Upd(ref _sheetHeight, value); }
    /// <summary>Khoảng cách từ part đến mép khổ.</summary>
    public double Margin { get => _margin; set => Upd(ref _margin, value); }
    /// <summary>Khoảng cách tối thiểu giữa hai part.</summary>
    public double Spacing { get => _spacing; set => Upd(ref _spacing, value); }
    public NestDirection Direction { get => _direction; set => Upd(ref _direction, value); }
    public bool AllowRotation { get => _allowRotation; set => Upd(ref _allowRotation, value); }
    public double MaxSeconds { get => _maxSeconds; set => Upd(ref _maxSeconds, value); }
    /// <summary>0 nghĩa là không dừng sớm theo hiệu suất; thời hạn vẫn luôn có hiệu lực.</summary>
    public double TargetUtilizationPercent { get => _targetUtilizationPercent; set => Upd(ref _targetUtilizationPercent, value); }
    public double RotationStep { get => _rotationStep; set => Upd(ref _rotationStep, value); }

    public void ApplyPreset(SheetPreset preset)
    {
        SheetWidth = preset.Width;
        SheetHeight = preset.Height;
    }

    /// <summary>null nếu hợp lệ, ngược lại là thông báo lỗi.</summary>
    public string? Validate()
    {
        if (!(SheetWidth > 0 && SheetHeight > 0) || !double.IsFinite(SheetWidth) || !double.IsFinite(SheetHeight)) return "Kích thước tấm phải lớn hơn 0.";
        if (Margin < 0 || !double.IsFinite(Margin)) return "Khoảng cách với lề mép khổ không được âm.";
        if (Spacing < 0 || !double.IsFinite(Spacing)) return "Khoảng cách giữa các part không được âm.";
        if (2 * Margin >= Math.Min(SheetWidth, SheetHeight)) return "Khoảng cách với lề mép khổ quá lớn so với kích thước tấm.";
        if (!(MaxSeconds >= 0.5 && MaxSeconds <= 3600) || !double.IsFinite(MaxSeconds)) return "Giới hạn thời gian nesting phải từ 0,5 đến 3600 giây.";
        if (!(TargetUtilizationPercent >= 0 && TargetUtilizationPercent <= 100) || !double.IsFinite(TargetUtilizationPercent)) return "Mục tiêu hiệu suất phải từ 0 đến 100%. Nhập 0 để tắt dừng sớm theo mục tiêu.";
        if (!Enum.IsDefined(Direction)) return "Hướng lấp đầy không hợp lệ.";
        if (!(RotationStep >= 5 && RotationStep <= 180) || !double.IsFinite(RotationStep)) return "Bước xoay phải nằm trong 5 đến 180 độ.";
        return null;
    }

    public SheetDefinition ToSheet() => new(SheetWidth, SheetHeight, Margin, Spacing);

    public NestOptions ToOptions(CancellationToken cancel = default, Action<string>? report = null) => new()
    {
        Direction = Direction,
        AllowRotation = AllowRotation,
        MaxSeconds = MaxSeconds,
        TargetUtilizationPercent = TargetUtilizationPercent,
        RotationStep = RotationStep,
        Cancel = cancel,
        Report = report,
    };

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private sealed class Dto
    {
        public double SheetWidth { get; set; } = 2500;
        public double SheetHeight { get; set; } = 1250;
        public double Margin { get; set; } = 5;
        public double Spacing { get; set; } = 3;
        public string Direction { get; set; } = nameof(NestDirection.FillWidth);
        public bool AllowRotation { get; set; } = true;
        public double MaxSeconds { get; set; } = 30;
        public double TargetUtilizationPercent { get; set; } = 85;
        public double RotationStep { get; set; } = 90;
        public List<SheetPreset>? SheetPresets { get; set; }
    }

    public string ToJson() => JsonSerializer.Serialize(new Dto
    {
        SheetWidth = SheetWidth,
        SheetHeight = SheetHeight,
        Margin = Margin,
        Spacing = Spacing,
        Direction = Direction.ToString(),
        AllowRotation = AllowRotation,
        MaxSeconds = MaxSeconds,
        TargetUtilizationPercent = TargetUtilizationPercent,
        RotationStep = RotationStep,
        SheetPresets = SheetPresets.ToList(),
    }, Json);

    /// <summary>Nạp JSON cũ; Mode/MaxRects và advanced fields cũ bị bỏ qua. JSON hoặc giá trị sai sẽ ném lỗi.</summary>
    public void FromJson(string json)
    {
        Dto dto = JsonSerializer.Deserialize<Dto>(json)
                  ?? throw new InvalidDataException("File thiết lập không có dữ liệu.");
        if (!Enum.TryParse<NestDirection>(dto.Direction, true, out var direction) || !Enum.IsDefined(direction))
            throw new InvalidDataException($"Hướng lấp đầy '{dto.Direction}' không hợp lệ.");

        var candidate = new NestSettings
        {
            SheetWidth = dto.SheetWidth,
            SheetHeight = dto.SheetHeight,
            Margin = dto.Margin,
            Spacing = dto.Spacing,
            Direction = direction,
            AllowRotation = dto.AllowRotation,
            MaxSeconds = dto.MaxSeconds,
            TargetUtilizationPercent = dto.TargetUtilizationPercent,
            RotationStep = dto.RotationStep
        };
        string? validationError = candidate.Validate();
        if (validationError != null)
            throw new InvalidDataException($"Thiết lập nesting không hợp lệ: {validationError}");

        List<SheetPreset>? validatedPresets = null;
        if (dto.SheetPresets != null)
        {
            validatedPresets = new List<SheetPreset>(dto.SheetPresets.Count);
            for (int i = 0; i < dto.SheetPresets.Count; i++)
            {
                var preset = dto.SheetPresets[i];
                if (preset is null)
                    throw new InvalidDataException($"Khổ tấm thứ {i + 1} không có dữ liệu.");
                if (string.IsNullOrWhiteSpace(preset.Name))
                    throw new InvalidDataException($"Khổ tấm thứ {i + 1} thiếu tên.");
                if (!(preset.Width > 0) || !double.IsFinite(preset.Width) ||
                    !(preset.Height > 0) || !double.IsFinite(preset.Height))
                    throw new InvalidDataException($"Khổ tấm thứ {i + 1} phải có chiều dài và chiều rộng lớn hơn 0.");
                if (preset.Thickness < 0 || !double.IsFinite(preset.Thickness))
                    throw new InvalidDataException($"Độ dày của khổ tấm thứ {i + 1} không hợp lệ.");

                validatedPresets.Add(preset with
                {
                    Name = preset.Name.Trim(),
                    Material = string.IsNullOrWhiteSpace(preset.Material) ? "Chưa phân loại" : preset.Material.Trim(),
                    Note = preset.Note?.Trim() ?? ""
                });
            }
        }

        if (validatedPresets != null)
        {
            SheetPresets.Clear();
            foreach (var preset in validatedPresets)
                SheetPresets.Add(preset);
        }

        SheetWidth = dto.SheetWidth;
        SheetHeight = dto.SheetHeight;
        Margin = dto.Margin;
        Spacing = dto.Spacing;
        Direction = direction;
        AllowRotation = dto.AllowRotation;
        MaxSeconds = dto.MaxSeconds;
        TargetUtilizationPercent = dto.TargetUtilizationPercent;
        RotationStep = dto.RotationStep;
    }

    public void ResetDefaults() => FromJson(JsonSerializer.Serialize(new Dto(), Json));

    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nesting2D", "settings.json");

    public void SaveTo(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporaryPath = fullPath + ".tmp";
        File.WriteAllText(temporaryPath, ToJson());
        File.Move(temporaryPath, fullPath, true);
    }

    public void LoadFrom(string path)
    {
        if (!File.Exists(path)) return;
        FromJson(File.ReadAllText(path));
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{SheetWidth}x{SheetHeight} lề {Margin} khe {Spacing}");
}
