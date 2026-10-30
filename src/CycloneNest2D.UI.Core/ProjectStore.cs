using System.Text.Json;
using CycloneNest2D.Core;

namespace CycloneNest2D.UI;

/// <summary>Persists project indexes and independent material/thickness workspaces.</summary>
public static class ProjectStore
{
    public const string Extension = ".nest2d";
    public const string ProjectKind = "CycloneNest2D.Project";
    public const string GroupKind = "CycloneNest2D.Group";
    public const int CurrentFormatVersion = 3;
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
        public string? TemplateShape { get; set; }
        public int TemplateSides { get; set; }
        public double TemplateSize { get; set; }
    }

    public sealed class GroupReferenceDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public int PartCount { get; set; }
        public int SheetCount { get; set; }
    }

    public sealed class ProjectDto
    {
        public string? Kind { get; set; }
        public int Version { get; set; } = 1;
        public string? Settings { get; set; }
        public List<PartDto> Parts { get; set; } = new();
        public List<GroupReferenceDto> Groups { get; set; } = new();
        public NestResultDto? Result { get; set; }
        public SheetDefinitionDto? ResultSheet { get; set; }
        public bool IsStale { get; set; }
        public bool IsEdited { get; set; }
        public double LastNestDurationSeconds { get; set; }
        public DateTime? LastNestAt { get; set; }
    }

    public sealed class WorkspaceSnapshot
    {
        public List<PartItem> Parts { get; init; } = new();
        public NestResult? Result { get; init; }
        public SheetDefinition? ResultSheet { get; init; }
        public bool IsStale { get; init; }
        public bool IsEdited { get; init; }
        public TimeSpan LastNestDuration { get; init; }
        public DateTime? LastNestAt { get; init; }
    }

    public static string DataDirectory(string projectPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!,
            Path.GetFileNameWithoutExtension(projectPath) + ".data");

    public static bool IsProjectIndex(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty(nameof(ProjectDto.Kind), out var kind) &&
               kind.GetString() == ProjectKind;
    }

    public static List<GroupReferenceDto> LoadProjectIndex(string path)
    {
        var dto = JsonSerializer.Deserialize<ProjectDto>(File.ReadAllText(path), Json)
                  ?? throw new InvalidDataException("File dự án rỗng.");
        if (dto.Kind != ProjectKind)
            throw new InvalidDataException("File không phải dự án nhiều nhóm.");
        EnsureSupportedVersion(dto);
        if (dto.Groups.Count == 0)
            throw new InvalidDataException("Dự án chưa có nhóm vật liệu nào.");
        return dto.Groups;
    }

    public static void SaveProjectIndex(string path, IReadOnlyList<GroupReferenceDto> groups)
    {
        if (groups.Count == 0) throw new InvalidOperationException("Dự án phải có ít nhất một nhóm vật liệu.");
        string fullPath = Path.GetFullPath(path);
        string dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        var dto = new ProjectDto
        {
            Kind = ProjectKind,
            Version = CurrentFormatVersion,
            Groups = groups.Select(group => new GroupReferenceDto
            {
                Id = group.Id,
                Name = group.Name,
                Path = Path.GetRelativePath(dir, Path.GetFullPath(group.Path)),
                PartCount = group.PartCount,
                SheetCount = group.SheetCount
            }).ToList()
        };
        WriteAtomically(fullPath, JsonSerializer.Serialize(dto, Json));
    }

    public static void Save(string path, NestSettings settings, IEnumerable<PartItem> parts) =>
        SaveWorkspace(path, settings, parts, null, null, false, false, TimeSpan.Zero, null);

    public static void SaveWorkspace(string path, NestSettings settings, IEnumerable<PartItem> parts,
        NestResult? result, SheetDefinition? resultSheet, bool isStale, bool isEdited,
        TimeSpan lastNestDuration, DateTime? lastNestAt)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var dto = new ProjectDto { Kind = GroupKind, Version = CurrentFormatVersion, Settings = settings.ToJson() };
        foreach (var part in parts)
        {
            var item = new PartDto
            {
                Kind = part.Kind.ToString(), Name = part.Name, Material = part.Material,
                Quantity = part.Quantity, CanRotate = part.CanRotate, Flip = part.Flip,
                Thickness = part.Thickness
            };
            if (part.Kind is PartKind.Rect or PartKind.Template)
            {
                item.Width = part.Width;
                item.Height = part.Height;
                if (part.Kind == PartKind.Template)
                {
                    item.TemplateShape = part.TemplateShape?.ToString();
                    item.TemplateSides = part.TemplateSides;
                    item.TemplateSize = part.TemplateSize;
                }
            }
            else
            {
                item.Path = Path.GetRelativePath(dir, part.SourcePath);
                item.Layers = part.CheckedLayerNames.ToList();
            }
            dto.Parts.Add(item);
        }

        dto.Result = result == null ? null : ToDto(result);
        if (resultSheet != null)
            dto.ResultSheet = new SheetDefinitionDto
            {
                Width = resultSheet.Width, Height = resultSheet.Height,
                Margin = resultSheet.Margin, Spacing = resultSheet.Spacing
            };
        dto.IsStale = isStale;
        dto.IsEdited = isEdited;
        dto.LastNestDurationSeconds = lastNestDuration.TotalSeconds;
        dto.LastNestAt = lastNestAt;

        Directory.CreateDirectory(dir);
        WriteAtomically(path, JsonSerializer.Serialize(dto, Json));
    }

    public static void CopyWorkspace(string sourcePath, string destinationPath)
    {
        string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        string destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath))!;
        var dto = JsonSerializer.Deserialize<ProjectDto>(File.ReadAllText(sourcePath), Json)
                  ?? throw new InvalidDataException("File nhóm rỗng.");
        if (dto.Kind == ProjectKind)
            throw new InvalidDataException("Không thể dùng file dự án tổng làm file nhóm.");
        EnsureSupportedVersion(dto);
        foreach (var part in dto.Parts.Where(part => part.Path != null))
        {
            string full = Path.GetFullPath(Path.Combine(sourceDirectory, part.Path!));
            part.Path = Path.GetRelativePath(destinationDirectory, full);
        }
        Directory.CreateDirectory(destinationDirectory);
        WriteAtomically(destinationPath, JsonSerializer.Serialize(dto, Json));
    }

    /// <summary>Loads current group files and legacy single-workspace project files.</summary>
    public static WorkspaceSnapshot LoadWorkspace(string path, NestSettings settings, List<string> problems)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var dto = JsonSerializer.Deserialize<ProjectDto>(File.ReadAllText(path), Json)
                  ?? throw new InvalidDataException("File dự án rỗng.");
        if (dto.Kind == ProjectKind)
            throw new InvalidDataException("Đây là file dự án tổng; hãy mở dự án tổng để chọn nhóm.");
        EnsureSupportedVersion(dto);
        if (dto.Settings != null) settings.FromJson(dto.Settings);

        var items = new List<PartItem>();
        foreach (var part in dto.Parts)
        {
            if (part.Kind == nameof(PartKind.Rect))
            {
                var rect = PartItem.NewRect(part.Name, part.Width, part.Height, part.Quantity, part.CanRotate);
                rect.Flip = part.Flip;
                rect.RestoreMaterialThickness(part.Material, part.Thickness);
                items.Add(rect);
                continue;
            }
            if (part.Kind == nameof(PartKind.Template))
            {
                if (!Enum.TryParse<StandardPartShape>(part.TemplateShape, out var shape) || !Enum.IsDefined(shape))
                    throw new InvalidDataException($"Loại hình mẫu '{part.TemplateShape}' không hợp lệ.");
                double size = part.TemplateSize > 0 ? part.TemplateSize : part.Width;
                double height = shape is StandardPartShape.Circle or StandardPartShape.Polygon ? size : part.Height;
                var template = PartItem.NewTemplate(part.Name, shape, size, height, part.TemplateSides, part.Quantity, part.CanRotate);
                template.Flip = part.Flip;
                template.RestoreMaterialThickness(part.Material, part.Thickness);
                items.Add(template);
                continue;
            }

            string full = Path.GetFullPath(Path.Combine(dir, part.Path ?? ""));
            if (!File.Exists(full)) { problems.Add($"Không tìm thấy file: {full}"); continue; }
            try
            {
                var item = PartItem.FromDxf(full, part.Layers is { Count: > 0 } ? part.Layers : null);
                item.Name = part.Name;
                item.Quantity = part.Quantity;
                item.CanRotate = part.CanRotate;
                item.RestoreMaterialThickness(part.Material, part.Thickness);
                item.Flip = part.Flip;
                items.Add(item);
            }
            catch (Exception ex) { problems.Add($"{Path.GetFileName(full)}: {ex.Message}"); }
        }

        return new WorkspaceSnapshot
        {
            Parts = items,
            Result = dto.Result == null ? null : FromDto(dto.Result),
            ResultSheet = dto.ResultSheet?.ToSheet(),
            IsStale = dto.IsStale,
            IsEdited = dto.IsEdited,
            LastNestDuration = TimeSpan.FromSeconds(Math.Max(0, dto.LastNestDurationSeconds)),
            LastNestAt = dto.LastNestAt
        };
    }

    private static void EnsureSupportedVersion(ProjectDto dto)
    {
        if (dto.Version < 1 || dto.Version > CurrentFormatVersion)
            throw new InvalidDataException(
                $"Phiên bản dữ liệu dự án {dto.Version} không được hỗ trợ (hiện hỗ trợ 1–{CurrentFormatVersion}).");
    }

    private static void WriteAtomically(string path, string contents)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = fullPath + ".tmp";
        File.WriteAllText(temporaryPath, contents);
        File.Move(temporaryPath, fullPath, true);
    }

    /// <summary>Loads a workspace. Missing or invalid DXFs are reported without losing remaining parts.</summary>
    public static List<PartItem> Load(string path, NestSettings settings, List<string> problems) =>
        LoadWorkspace(path, settings, problems).Parts;

    private static NestResultDto ToDto(NestResult result) => new()
    {
        Strategy = result.Strategy,
        Notes = result.Notes.ToList(),
        Unplaced = result.Unplaced.ToList(),
        Sheets = result.Sheets.Select(sheet => new SheetResultDto
        {
            Index = sheet.Index, Width = sheet.Width, Height = sheet.Height,
            Material = sheet.Material, Thickness = sheet.Thickness, StockName = sheet.StockName,
            Placements = sheet.Placements.Select(placement => new PlacementDto
            {
                PartName = placement.PartName, Instance = placement.Instance,
                X = placement.X, Y = placement.Y, Width = placement.Width, Height = placement.Height,
                Rotated = placement.Rotated, AngleDeg = placement.AngleDeg, Area = placement.Area,
                Shape = placement.Shape == null ? null : ToDto(placement.Shape)
            }).ToList()
        }).ToList()
    };

    private static ShapeDto ToDto(DxfShape shape) => new()
    {
        Name = shape.Name,
        UnitScale = shape.UnitScale,
        Layers = shape.Layers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
        Warnings = shape.Warnings.ToList(),
        ApproximatedEntities = shape.ApproximatedEntities,
        LayerColors = shape.LayerColors.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
        Texts = shape.Texts.ToList(),
        Segments = shape.Segments.Select(segment => segment switch
        {
            LineSeg line => new SegmentDto { Kind = "Line", Layer = line.Layer, X1 = line.X1, Y1 = line.Y1, X2 = line.X2, Y2 = line.Y2 },
            CircleSeg circle => new SegmentDto { Kind = "Circle", Layer = circle.Layer, Cx = circle.Cx, Cy = circle.Cy, Radius = circle.R },
            ArcSeg arc => new SegmentDto { Kind = "Arc", Layer = arc.Layer, Cx = arc.Cx, Cy = arc.Cy, Radius = arc.R, StartRad = arc.StartRad, SweepRad = arc.SweepRad },
            _ => throw new InvalidDataException($"Không thể lưu segment {segment.GetType().Name}.")
        }).ToList()
    };

    private static NestResult FromDto(NestResultDto dto)
    {
        var result = new NestResult { Strategy = dto.Strategy };
        result.Notes.AddRange(dto.Notes);
        result.Unplaced.AddRange(dto.Unplaced);
        foreach (var savedSheet in dto.Sheets)
        {
            var sheet = new SheetResult
            {
                Index = savedSheet.Index, Width = savedSheet.Width, Height = savedSheet.Height,
                Material = savedSheet.Material, Thickness = savedSheet.Thickness, StockName = savedSheet.StockName
            };
            foreach (var placement in savedSheet.Placements)
            {
                sheet.Placements.Add(new Placement(placement.PartName, placement.Instance,
                    placement.X, placement.Y, placement.Width, placement.Height, placement.Rotated,
                    placement.Shape == null ? null : FromDto(placement.Shape), placement.AngleDeg, placement.Area));
            }
            result.Sheets.Add(sheet);
        }
        return result;
    }

    private static DxfShape FromDto(ShapeDto dto)
    {
        var segments = dto.Segments.Select(segment => segment.Kind switch
        {
            "Line" => (Seg)new LineSeg(segment.X1, segment.Y1, segment.X2, segment.Y2, segment.Layer),
            "Circle" => new CircleSeg(segment.Cx, segment.Cy, segment.Radius, segment.Layer),
            "Arc" => new ArcSeg(segment.Cx, segment.Cy, segment.Radius, segment.StartRad, segment.SweepRad, segment.Layer),
            _ => throw new InvalidDataException($"Loại segment không hợp lệ: {segment.Kind}.")
        }).ToList();
        return new DxfShape(dto.Name, segments, dto.UnitScale, dto.Layers, dto.Warnings,
            dto.ApproximatedEntities, dto.LayerColors, dto.Texts);
    }

    public sealed class NestResultDto
    {
        public string Strategy { get; set; } = "";
        public List<string> Notes { get; set; } = new();
        public List<string> Unplaced { get; set; } = new();
        public List<SheetResultDto> Sheets { get; set; } = new();
    }

    public sealed class SheetDefinitionDto
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double Margin { get; set; }
        public double Spacing { get; set; }
        public SheetDefinition ToSheet() => new(Width, Height, Margin, Spacing);
    }

    public sealed class SheetResultDto
    {
        public int Index { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Material { get; set; } = "";
        public double Thickness { get; set; }
        public string StockName { get; set; } = "";
        public List<PlacementDto> Placements { get; set; } = new();
    }

    public sealed class PlacementDto
    {
        public string PartName { get; set; } = "";
        public int Instance { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Rotated { get; set; }
        public double AngleDeg { get; set; }
        public double Area { get; set; }
        public ShapeDto? Shape { get; set; }
    }

    public sealed class ShapeDto
    {
        public string Name { get; set; } = "";
        public double UnitScale { get; set; }
        public Dictionary<string, int> Layers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Warnings { get; set; } = new();
        public int ApproximatedEntities { get; set; }
        public Dictionary<string, DxfLayerColor> LayerColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<DxfTextEntity> Texts { get; set; } = new();
        public List<SegmentDto> Segments { get; set; } = new();
    }

    public sealed class SegmentDto
    {
        public string Kind { get; set; } = "";
        public string Layer { get; set; } = "0";
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Cx { get; set; }
        public double Cy { get; set; }
        public double Radius { get; set; }
        public double StartRad { get; set; }
        public double SweepRad { get; set; }
    }
}
