namespace CycloneNest2D.UI;

/// <summary>
/// Encapsulates project-level orchestration used by the main view model.
/// This keeps MainViewModel focused on state and command wiring.
/// </summary>
public sealed class ProjectService
{
    public ProjectGroupItem CreateProjectGroup(string? name = null, string? path = null)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? "Nhóm vật liệu 1" : name.Trim();
        return new ProjectGroupItem(Guid.NewGuid().ToString("N"), normalized, path ?? string.Empty);
    }

    public string NormalizeProjectGroupName(string? name, string fallback = "Nhóm vật liệu 1")
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        return name.Trim();
    }
}
