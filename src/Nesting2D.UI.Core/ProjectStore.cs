using System.Text.Json;

namespace Nesting2D.UI;

/// <summary>File dự án .nest2d (JSON): thông số + danh sách chi tiết. Đường dẫn DXF lưu tương đối với file dự án.</summary>
public static class ProjectStore
{
    public const string Extension = ".nest2d";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public sealed class PartDto
    {
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public string Material { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public bool CanRotate { get; set; } = true;
        public bool Flip { get; set; }
        public double Thickness { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string? Path { get; set; }
        public List<string>? Layers { get; set; }
    }

    public sealed class ProjectDto
    {
        public int Version { get; set; } = 1;
        public string? Settings { get; set; }
        public List<PartDto> Parts { get; set; } = new();
    }

    public static void Save(string path, NestSettings settings, IEnumerable<PartItem> parts)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var dto = new ProjectDto { Settings = settings.ToJson() };
        foreach (var p in parts)
        {
            var d = new PartDto { Kind = p.Kind.ToString(), Name = p.Name, Material = p.Material, Quantity = p.Quantity, CanRotate = p.CanRotate, Flip = p.Flip, Thickness = p.Thickness };
            if (p.Kind == PartKind.Rect) { d.Width = p.Width; d.Height = p.Height; }
            else { d.Path = Path.GetRelativePath(dir, p.SourcePath); d.Layers = p.CheckedLayerNames.ToList(); }
            dto.Parts.Add(d);
        }
        File.WriteAllText(path, JsonSerializer.Serialize(dto, Json));
    }

    /// <summary>Nạp dự án. File DXF thiếu/lỗi không làm hỏng cả dự án: được ghi vào <paramref name="problems"/>.</summary>
    public static List<PartItem> Load(string path, NestSettings settings, List<string> problems)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var dto = JsonSerializer.Deserialize<ProjectDto>(File.ReadAllText(path))
                  ?? throw new InvalidDataException("File dự án rỗng.");
        if (dto.Settings != null) settings.FromJson(dto.Settings);

        var list = new List<PartItem>();
        foreach (var d in dto.Parts)
        {
            if (d.Kind == nameof(PartKind.Rect))
            {
                list.Add(PartItem.NewRect(d.Name, d.Width, d.Height, d.Quantity, d.CanRotate));
                list[^1].Flip = d.Flip;
                list[^1].RestoreMaterialThickness(d.Material, d.Thickness);
                continue;
            }
            string full = Path.GetFullPath(Path.Combine(dir, d.Path ?? ""));
            if (!File.Exists(full)) { problems.Add($"Không tìm thấy file: {full}"); continue; }
            try
            {
                var item = PartItem.FromDxf(full, d.Layers is { Count: > 0 } ? d.Layers : null);
                item.Name = d.Name;
                item.Quantity = d.Quantity;
                item.CanRotate = d.CanRotate;
                item.RestoreMaterialThickness(d.Material, d.Thickness);
                item.Flip = d.Flip;
                list.Add(item);
            }
            catch (Exception ex) { problems.Add($"{Path.GetFileName(full)}: {ex.Message}"); }
        }
        return list;
    }
}
