namespace CycloneNest2D.UI;

/// <summary>
/// Encapsulates the folder/file import and list management for project parts.
/// </summary>
public sealed class PartManagementService
{
    public int AddDxfFiles(IEnumerable<string> paths, ICollection<PartItem> parts, Func<string, PartItem?, string> uniqueName)
    {
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                files.AddRange(Directory.EnumerateFiles(path, "*.dxf", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            else if (File.Exists(path) && path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
                files.Add(path);
        }

        int added = 0;
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var full = Path.GetFullPath(file);
            if (parts.Any(x => x.IsDxf && string.Equals(x.SourcePath, full, StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                var item = PartItem.FromDxf(full);
                item.Name = uniqueName(item.Name, null);
                parts.Add(item);
                added++;
            }
            catch
            {
                // caller decides how to surface problems and warnings.
            }
        }

        return added;
    }

    public PartItem AddRectangle(ICollection<PartItem> parts, Func<string, PartItem?, string> uniqueName)
    {
        var item = PartItem.NewRect(uniqueName("Chu_nhat", null), 200, 100);
        parts.Add(item);
        return item;
    }

    public int RemoveParts(ICollection<PartItem> parts, IEnumerable<PartItem> removeThese)
    {
        var removing = removeThese.Where(p => parts.Contains(p)).Distinct().ToArray();
        foreach (var part in removing) parts.Remove(part);
        return removing.Length;
    }

    public void ClearParts(ICollection<PartItem> parts)
    {
        parts.Clear();
    }

    public string UniqueName(IEnumerable<PartItem> parts, string name, PartItem? except)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "Chi_tiet";
        string candidate = name;
        for (int index = 2; parts.Any(p => p != except && string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)); index++)
            candidate = $"{name}_{index}";
        return candidate;
    }
}
