using CycloneNest2D.Core;

namespace CycloneNest2D.UI;

/// <summary>
/// Encapsulates project open/save and project-group lifecycle operations.
/// </summary>
public sealed class ProjectLifecycleService
{
    public ProjectGroupItem CreateLegacyGroup(string projectPath, IReadOnlyList<PartItem> parts, NestResult? result)
    {
        var name = GroupNameFor(parts, 1);
        return new ProjectGroupItem(Guid.NewGuid().ToString("N"), name, projectPath)
        {
            PartCount = parts.Count,
            SheetCount = result?.Sheets.Count ?? 0
        };
    }

    public ProjectGroupItem CreateEmptyGroup(string? projectPath, string name)
    {
        string groupName = (name ?? "").Trim();
        if (groupName.Length == 0) groupName = "Nhóm vật liệu 1";
        string path = string.IsNullOrWhiteSpace(projectPath)
            ? Path.Combine(Environment.CurrentDirectory, "new-project" + ProjectStore.Extension)
            : projectPath;
        return new ProjectGroupItem(Guid.NewGuid().ToString("N"), groupName,
            BuildChildProjectPath(path, Guid.NewGuid().ToString("N")));
    }

    public void SyncGroupPathsForProject(string? currentProjectPath, string newProjectPath, IList<ProjectGroupItem> groups)
    {
        bool sameProject = IsSameProjectPath(currentProjectPath, newProjectPath);
        string dataDirectory = ProjectStore.DataDirectory(newProjectPath);
        foreach (var group in groups)
        {
            string targetChild = BuildChildProjectPath(newProjectPath, group.Id);
            bool needsChildPath = string.IsNullOrWhiteSpace(group.Path) ||
                IsSameProjectPath(group.Path, newProjectPath);
            if (!needsChildPath && !sameProject && File.Exists(group.Path))
            {
                if (!IsSameProjectPath(group.Path, targetChild))
                    ProjectStore.CopyWorkspace(group.Path, targetChild);
                group.Path = targetChild;
            }
            else if (needsChildPath)
            {
                group.Path = targetChild;
            }
        }
    }

    public bool IsSameProjectPath(string? currentPath, string? candidatePath)
    {
        if (string.IsNullOrWhiteSpace(currentPath) || string.IsNullOrWhiteSpace(candidatePath)) return false;
        return string.Equals(Path.GetFullPath(currentPath), Path.GetFullPath(candidatePath), StringComparison.OrdinalIgnoreCase);
    }

