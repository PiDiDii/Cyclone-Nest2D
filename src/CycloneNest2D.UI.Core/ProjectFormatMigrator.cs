namespace CycloneNest2D.UI;

internal static class ProjectFormatMigrator
{
    public static ProjectStore.ProjectDto Upgrade(ProjectStore.ProjectDto project)
    {
        if (project.Version < 1 || project.Version > ProjectStore.CurrentFormatVersion)
            throw new InvalidDataException(
                $"Phiên bản dữ liệu dự án {project.Version} không được hỗ trợ (hiện hỗ trợ 1–{ProjectStore.CurrentFormatVersion}).");

        if (project.Kind == ProjectStore.ProjectKind)
        {
            if (project.Groups == null)
                throw new InvalidDataException("File chỉ mục dự án thiếu danh sách nhóm.");
            if (project.Groups.Any(group => group == null))
                throw new InvalidDataException("Danh sách nhóm trong file dự án chứa mục rỗng.");
        }
        else if (project.Kind is null or ProjectStore.GroupKind)
        {
            if (project.Parts == null)
                throw new InvalidDataException("File nhóm thiếu danh sách chi tiết.");
            if (project.Parts.Any(part => part == null))
                throw new InvalidDataException("Danh sách chi tiết trong file nhóm chứa mục rỗng.");
        }
        else
        {
            throw new InvalidDataException($"Loại dữ liệu dự án '{project.Kind}' không được hỗ trợ.");
        }

        while (project.Version < ProjectStore.CurrentFormatVersion)
        {
            switch (project.Version)
            {
                case 1:
                    // Version 1 files are legacy single-workspace files without a Kind field.
                    project.Version = 2;
                    break;
                case 2:
                    // Version 3 added template-specific metadata. Missing fields use the DTO defaults.
                    if (project.Kind != ProjectStore.ProjectKind)
                    {
                        foreach (var part in project.Parts)
                        {
                            if (part.Kind == nameof(PartKind.Template) && part.TemplateSize <= 0)
                                part.TemplateSize = part.Width;
                        }
                    }
                    project.Version = 3;
                    break;
                default:
                    throw new InvalidDataException($"Không có migration cho phiên bản dữ liệu dự án {project.Version}.");
            }
        }

        return project;
    }
}
