using System.IO;
using System.Text.Json;
using System.Windows;

namespace CycloneNest2D.Desktop;

internal enum PartGridTextAlignment
{
    Left,
    Center,
    Right
}

internal sealed class PartGridColumnAlignment
{
    public PartGridTextAlignment Header { get; set; } = PartGridTextAlignment.Center;
    public PartGridTextAlignment Content { get; set; } = PartGridTextAlignment.Center;
}

internal sealed class PartGridAlignmentSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly Dictionary<string, PartGridColumnAlignment> _columns = new(StringComparer.OrdinalIgnoreCase);

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Nesting2D", "part-grid-alignment.json");

    public PartGridColumnAlignment Get(string columnName) =>
        _columns.TryGetValue(columnName, out var value) ? value : new PartGridColumnAlignment();

    public void Set(string columnName, PartGridColumnAlignment alignment) =>
        _columns[columnName] = alignment;

    public static PartGridAlignmentSettings Load(string path)
    {
        if (!File.Exists(path)) return new PartGridAlignmentSettings();
        var stored = JsonSerializer.Deserialize<Dictionary<string, PartGridColumnAlignment>>(File.ReadAllText(path), Json)
                     ?? throw new InvalidDataException("Thiết lập căn chỉnh cột đang trống hoặc không hợp lệ.");
        var settings = new PartGridAlignmentSettings();
        foreach (var (name, alignment) in stored)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                !Enum.IsDefined(alignment.Header) || !Enum.IsDefined(alignment.Content))
                throw new InvalidDataException($"Thiết lập căn chỉnh cho cột '{name}' không hợp lệ.");
            settings.Set(name, alignment);
        }
        return settings;
    }

    public void Save(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = fullPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_columns, Json));
        File.Move(temporaryPath, fullPath, true);
    }

    public static HorizontalAlignment ToHorizontalAlignment(PartGridTextAlignment alignment) => alignment switch
    {
        PartGridTextAlignment.Left => HorizontalAlignment.Left,
        PartGridTextAlignment.Right => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Center
    };

    public static System.Windows.TextAlignment ToTextAlignment(PartGridTextAlignment alignment) => alignment switch
    {
        PartGridTextAlignment.Left => System.Windows.TextAlignment.Left,
        PartGridTextAlignment.Right => System.Windows.TextAlignment.Right,
        _ => System.Windows.TextAlignment.Center
    };
}