    public bool IsGroupNameTaken(IReadOnlyCollection<ProjectGroupItem> groups, string groupName, ProjectGroupItem? except = null)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return true;
        return groups.Any(group => !ReferenceEquals(group, except) &&
            string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase));
    }

    public string BuildUniqueGroupName(IReadOnlyCollection<ProjectGroupItem> groups, string baseName, ProjectGroupItem? except = null)
    {
        string candidate = string.IsNullOrWhiteSpace(baseName) ? "Nhóm vật liệu" : baseName.Trim();
        int suffix = 2;
        while (IsGroupNameTaken(groups, candidate, except))
            candidate = $"{baseName.Trim()} {suffix++}";
        return candidate;
    }

    public string BuildDuplicateGroupName(IReadOnlyCollection<ProjectGroupItem> groups, string sourceName)
    {
        string baseName = string.IsNullOrWhiteSpace(sourceName) ? "Nhóm vật liệu" : sourceName.Trim() + " - bản sao";
        return BuildUniqueGroupName(groups, baseName);
    }

    public ProjectGroupItem CreateGroupForProject(string? projectPath, string name, string? existingId = null)
    {
        string groupName = string.IsNullOrWhiteSpace(name) ? "Nhóm vật liệu 1" : name.Trim();
        string id = existingId ?? Guid.NewGuid().ToString("N");
        return new ProjectGroupItem(id, groupName, BuildChildProjectPath(projectPath ?? string.Empty, id));
    }

    public IReadOnlyList<ProjectStore.GroupReferenceDto> BuildProjectReferences(IReadOnlyCollection<ProjectGroupItem> groups)
        => groups.Select(group => new ProjectStore.GroupReferenceDto
        {
            Id = group.Id,
            Name = group.Name,
            Path = group.Path,
            PartCount = group.PartCount,
            SheetCount = group.SheetCount
        }).ToList();

    public List<ProjectGroupItem> BuildProjectGroupsFromIndex(string projectPath, List<string> problems)
    {
        var references = ProjectStore.LoadProjectIndex(projectPath);
        var groups = new List<ProjectGroupItem>();
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        foreach (var reference in references)
        {
            string childPath = Path.GetFullPath(Path.Combine(projectDirectory, reference.Path));
            var group = new ProjectGroupItem(reference.Id, reference.Name, childPath)
            {
                PartCount = reference.PartCount,
                SheetCount = reference.SheetCount
            };
            if (!File.Exists(childPath))
                problems.Add($"Nhóm '{reference.Name}' không tìm thấy file con: {childPath}");
            groups.Add(group);
        }
        return groups;
    }

    public void SaveProjectIndex(string projectPath, IReadOnlyCollection<ProjectGroupItem> groups)
        => ProjectStore.SaveProjectIndex(projectPath, BuildProjectReferences(groups).ToList());

    public void SaveEmptyGroupWorkspace(ProjectGroupItem group, NestSettings settings)
    {
        var groupSettings = new NestSettings();
        groupSettings.FromJson(settings.ToJson());
        ProjectStore.SaveWorkspace(group.Path, groupSettings, Array.Empty<PartItem>(), null, null,
            false, false, TimeSpan.Zero, null);
    }

    public ProjectGroupItem DuplicateGroup(string projectPath, ProjectGroupItem source, string name)
    {
        string newPath = Path.Combine(ProjectStore.DataDirectory(projectPath), Guid.NewGuid().ToString("N") + ProjectStore.Extension);
        ProjectStore.CopyWorkspace(source.Path, newPath);
        var duplicate = CreateGroupForProject(projectPath, name, Guid.NewGuid().ToString("N"));
        duplicate.Path = newPath;
        duplicate.PartCount = source.PartCount;
        duplicate.SheetCount = source.SheetCount;
        return duplicate;
    }

    public void LinkAllMatchingParts(IEnumerable<PartItem> parts, IEnumerable<SheetPreset> presets)
    {
        foreach (var part in parts.Where(p => p.AssignedPreset == null && !string.IsNullOrWhiteSpace(p.Material) && p.Thickness > 0))
        {
            var match = presets.FirstOrDefault(sheet =>
                string.Equals(sheet.Material, part.Material, StringComparison.OrdinalIgnoreCase) &&
                Math.Round(sheet.Thickness, 4) == Math.Round(part.Thickness, 4));
            if (match != null) part.AssignPreset(match);
        }
    }

    public void UpdateGroupSummary(ProjectGroupItem? group, int partCount, int sheetCount, Action notify)
    {
        if (group == null) return;
        group.PartCount = partCount;
        group.SheetCount = sheetCount;
        notify();
    }

    public bool TryCreateGroup(IReadOnlyCollection<ProjectGroupItem> groups, string? projectPath, string name,
        IDialogService dialogs, out ProjectGroupItem? group, out string? message)
    {
        string groupName = (name ?? "").Trim();
        if (groupName.Length == 0)
        {
            group = null;
            message = "Hãy nhập tên nhóm vật liệu.";
            dialogs.ShowMessage(message);
            return false;
        }

        string resolvedGroupName = BuildUniqueGroupName(groups, groupName);
        if (!string.Equals(groupName, resolvedGroupName, StringComparison.OrdinalIgnoreCase))
        {
            group = null;
            message = "Tên nhóm đã tồn tại trong dự án.";
            dialogs.ShowMessage(message);
            return false;
        }

        group = CreateGroupForProject(projectPath, resolvedGroupName);
        message = null;
        return true;
    }

    public bool TryRenameGroup(IReadOnlyCollection<ProjectGroupItem> groups, ProjectGroupItem? group, string name,
        IDialogService dialogs, out string? message)
    {
        string groupName = (name ?? "").Trim();
        if (group == null || groupName.Length == 0)
        {
            message = "Tên nhóm không hợp lệ.";
            return false;
        }

        if (IsGroupNameTaken(groups, groupName, group))
        {
            message = "Tên nhóm đã tồn tại trong dự án.";
            dialogs.ShowMessage(message);
            return false;
        }

        group.Name = groupName;
        message = null;
        return true;
    }

    public bool TryDuplicateGroup(IReadOnlyCollection<ProjectGroupItem> groups, string projectPath, ProjectGroupItem? source,
        out ProjectGroupItem? duplicate, out string? message)
    {
        duplicate = null;
        if (source == null || string.IsNullOrWhiteSpace(projectPath))
        {
            message = "Không có dự án hoặc nhóm nguồn để nhân bản.";
            return false;
        }

        string copyName = BuildDuplicateGroupName(groups, source.Name);
        duplicate = DuplicateGroup(projectPath, source, copyName);
        message = null;
        return true;
    }

    public bool TryRemoveGroup(IReadOnlyCollection<ProjectGroupItem> groups, ProjectGroupItem? group, IDialogService dialogs,
        out string? reason)
    {
        if (group == null || groups.Count <= 1 || !groups.Contains(group))
        {
            reason = "Nhóm không hợp lệ.";
            return false;
        }

        if (!dialogs.Confirm($"Gỡ nhóm '{group.Name}' khỏi dự án? File con trên ổ đĩa sẽ được giữ nguyên."))
        {
            reason = "Người dùng hủy bỏ.";
            return false;
        }

        reason = null;
        return true;
    }

    public void RestoreSelectedProjectGroup(ProjectGroupItem? group, Action<ProjectGroupItem?> setSelectedGroup)
    {
        setSelectedGroup(group);
    }

    public void SaveActiveWorkspace(ProjectGroupItem? group, NestSettings settings, IEnumerable<PartItem> parts, NestResult? result,
        SheetDefinition? resultSheet, bool isStale, bool isEdited, TimeSpan lastNestDuration, DateTime? lastNestAt)
    {
        if (group == null || string.IsNullOrWhiteSpace(group.Path)) return;
        ProjectStore.SaveWorkspace(group.Path, settings, parts, result, resultSheet, isStale, isEdited, lastNestDuration, lastNestAt);
    }

    public string BuildChildProjectPath(string projectPath, string groupId)
    {
        string dataDirectory = ProjectStore.DataDirectory(projectPath);
        return Path.Combine(dataDirectory, groupId + ProjectStore.Extension);
    }

    private static string GroupNameFor(IReadOnlyList<PartItem> parts, int index)
    {
        var materials = parts.Select(part => part.Material)
            .Where(material => !string.IsNullOrWhiteSpace(material))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var thicknesses = parts.Select(part => part.Thickness)
            .Where(thickness => thickness > 0)
            .Distinct()
            .ToList();

        return materials.Count == 1 && thicknesses.Count == 1
            ? $"{materials[0]} · {thicknesses[0]:0.###} mm"
            : $"Nhóm vật liệu {index}";
    }
}
