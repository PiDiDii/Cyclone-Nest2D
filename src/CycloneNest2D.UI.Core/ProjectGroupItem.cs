namespace CycloneNest2D.UI;

/// <summary>One independently saved material/thickness workspace referenced by a project.</summary>
public sealed class ProjectGroupItem : ObservableObject
{
    private string _name;
    private string _path;
    private int _partCount;
    private int _sheetCount;

    public ProjectGroupItem(string id, string name, string path)
    {
        Id = id;
        _name = name;
        _path = path;
    }

    public string Id { get; }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Path { get => _path; set => Set(ref _path, value); }
    public int PartCount { get => _partCount; set { if (Set(ref _partCount, value)) OnPropertyChanged(nameof(Summary)); } }
    public int SheetCount { get => _sheetCount; set { if (Set(ref _sheetCount, value)) OnPropertyChanged(nameof(Summary)); } }
    public string Summary => $"{PartCount} chi tiết · {SheetCount} tấm";
    public override string ToString() => $"{Name}  |  {Summary}";
}
