using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using CycloneNest2D.Core;

namespace CycloneNest2D.UI;

public sealed class SheetItem
{
    public SheetItem(SheetResult sheet) => Sheet = sheet;
    public SheetResult Sheet { get; }
    public string Material => Sheet.Material;
    public double Thickness => Sheet.Thickness;
    public string Text => $"Tấm {Sheet.Index} · {Material} · {Thickness:0.##} mm · {Sheet.Width:0.##} × {Sheet.Height:0.##} mm · {Sheet.Placements.Count} chi tiết · {Sheet.Utilization:P1}";
    public override string ToString() => Text;
}

/// <summary>Một nhóm chi tiết cùng vật liệu và độ dày cho sidebar Nesting.</summary>
public sealed class PartMaterialGroup
{
    public PartMaterialGroup(string material, double thickness, IReadOnlyList<PartItem> parts)
    {
        Material = material;
        Thickness = thickness;
        Parts = parts;
    }

    public string Material { get; }
    public double Thickness { get; }
    public string Label => $"{Material} · {Thickness:0.###} mm";
    public IReadOnlyList<PartItem> Parts { get; }
    public int UniqueCount => Parts.Count;
    public int TotalQuantity => Parts.Sum(p => p.RemainingQuantity);
}

/// <summary>Toàn bộ logic của cửa sổ chính; lớp WPF chỉ binding và vẽ.</summary>
public sealed class MainViewModel : ObservableObject
{
    private sealed class ViewModelProgress<T> : IProgress<T>
    {
        private readonly SynchronizationContext? _context = SynchronizationContext.Current;
        private readonly Action<T> _report;

        public ViewModelProgress(Action<T> report) => _report = report;

        public void Report(T value)
        {
            if (_context == null)
            {
                _report(value);
                return;
            }
            _context.Post(static state =>
            {
                var (progress, item) = ((ViewModelProgress<T>, T))state!;
                progress._report(item);
            }, (this, value));
        }
    }

    private readonly IDialogService _dialogs;
    private readonly ProjectService _projectService = new();
    private readonly ProjectLifecycleService _projectLifecycleService = new();
    private readonly NestingOrchestrator _nestingOrchestrator = new();
    private readonly ManualPlacementService _manualPlacementService = new();
    private readonly PartManagementService _partManagementService = new();
    private readonly SceneBuilder _sceneBuilder = new();
    private readonly ExportCoordinator _exportCoordinator = new();
    private readonly object _progressSync = new();
    private CancellationTokenSource? _cts;

    private PartItem? _selectedPart;
    private NestResult? _result;
    private SheetDefinition? _resultSheet;
    private SheetItem? _selectedSheet;
    private PreviewScene _scene;
    private int _selectedPlacement = -1;
    private readonly HashSet<int> _selectedPlacementIndices = new();
    private bool _syncingPlacementSelection;
    private bool _isBusy;
    private bool _isStale;
    private bool _isEdited;
    private bool _isProjectDirty;
    private string _status = "Sẵn sàng. Kéo file DXF vào cửa sổ hoặc bấm Thêm DXF.";
    private string _progress = "";
    private double _progressPercent;
    private string _summary = "";
    private string? _projectPath;
    private TimeSpan _lastNestDuration;
    private TimeSpan _elapsedNestTime;
    private TimeSpan? _estimatedRemainingTime;
    private DateTime? _lastNestAt;
    private SheetPreset? _preset;
    private string _selectedInfo = "";
    private ProjectGroupItem? _selectedProjectGroup;
    private ProjectGroupItem? _activeProjectGroup;
    private string _newProjectGroupName = "";
    private bool _syncingProjectGroups;

    public MainViewModel(IDialogService dialogs, NestSettings? settings = null)
    {
        _dialogs = dialogs;
        Settings = settings ?? new NestSettings();
        _preset = Settings.SheetPresets.FirstOrDefault(p => Math.Abs(p.Width - Settings.SheetWidth) < 1e-6 && Math.Abs(p.Height - Settings.SheetHeight) < 1e-6);
        _scene = PreviewScene.Empty(Settings.ToSheet());
        var initialGroup = new ProjectGroupItem(Guid.NewGuid().ToString("N"), "Nhóm vật liệu 1", "");
        ProjectGroups.Add(initialGroup);
        _activeProjectGroup = initialGroup;
        _selectedProjectGroup = initialGroup;
        Settings.Changed += () =>
        {
            IsProjectDirty = true;
            if (_result != null) MarkStale();
        };
        Settings.SheetPresets.CollectionChanged += (_, _) => IsProjectDirty = true;
        Parts.CollectionChanged += OnPartsChanged;
        ProjectGroups.CollectionChanged += (_, _) => NotifyProjectSummary();

        AddDxfCommand = new RelayCommand(AddDxfDialog, () => !IsBusy);
        AddFolderCommand = new RelayCommand(AddFolderDialog, () => !IsBusy);
        AddRectCommand = new RelayCommand(AddRect, () => !IsBusy);
        RemoveCommand = new RelayCommand(RemoveSelected, () => !IsBusy && SelectedPart != null);
        ClearCommand = new RelayCommand(ClearParts, () => !IsBusy && Parts.Count > 0);
        RunCommand = new AsyncRelayCommand(RunAsync, () => Parts.Count > 0, ex => Fail("Lỗi khi nesting: " + ex.Message));
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        ExportDxfCommand = new RelayCommand(ExportDxf, () => !IsBusy && Result != null);
        RotateSelectedCommand = new RelayCommand(RotateSelected, () => !IsBusy && SelectedPlacement >= 0);
        DeletePlacementCommand = new RelayCommand(() => DeleteSelectedPlacements(), () => !IsBusy && _selectedPlacementIndices.Count > 0);
        DeleteSelectedSheetCommand = new RelayCommand(() => DeleteSelectedSheet(), () => !IsBusy && SelectedSheet != null);
        ClearNestingCommand = new RelayCommand(ClearNesting, () => !IsBusy && Result != null);
        AddManualSheetCommand = new RelayCommand(() => AddManualSheet(), () => !IsBusy);
        NewProjectCommand = new RelayCommand(NewProject, () => !IsBusy);
        OpenProjectCommand = new RelayCommand(OpenProjectDialog, () => !IsBusy);
        SaveProjectCommand = new RelayCommand(() => SaveProject(false), () => !IsBusy);
        SaveProjectAsCommand = new RelayCommand(() => SaveProject(true), () => !IsBusy);
        CreateProjectGroupCommand = new RelayCommand(() => CreateProjectGroup(NewProjectGroupName), () => !IsBusy);
        RenameProjectGroupCommand = new RelayCommand(() => RenameProjectGroup(SelectedProjectGroup, NewProjectGroupName),
            () => !IsBusy && SelectedProjectGroup != null);
        DuplicateProjectGroupCommand = new RelayCommand(() => DuplicateProjectGroup(), () => !IsBusy && SelectedProjectGroup != null);
        RemoveProjectGroupCommand = new RelayCommand(() => RemoveProjectGroup(SelectedProjectGroup),
            () => !IsBusy && SelectedProjectGroup != null && ProjectGroups.Count > 1);
        ResetViewCommand = new RelayCommand(() => ResetViewRequested?.Invoke());
    }

    // ---------- trạng thái ----------

    public NestSettings Settings { get; }
    public ObservableCollection<PartItem> Parts { get; } = new();
    public ObservableCollection<SheetItem> Sheets { get; } = new();
    public ObservableCollection<ProjectGroupItem> ProjectGroups { get; } = new();
    public ObservableCollection<string> Messages { get; } = new();
    /// <summary>Thông báo nhanh góc phải dưới cửa sổ.</summary>
    public ToastService Toasts { get; } = new();
    public ObservableCollection<SheetPreset> Presets => Settings.SheetPresets;
    public IReadOnlyList<PartMaterialGroup> PartGroups => Parts
        .GroupBy(p => (Material: MaterialKey(p.Material), p.Thickness))
        .OrderBy(g => g.First().Material, StringComparer.OrdinalIgnoreCase)
        .ThenBy(g => g.Key.Thickness)
        .Select(g => new PartMaterialGroup(
            string.IsNullOrWhiteSpace(g.First().Material) ? "Chưa khai báo vật liệu" : g.First().Material,
            g.Key.Thickness,
            g.ToList()))
        .ToList();
    public int RequiredCount => Parts.Sum(p => Math.Max(0, p.Quantity));
    public int PlacedCount => Result?.Sheets.Sum(s => s.Placements.Count) ?? 0;
    public int RemainingCount => Math.Max(0, RequiredCount - PlacedCount);
    public int ProjectGroupCount => ProjectGroups.Count;
    public int ProjectPartCount => ProjectGroups.Sum(group => group.PartCount);
    public int ProjectSheetCount => ProjectGroups.Sum(group => group.SheetCount);

    /// <summary>View lắng nghe để đưa khung nhìn về vừa khít tấm.</summary>
    public event Action? ResetViewRequested;

    public PartItem? SelectedPart
    {
        get => _selectedPart;
        set { if (Set(ref _selectedPart, value)) RaiseCommands(); }
    }

    public SheetPreset? SelectedPreset
    {
        get => _preset;
        set
        {
            if (Set(ref _preset, value) && value != null) Settings.ApplyPreset(value);
        }
    }

    public void AddSheetPreset(SheetPreset preset)
    {
        if (Settings.SheetPresets.Any(p => SamePresetProfile(p, preset)))
        {
            _dialogs.ShowMessage("Khổ tấm có cùng vật liệu, độ dày và kích thước đã tồn tại.");
            return;
        }
        Settings.SheetPresets.Add(preset);
        SelectedPreset = preset;
        LinkMatchingParts(preset);
        OnPropertyChanged(nameof(Presets));
        if (_result != null) MarkStale();
        OnPropertyChanged(nameof(Presets));
    }

    public void EditSheetPreset(SheetPreset oldPreset, SheetPreset newPreset)
    {
        int index = Settings.SheetPresets.IndexOf(oldPreset);
        if (index < 0) return;
        if (Settings.SheetPresets.Where((_, i) => i != index).Any(p => SamePresetProfile(p, newPreset)))
        {
            _dialogs.ShowMessage("Khổ tấm có cùng vật liệu, độ dày và kích thước đã tồn tại.");
            return;
        }
        bool wasSelected = Equals(SelectedPreset, oldPreset);
        Settings.SheetPresets[index] = newPreset;
        foreach (var part in Parts.Where(p => Equals(p.AssignedPreset, oldPreset))) part.AssignPreset(newPreset);
        LinkMatchingParts(newPreset);
        if (wasSelected) SelectedPreset = newPreset;
        if (_result != null) MarkStale();
    }

    public void DeleteSheetPreset(SheetPreset preset)
    {
        bool wasSelected = Equals(SelectedPreset, preset);
        if (!Settings.SheetPresets.Remove(preset)) return;
        foreach (var part in Parts.Where(p => Equals(p.AssignedPreset, preset))) part.AssignedPreset = null;
        if (wasSelected) SelectedPreset = Settings.SheetPresets.FirstOrDefault();
        if (_result != null) MarkStale();
        OnPropertyChanged(nameof(Presets));
    }

    public void UseSheetPreset(SheetPreset preset) => SelectedPreset = preset;

    private static bool SamePresetProfile(SheetPreset a, SheetPreset b) =>
        MaterialKey(a.Material) == MaterialKey(b.Material) &&
        Math.Round(a.Thickness, 4) == Math.Round(b.Thickness, 4) &&
        Math.Round(a.Width, 3) == Math.Round(b.Width, 3) &&
        Math.Round(a.Height, 3) == Math.Round(b.Height, 3);

    private void LinkMatchingParts(SheetPreset preset)
    {
        foreach (var part in Parts.Where(p => p.AssignedPreset == null &&
            MaterialKey(p.Material) == MaterialKey(preset.Material) && Math.Round(p.Thickness, 4) == Math.Round(preset.Thickness, 4)))
            part.AssignPreset(preset);
    }

    private void LinkAllMatchingParts() => _projectLifecycleService.LinkAllMatchingParts(Parts, Settings.SheetPresets);

    public int AssignPresetToParts(IEnumerable<PartItem> parts, SheetPreset preset)
    {
        int count = 0;
        foreach (var part in parts.Distinct())
        {
            part.AssignPreset(preset);
            count++;
        }
        if (count > 0) Status = $"Đã gán {preset.Material} · {preset.Thickness:0.###} mm cho {count} chi tiết.";
        return count;
    }

    public void AssignPresetToPart(PartItem part, SheetPreset preset) => part.AssignPreset(preset);

    public NestResult? Result
    {
        get => _result;
        private set
        {
            if (!Set(ref _result, value)) return;
            OnPropertyChanged(nameof(PlacedCount));
            OnPropertyChanged(nameof(RemainingCount));
            UpdateRemainingQuantities();
            OnPropertyChanged(nameof(PartGroups));
            UpdateActiveGroupSummary();
            RaiseCommands();
        }
    }

    public SheetItem? SelectedSheet
    {
        get => _selectedSheet;
        set
        {
            if (Set(ref _selectedSheet, value))
            {
                _selectedPlacementIndices.Clear();
                OnPropertyChanged(nameof(SelectedPlacementIndices));
                SelectedPlacement = -1;
                RebuildScene();
                ResetViewRequested?.Invoke();
                RaiseCommands();
            }
        }
    }

    public PreviewScene Scene { get => _scene; private set => Set(ref _scene, value); }

    public int SelectedPlacement
    {
        get => _selectedPlacement;
        set
        {
            if (!Set(ref _selectedPlacement, value)) return;
            if (!_syncingPlacementSelection)
            {
                _selectedPlacementIndices.Clear();
                if (value >= 0) _selectedPlacementIndices.Add(value);
            }
            SelectedInfo = DescribeSelected();
            RaiseCommands();
        }
    }

    public IReadOnlyCollection<int> SelectedPlacementIndices => _selectedPlacementIndices;

    public void RefreshPlacementSelection()
    {
        int count = CurrentSheet?.Placements.Count ?? 0;
        SetSelectedPlacements(_selectedPlacementIndices.Where(i => i < count), SelectedPlacement);
    }

    public void SetSelectedPlacements(IEnumerable<int> indices, int primaryIndex = -1)
    {
        int count = CurrentSheet?.Placements.Count ?? 0;
        var valid = indices.Where(i => i >= 0 && i < count).Distinct().ToHashSet();
        if (primaryIndex < 0 || !valid.Contains(primaryIndex)) primaryIndex = valid.Order().FirstOrDefault(-1);
        _selectedPlacementIndices.Clear();
        foreach (int index in valid) _selectedPlacementIndices.Add(index);
        _syncingPlacementSelection = true;
        SelectedPlacement = primaryIndex;
        _syncingPlacementSelection = false;
        OnPropertyChanged(nameof(SelectedPlacementIndices));
        RaiseCommands();
    }

    public string SelectedInfo { get => _selectedInfo; private set => Set(ref _selectedInfo, value); }
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) { OnPropertyChanged(nameof(IsIdle)); RaiseCommands(); } } }
    public bool IsIdle => !IsBusy;
    public bool IsStale { get => _isStale; private set => Set(ref _isStale, value); }
    public bool IsEdited { get => _isEdited; private set => Set(ref _isEdited, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Progress { get => _progress; private set => Set(ref _progress, value); }
    public double ProgressPercent { get => _progressPercent; private set => Set(ref _progressPercent, value); }
    public string ElapsedNestTimeText => FormatProgressTime(_elapsedNestTime);
    public string EstimatedRemainingTimeText => _estimatedRemainingTime is { } time
        ? FormatProgressTime(time)
        : "Đang ước tính...";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public TimeSpan LastNestDuration { get => _lastNestDuration; private set => Set(ref _lastNestDuration, value); }
    public DateTime? LastNestAt { get => _lastNestAt; private set => Set(ref _lastNestAt, value); }

    private static string FormatProgressTime(TimeSpan time) =>
        time.TotalMinutes >= 1
            ? $"{(int)time.TotalMinutes:00}:{time.Seconds:00}"
            : $"{time.TotalSeconds:0} giây";

    private void UpdateProgressClock(Stopwatch stopwatch)
    {
        lock (_progressSync)
        {
            _elapsedNestTime = stopwatch.Elapsed;
            OnPropertyChanged(nameof(ElapsedNestTimeText));
            UpdateEstimatedRemainingTime();
        }
    }

    private void UpdateEstimatedRemainingTime()
    {
        if (ProgressPercent <= 0)
        {
            _estimatedRemainingTime = null;
        }
        else
        {
            double remainingSeconds = _elapsedNestTime.TotalSeconds * (100 - ProgressPercent) / ProgressPercent;
            _estimatedRemainingTime = TimeSpan.FromSeconds(Math.Max(0, remainingSeconds));
        }
        OnPropertyChanged(nameof(EstimatedRemainingTimeText));
    }

    private void UpdateProgressEstimate(double completed, double inFlight, long total)
    {
        if (total <= 0) return;
        lock (_progressSync)
        {
            double estimate = Math.Clamp((completed + inFlight) / total * 100, 0, 99);
            if (estimate > ProgressPercent)
            {
                ProgressPercent = estimate;
                UpdateEstimatedRemainingTime();
            }
        }
    }

    private async Task RefreshProgressClockAsync(Stopwatch stopwatch)
    {
        while (stopwatch.IsRunning)
        {
            UpdateProgressClock(stopwatch);
            await Task.Delay(500);
        }
        UpdateProgressClock(stopwatch);
    }
    public string? ProjectPath
    {
        get => _projectPath;
        private set
        {
            if (!Set(ref _projectPath, value)) return;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(ProjectName));
        }
    }
    public bool IsProjectDirty
    {
        get => _isProjectDirty;
        private set
        {
            if (Set(ref _isProjectDirty, value))
                OnPropertyChanged(nameof(ProjectSaveState));
        }
    }
    public string ProjectSaveState => IsProjectDirty ? "Chưa lưu thay đổi" : "Đã lưu";
    public string NewProjectGroupName { get => _newProjectGroupName; set => Set(ref _newProjectGroupName, value); }
    public ProjectGroupItem? SelectedProjectGroup
    {
        get => _selectedProjectGroup;
        set
        {
            if (!Set(ref _selectedProjectGroup, value) || _syncingProjectGroups) return;
            if (value == null)
            {
                RestoreSelectedProjectGroup(_activeProjectGroup);
                return;
            }
            OnPropertyChanged(nameof(Title));
            ActivateProjectGroup(value);
            RaiseCommands();
        }
    }
    public string Title => ProjectPath == null ? "Dự án mới" : Path.GetFileNameWithoutExtension(ProjectPath);
    public string ProjectName => ProjectPath == null ? "Dự án mới" : Path.GetFileNameWithoutExtension(ProjectPath);
    public SheetDefinition? ResultSheet => _resultSheet;

    // ---------- lệnh ----------

    public RelayCommand AddDxfCommand { get; }
    public RelayCommand AddFolderCommand { get; }
    public RelayCommand AddRectCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand ClearCommand { get; }
    public AsyncRelayCommand RunCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ExportDxfCommand { get; }
    public RelayCommand RotateSelectedCommand { get; }
    public RelayCommand DeletePlacementCommand { get; }
    public RelayCommand DeleteSelectedSheetCommand { get; }
    public RelayCommand ClearNestingCommand { get; }
    public RelayCommand AddManualSheetCommand { get; }
    public RelayCommand NewProjectCommand { get; }
    public RelayCommand OpenProjectCommand { get; }
    public RelayCommand SaveProjectCommand { get; }
    public RelayCommand SaveProjectAsCommand { get; }
    public RelayCommand CreateProjectGroupCommand { get; }
    public RelayCommand RenameProjectGroupCommand { get; }
    public RelayCommand DuplicateProjectGroupCommand { get; }
    public RelayCommand RemoveProjectGroupCommand { get; }
    public RelayCommand ResetViewCommand { get; }

    private void RaiseCommands()
    {
        AddDxfCommand.RaiseCanExecuteChanged();
        AddFolderCommand.RaiseCanExecuteChanged();
        AddRectCommand.RaiseCanExecuteChanged();
        RemoveCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
        RunCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        ExportDxfCommand.RaiseCanExecuteChanged();
        RotateSelectedCommand.RaiseCanExecuteChanged();
        DeletePlacementCommand.RaiseCanExecuteChanged();
        DeleteSelectedSheetCommand.RaiseCanExecuteChanged();
        ClearNestingCommand.RaiseCanExecuteChanged();
        AddManualSheetCommand.RaiseCanExecuteChanged();
        NewProjectCommand.RaiseCanExecuteChanged();
        OpenProjectCommand.RaiseCanExecuteChanged();
        SaveProjectCommand.RaiseCanExecuteChanged();
        SaveProjectAsCommand.RaiseCanExecuteChanged();
        CreateProjectGroupCommand.RaiseCanExecuteChanged();
        RenameProjectGroupCommand.RaiseCanExecuteChanged();
        DuplicateProjectGroupCommand.RaiseCanExecuteChanged();
        RemoveProjectGroupCommand.RaiseCanExecuteChanged();
    }

    // ---------- chi tiết ----------

    private void OnPartsChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        IsProjectDirty = true;
        if (e.NewItems != null) foreach (PartItem p in e.NewItems) p.Changed += OnPartChanged;
        if (e.OldItems != null) foreach (PartItem p in e.OldItems) p.Changed -= OnPartChanged;
        for (int i = 0; i < Parts.Count; i++) Parts[i].SetSequence(i + 1);
        NotifyPartSummaries();
        UpdateActiveGroupSummary();
        if (_result != null) MarkStale();
        RaiseCommands();
    }

    private void OnPartChanged(PartItem _)
    {
        IsProjectDirty = true;
        NotifyPartSummaries();
        UpdateActiveGroupSummary();
        if (_result != null) MarkStale();
    }

    private void NotifyPartSummaries()
    {
        UpdateRemainingQuantities();
        OnPropertyChanged(nameof(PartGroups));
        OnPropertyChanged(nameof(RequiredCount));
        OnPropertyChanged(nameof(RemainingCount));
    }

    private void UpdateRemainingQuantities()
    {
        var placedByName = Result?.Sheets.SelectMany(s => s.Placements)
            .GroupBy(p => p.PartName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in Parts)
            part.SetRemainingQuantity(Math.Max(0, part.Quantity - placedByName.GetValueOrDefault(part.Name)));
    }

    private void MarkStale()
    {
        if (!IsStale) { IsStale = true; Status = "Danh sách chi tiết hoặc thông số đã đổi: kết quả bên dưới đã cũ, bấm Nesting để tính lại."; }
    }

    public void AddDxfDialog()
    {
        var files = _dialogs.PickOpenFiles("Chọn file DXF", "DXF (*.dxf)|*.dxf");
        if (files.Length > 0) AddPaths(files);
    }

    public void AddFolderDialog()
    {
        var f = _dialogs.PickFolder("Chọn thư mục chứa các file DXF");
        if (f != null) AddPaths(new[] { f });
    }

    /// <summary>Thêm file/thư mục (kéo thả hoặc hộp thoại). File không phải DXF bị bỏ qua. Trả về số chi tiết đã thêm.</summary>
    public int AddPaths(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                files.AddRange(Directory.EnumerateFiles(p, "*.dxf", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            else if (File.Exists(p) && p.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
                files.Add(p);
        }

        int added = 0;
        var problems = new List<string>();
        foreach (var f in files)
        {
            var full = Path.GetFullPath(f);
            if (Parts.Any(x => x.IsDxf && string.Equals(x.SourcePath, full, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"{Path.GetFileName(f)}: đã có trong danh sách.");
                continue;
            }
            try
            {
                var item = PartItem.FromDxf(full);
                item.Name = UniqueName(item.Name, null);
                Parts.Add(item);
                added++;
                foreach (var w in item.Warnings) problems.Add($"{item.Name}: {w}");
            }
            catch (Exception ex) { problems.Add($"{Path.GetFileName(f)}: KHÔNG ĐỌC ĐƯỢC - {ex.Message}"); }
        }

        if (files.Count == 0) problems.Add("Không có file DXF nào trong mục đã chọn.");
        PostMessages(problems);
        Status = $"Đã thêm {added} chi tiết" + (problems.Count > 0 ? $", {problems.Count} thông báo (xem khung Thông báo)." : ".");
        if (problems.Count > 0) Toasts.Show(ToastKind.Warning, $"Thêm {added} chi tiết, {problems.Count} vấn đề", problems[0]);
        else if (added > 0) Toasts.Show(ToastKind.Success, $"Đã thêm {added} chi tiết");
        if (SelectedPart == null && Parts.Count > 0) SelectedPart = Parts[^1];
        return added;
    }

    public void AddRect()
    {
        var item = _partManagementService.AddRectangle(Parts, UniqueName);
        SelectedPart = item;
        Status = "Đã thêm hình chữ nhật 200 x 100: sửa Rộng/Cao/Số lượng ngay trong bảng.";
    }

    public PartItem AddTemplate(StandardPartShape shape, double width, double height, int sides = 6)
    {
        string prefix = shape switch
        {
            StandardPartShape.Circle => "Hinh_tron",
            StandardPartShape.Triangle => "Tam_giac",
            StandardPartShape.Polygon => $"Da_giac_{sides}_canh",
            _ => "Chu_nhat"
        };
        var item = PartItem.NewTemplate(UniqueName(prefix, null), shape, width, height, sides);
        Parts.Add(item);
        SelectedPart = item;
        Status = $"Đã thêm mẫu {item.KindText.ToLowerInvariant()} {item.Width:0.##} × {item.Height:0.##} mm.";
        return item;
    }

    public void RemoveSelected()
    {
        if (SelectedPart == null) return;
        int i = Parts.IndexOf(SelectedPart);
        _partManagementService.RemoveParts(Parts, new[] { SelectedPart });
        SelectedPart = Parts.Count == 0 ? null : Parts[Math.Min(i, Parts.Count - 1)];
    }

    public int RemoveParts(IEnumerable<PartItem> parts)
    {
        var removing = _partManagementService.RemoveParts(Parts, parts);
        if (removing == 0) return 0;
        if (SelectedPart != null && parts.Contains(SelectedPart)) SelectedPart = null;
        Status = $"Đã xóa {removing} dòng Part.";
        return removing;
    }

    public void ClearParts()
    {
        if (Parts.Count > 0 && !_dialogs.Confirm("Xoá toàn bộ danh sách chi tiết?")) return;
        _partManagementService.ClearParts(Parts);
        SelectedPart = null;
    }

    /// <summary>Tên chưa trùng (so không phân biệt hoa thường); except = dòng đang đổi tên.</summary>
    public string UniqueName(string name, PartItem? except) =>
        _partManagementService.UniqueName(Parts, name, except);

    private void PostMessages(IEnumerable<string> msgs)
    {
        foreach (var m in msgs) Messages.Add(m);
    }

    // ---------- nesting ----------

    public async Task RunAsync()
    {
        if (IsBusy) return;
        var bad = Settings.Validate();
        if (bad != null) { _dialogs.ShowMessage(bad); return; }

        var skipped = Parts.Where(p => !p.IsValid).ToList();
        var usable = Parts.Where(p => p.IsValid && p.Quantity > 0).ToList();
        if (usable.Count == 0)
        {
            _dialogs.ShowMessage("Không có chi tiết hợp lệ để nesting (cần kích thước lớn hơn 0 và số lượng lớn hơn 0).");
            return;
        }
        long totalInstances = usable.Sum(part => (long)part.Quantity);
        if (totalInstances > NestEngine.MaxPartInstances)
        {
            _dialogs.ShowMessage($"Tổng số chi tiết vượt giới hạn {NestEngine.MaxPartInstances:N0} cho mỗi lần nesting.");
            return;
        }

        var validSheets = Settings.SheetPresets.Where(s => !string.IsNullOrWhiteSpace(s.Material) &&
            !string.Equals(s.Material, "Chưa phân loại", StringComparison.OrdinalIgnoreCase) && s.Thickness > 0 &&
            s.Width > 2 * Settings.Margin && s.Height > 2 * Settings.Margin).ToList();
        var preflight = _nestingOrchestrator.BuildPreflightIssues(usable, Settings, validSheets);
        var materialGroups = _nestingOrchestrator.GroupMaterialThicknesses(usable).ToList();
        if (preflight.Count > 0)
        {
            Messages.Clear();
            foreach (var message in preflight) Messages.Add(message);
            Status = $"Chưa thể nesting: cần gán vật liệu/độ dày hoặc thêm khổ tấm ({preflight.Count} vấn đề).";
            _dialogs.ShowMessage("Kiểm tra trước khi nesting:\n\n• " + string.Join("\n• ", preflight));
            return;
        }

        if (!_dialogs.Confirm($"Bắt đầu nesting {totalInstances:N0} chi tiết cho {materialGroups.Count} nhóm vật liệu?"))
            return;

        // Tên phải duy nhất, vì tên là khoá nhận dạng của chi tiết trong kết quả.
        foreach (var p in usable) p.Name = UniqueName(p.Name, p);

        Messages.Clear();
        foreach (var p in skipped) Messages.Add($"Bỏ qua {p.Name}: {p.Error ?? "kích thước bằng 0"}");

        _cts = new CancellationTokenSource();
        IsBusy = true;
        Progress = "Đang chuẩn bị Nesting hình dạng thật...";
        ProgressPercent = 0;
        _elapsedNestTime = TimeSpan.Zero;
        _estimatedRemainingTime = null;
        OnPropertyChanged(nameof(ElapsedNestTimeText));
        OnPropertyChanged(nameof(EstimatedRemainingTimeText));
        Status = $"Đang nesting · giới hạn {Settings.MaxSeconds:0.#} giây · mục tiêu {Settings.TargetUtilizationPercent:0.#}%...";
        var wallClock = Stopwatch.StartNew();
        var progressClock = RefreshProgressClockAsync(wallClock);
        try
        {
            var combined = new NestResult { Strategy = "Nesting theo vật liệu và độ dày" };
            long completedPlacements = 0;
            var errors = new List<string>();
            var groups = usable.GroupBy(p => (Material: MaterialKey(p.Material), Thickness: Math.Round(p.Thickness, 4)))
                .OrderBy(g => g.Key.Material).ThenBy(g => g.Key.Thickness).ToList();
            int nextSheetIndex = 1;
            bool cancelled = false;
            bool timeLimitReached = false;
            int nextGroupToProcess = 0;

            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                if (wallClock.Elapsed.TotalSeconds >= Settings.MaxSeconds) { timeLimitReached = true; break; }
                var group = groups[groupIndex];
                if (_cts.Token.IsCancellationRequested) { cancelled = true; break; }
                string materialLabel = group.First().Material.Trim();
                double thickness = group.Key.Thickness;
                Progress = $"Nhóm vật liệu {groupIndex + 1}/{groups.Count}: {materialLabel} · {thickness:0.###} mm · chuẩn bị chi tiết...";
                var groupSheets = validSheets.Where(sh => MaterialKey(sh.Material) == group.Key.Material &&
                    Math.Round(sh.Thickness, 4) == thickness)
                    .OrderBy(sh => sh.Width * sh.Height).ToList();
                var remaining = group.ToDictionary(p => p.Name, p => p.Quantity, StringComparer.OrdinalIgnoreCase);
                var nextInstance = group.ToDictionary(p => p.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
                int usedForGroup = 0;

                while (remaining.Values.Any(q => q > 0) && !_cts.Token.IsCancellationRequested && !timeLimitReached)
                {
                    var defs = group.Where(p => remaining[p.Name] > 0)
                        .Select(p => p.ToDefinition() with { Quantity = remaining[p.Name] }).ToList();
                    NestRun? bestRun = null;
                    SheetPreset? bestPreset = null;
                    SheetResult? bestFirst = null;
                    int bestCount = -1;
                    double bestUtilization = -1;
                    double bestArea = double.PositiveInfinity;

                    for (int sheetIndex = 0; sheetIndex < groupSheets.Count; sheetIndex++)
                    {
                        if (_cts.Token.IsCancellationRequested) { cancelled = true; break; }
                        double remainingSeconds = Settings.MaxSeconds - wallClock.Elapsed.TotalSeconds;
                        if (remainingSeconds <= 0) { timeLimitReached = true; break; }
                        var preset = groupSheets[sheetIndex];
                        var trialSettings = new NestSettings();
                        trialSettings.FromJson(Settings.ToJson());
                        trialSettings.SheetWidth = preset.Width;
                        trialSettings.SheetHeight = preset.Height;
                        int remainingTrials = Math.Max(1, groups.Count - groupIndex + groupSheets.Count - sheetIndex - 1);
                        trialSettings.MaxSeconds = Math.Max(0.05, remainingSeconds / remainingTrials);
                        string progressPrefix = $"Nhóm {groupIndex + 1}/{groups.Count} · {materialLabel} · {thickness:0.###} mm · khổ {sheetIndex + 1}/{groupSheets.Count}: {preset.Name}";
                        Progress = progressPrefix + " · đang tính phương án...";
                        var progress = new ViewModelProgress<string>(m => Progress = progressPrefix + " · " + m);
                        long remainingInGroup = remaining.Values.Sum(value => (long)value);
                        var percentProgress = new ViewModelProgress<double>(percent =>
                        {
                            long completed = Interlocked.Read(ref completedPlacements);
                            UpdateProgressEstimate(completed, remainingInGroup * percent * 0.35, totalInstances);
                        });
                        var trial = await NestingService.RunAsync(defs, trialSettings, progress, _cts.Token, percentProgress);
                        cancelled |= trial.Cancelled;
                        var first = trial.Result.Sheets.FirstOrDefault(sh => sh.Placements.Count > 0);
                        if (first == null) continue;
                        int count = first.Placements.Count;
                        double area = first.Width * first.Height;
                        if (count > bestCount || (count == bestCount && first.Utilization > bestUtilization + 1e-9) ||
                            (count == bestCount && Math.Abs(first.Utilization - bestUtilization) <= 1e-9 && area < bestArea))
                        {
                            bestRun = trial; bestPreset = preset; bestFirst = first;
                            bestCount = count; bestUtilization = first.Utilization; bestArea = area;
                        }
                        if (wallClock.Elapsed.TotalSeconds >= Settings.MaxSeconds) { timeLimitReached = true; break; }
                    }

                    if (bestRun == null || bestPreset == null || bestFirst == null) break;
                    errors.AddRange(bestRun.Errors.Select(e => $"{materialLabel} · {thickness:0.###} mm · {bestPreset.Name}: {e}"));

                    var sheetsToApply = groupSheets.Count == 1
                        ? bestRun.Result.Sheets.Where(sh => sh.Placements.Count > 0).ToList()
                        : new List<SheetResult> { bestFirst };

                    int addedInBatch = 0;
                    foreach (var srcSheet in sheetsToApply)
                    {
                        var output = new SheetResult
                        {
                            Index = nextSheetIndex++, Width = srcSheet.Width, Height = srcSheet.Height,
                            Material = materialLabel, Thickness = thickness, StockName = bestPreset.Name
                        };
                        foreach (var placement in srcSheet.Placements)
                        {
                            if (!remaining.ContainsKey(placement.PartName) || remaining[placement.PartName] <= 0) continue;
                            remaining[placement.PartName]--;
                            int instance = ++nextInstance[placement.PartName];
                            output.Placements.Add(placement with { Instance = instance });
                        }
                        if (output.Placements.Count == 0) continue;
                        usedForGroup++;
                        addedInBatch++;
                        Interlocked.Add(ref completedPlacements, output.Placements.Count);
                        combined.Sheets.Add(output);
                        combined.Notes.Add($"Tấm {output.Index}: {materialLabel} · {thickness:0.###} mm · {bestPreset.Name} ({bestPreset.Width:0.##} × {bestPreset.Height:0.##} mm).");
                    }
                    if (addedInBatch == 0) break;
                    if (bestRun.Cancelled || _cts.Token.IsCancellationRequested) { cancelled = true; break; }
                    if (timeLimitReached || wallClock.Elapsed.TotalSeconds >= Settings.MaxSeconds) { timeLimitReached = true; break; }
                }

                if (remaining.Values.Any(q => q > 0))
                    foreach (var part in group.Where(p => remaining[p.Name] > 0))
                        combined.Unplaced.Add($"{part.Name}: còn {remaining[part.Name]} / {part.Quantity} chi tiết chưa xếp ({(timeLimitReached ? "hết giới hạn thời gian" : "không vừa khổ phù hợp hoặc đã dừng")}).");
                combined.Notes.Add($"Nhóm {materialLabel} · {thickness:0.###} mm: đã tạo {usedForGroup} tấm.");
                nextGroupToProcess = groupIndex + 1;
                if (timeLimitReached) break;
            }

            if (timeLimitReached)
            {
                foreach (var pendingGroup in groups.Skip(nextGroupToProcess))
                    foreach (var part in pendingGroup.Where(p => p.Quantity > 0))
                        combined.Unplaced.Add($"{part.Name}: còn {part.Quantity} / {part.Quantity} chi tiết chưa xếp (hết giới hạn thời gian toàn tác vụ).");
                combined.Notes.Add($"Đã chạm giới hạn {Settings.MaxSeconds:0.#} giây; giữ lại kết quả tốt nhất tìm được.");
            }

            wallClock.Stop();
            LastNestDuration = wallClock.Elapsed;
            LastNestAt = DateTime.Now;
            var run = new NestRun(combined, Settings.ToSheet(), errors, cancelled, wallClock.Elapsed, timeLimitReached);
            ShowResult(run);
            lock (_progressSync)
            {
                ProgressPercent = 100;
                UpdateEstimatedRemainingTime();
            }
        }
        finally
        {
            wallClock.Stop();
            await progressClock;
            IsBusy = false;
            Progress = "";
            _cts.Dispose();
            _cts = null;
        }
    }

    private static string MaterialKey(string? material) => (material ?? "").Trim().ToUpperInvariant();

    private void ShowResult(NestRun run)
    {
        var r = run.Result;
        Result = r;
        _resultSheet = run.Sheet;
        OnPropertyChanged(nameof(ResultSheet));
        IsStale = false;
        IsEdited = false;

        Sheets.Clear();
        foreach (var s in r.Sheets) Sheets.Add(new SheetItem(s));

        foreach (var n in r.Notes) Messages.Add(n);
        foreach (var u in r.Unplaced) Messages.Add("Chưa đặt được: " + u);
        foreach (var e in run.Errors) Messages.Add("KIỂM TRA LỖI: " + e);

        Summary = ReportBuilder.Summary(r, run.Sheet);
        Status = $"Xong sau {run.Elapsed.TotalSeconds:0.0}s" +
                 (run.TimeLimitReached ? " (đã chạm giới hạn thời gian; giữ kết quả tốt nhất)" : run.Cancelled ? " (đã dừng theo yêu cầu)" : "") +
                 $": {r.Sheets.Count} tấm, hiệu suất {r.Utilization:P1}" +
                 (r.Unplaced.Count > 0 ? $", CHƯA ĐẶT ĐƯỢC {r.Unplaced.Count}" : "") +
                 (run.Errors.Count > 0 ? $", {run.Errors.Count} LỖI KIỂM TRA" : "");

        if (run.TimeLimitReached) Toasts.Show(ToastKind.Warning, "Đã hết giới hạn thời gian Nesting", $"Đã giữ {r.Sheets.Count} tấm và phương án tốt nhất tìm được.");
        else if (r.Unplaced.Count > 0) Toasts.Show(ToastKind.Warning, $"Còn {r.Unplaced.Count} chi tiết chưa đặt được", "Xem mục Thông báo hoặc thêm tấm vật liệu.");
        else if (run.Errors.Count > 0) Toasts.Show(ToastKind.Error, $"{run.Errors.Count} lỗi kiểm tra", run.Errors[0]);
        else Toasts.Show(ToastKind.Success, "Nesting xong", $"{r.Sheets.Count} tấm, hiệu suất {r.Utilization:P1}.");

        SelectedSheet = Sheets.Count > 0 ? Sheets[0] : null;
        if (Sheets.Count == 0) { Scene = PreviewScene.Empty(run.Sheet); }
        ResetViewRequested?.Invoke();
    }

    // ---------- xem trước và chỉnh tay ----------

    private SheetResult? CurrentSheet => SelectedSheet?.Sheet;

    private SheetDefinition DefinitionFor(SheetResult sheet)
        => _sceneBuilder.DefinitionFor(sheet, _resultSheet, Settings);

    private List<string> ValidateResult(NestResult result)
        => _exportCoordinator.ValidateResult(result, DefinitionFor);

    private void RebuildScene()
    {
        Scene = _sceneBuilder.Build(CurrentSheet, _resultSheet, Settings);
        RefreshPlacementSelection();
        SelectedInfo = DescribeSelected();
    }

    private string DescribeSelected()
    {
        if (CurrentSheet == null || SelectedPlacement < 0 || SelectedPlacement >= CurrentSheet.Placements.Count) return "";
        var p = CurrentSheet.Placements[SelectedPlacement];
        double ang = p.Rotated ? 90 : p.AngleDeg;
        return $"{p.PartName} #{p.Instance} · X {p.X:0.#}, Y {p.Y:0.#} · {p.Width:0.#} x {p.Height:0.#} · xoay {ang:0.#}°";
    }

    /// <summary>Kiểm tra (không ghi) việc đặt chi tiết tại góc dưới-trái (x, y). null = hợp lệ.</summary>
    public string? CheckMove(int index, double x, double y)
    {
        var sheet = CurrentSheet;
        if (sheet == null || _resultSheet == null) return "Không có chi tiết được chọn.";
        return _manualPlacementService.CheckMove(sheet, DefinitionFor(sheet), index, x, y);
    }

    public string? CheckMoveSelected(double dx, double dy)
    {
        var sheet = CurrentSheet;
        if (sheet == null || _resultSheet == null || _selectedPlacementIndices.Count == 0) return "Chưa chọn chi tiết.";
        return _manualPlacementService.CheckMoveSelected(sheet, DefinitionFor(sheet), _selectedPlacementIndices, dx, dy);
    }

    public bool TryMoveSelected(double dx, double dy)
    {
        var sheet = CurrentSheet;
        if (sheet == null) return false;
        string? error = CheckMoveSelected(dx, dy);
        if (error != null)
        {
            Status = "Không thể di chuyển nhóm: " + error;
            Toasts.Show(ToastKind.Warning, "Không thể di chuyển nhóm", error);
            return false;
        }

        if (!_manualPlacementService.TryMoveSelected(sheet, DefinitionFor(sheet), _selectedPlacementIndices, dx, dy))
        {
            Status = "Không thể di chuyển nhóm: phạm vi không hợp lệ.";
            return false;
        }

        AfterManualEdit($"Đã di chuyển {_selectedPlacementIndices.Count} chi tiết.");
        return true;
    }

    public bool MoveSelectedUntilBlocked(double dx, double dy)
    {
        var sheet = CurrentSheet;
        if (sheet == null || _selectedPlacementIndices.Count == 0 || _resultSheet == null) return false;
        bool moved = _manualPlacementService.MoveSelectedUntilBlocked(sheet, DefinitionFor(sheet), _selectedPlacementIndices, dx, dy);
        if (moved) AfterManualEdit($"Đã di chuyển {_selectedPlacementIndices.Count} chi tiết theo vật cản.");
        return moved;
    }

    public int DeleteSelectedPlacements()
    {
        var sheet = CurrentSheet;
        if (sheet == null) return 0;
        var indices = _selectedPlacementIndices.Where(i => i >= 0 && i < sheet.Placements.Count).OrderDescending().ToArray();
        if (indices.Length == 0 && SelectedPlacement >= 0 && SelectedPlacement < sheet.Placements.Count) indices = new[] { SelectedPlacement };
        if (indices.Length == 0) return 0;
        foreach (int index in indices) sheet.Placements.RemoveAt(index);
        RenumberInstances();
        UpdateUnplacedFromRemaining();
        OnPropertyChanged(nameof(PlacedCount));
        OnPropertyChanged(nameof(RemainingCount));
        RefreshSheetItems();
        SetSelectedPlacements(Array.Empty<int>());
        AfterManualEdit($"Đã xóa {indices.Length} chi tiết khỏi tấm.");
        return indices.Length;
    }

    public bool DeleteSelectedSheet()
    {
        if (!IsIdle || Result == null || CurrentSheet == null) return false;
        int sheetIndex = Result.Sheets.IndexOf(CurrentSheet);
        if (sheetIndex < 0) return false;
        int deletedNumber = CurrentSheet.Index;
        Result.Sheets.RemoveAt(sheetIndex);

        if (Result.Sheets.Count == 0)
        {
            ClearResult();
        }
        else
        {
            for (int i = 0; i < Result.Sheets.Count; i++)
                Result.Sheets[i].Index = i + 1;
            RefreshSheetItems();
            UpdateUnplacedFromRemaining();
            OnPropertyChanged(nameof(PlacedCount));
            OnPropertyChanged(nameof(RemainingCount));
            Summary = ReportBuilder.Summary(Result, _resultSheet ?? Settings.ToSheet());
            IsStale = false;
            IsEdited = true;
        }

        if (Result == null) IsEdited = true;
        Status = $"Đã xóa nesting của tấm {deletedNumber}; danh sách chi tiết vẫn được giữ lại.";
        UpdateActiveGroupSummary();
        RaiseCommands();
        return true;
    }

    public void ClearNesting()
    {
        if (Result == null) return;
        ClearResult();
        UpdateRemainingQuantities();
        OnPropertyChanged(nameof(PartGroups));
        Status = "Đã xóa toàn bộ kết quả nesting; danh sách Part vẫn được giữ lại.";
    }

    public bool AddManualSheet()
    {
        if (!IsIdle) return false;
        var preset = SelectedPreset ?? Settings.SheetPresets.FirstOrDefault();
        double width = SelectedSheet?.Sheet.Width ?? preset?.Width ?? _resultSheet?.Width ?? Settings.SheetWidth;
        double height = SelectedSheet?.Sheet.Height ?? preset?.Height ?? _resultSheet?.Height ?? Settings.SheetHeight;
        string material = SelectedSheet?.Material ?? preset?.Material ?? "Chưa phân loại";
        double thickness = SelectedSheet?.Thickness ?? preset?.Thickness ?? 0;
        var definition = new SheetDefinition(width, height, Settings.Margin, Settings.Spacing);
        var sheet = new SheetResult
        {
            Index = (Result?.Sheets.Count ?? 0) + 1,
            Width = width, Height = height, Material = material, Thickness = thickness,
            StockName = preset?.Name ?? "Đặt thủ công"
        };
        if (Result == null)
        {
            Result = new NestResult { Strategy = "Bố trí thủ công" };
            _resultSheet = definition;
            OnPropertyChanged(nameof(ResultSheet));
        }
        Result.Sheets.Add(sheet);
        _resultSheet ??= definition;
        Sheets.Add(new SheetItem(sheet));
        SelectedSheet = Sheets[^1];
        IsStale = false;
        IsEdited = true;
        OnPropertyChanged(nameof(PlacedCount));
        OnPropertyChanged(nameof(RemainingCount));
        UpdateActiveGroupSummary();
        Status = $"Đã thêm tấm thủ công {sheet.Index} ({width:0.##} × {height:0.##} mm).";
        return true;
    }

    private void RenumberInstances()
    {
        var next = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (Result == null) return;
        foreach (var sheet in Result.Sheets)
            for (int i = 0; i < sheet.Placements.Count; i++)
            {
                var p = sheet.Placements[i];
                int instance = next.GetValueOrDefault(p.PartName) + 1;
                next[p.PartName] = instance;
                if (p.Instance != instance) sheet.Placements[i] = p with { Instance = instance };
            }
    }

    private void UpdateUnplacedFromRemaining()
    {
        if (Result == null) return;
        Result.Unplaced.Clear();
        foreach (var part in Parts)
        {
            int placed = Result.Sheets.SelectMany(s => s.Placements).Count(p => string.Equals(p.PartName, part.Name, StringComparison.OrdinalIgnoreCase));
            int remaining = Math.Max(0, part.Quantity - placed);
            part.SetRemainingQuantity(remaining);
            if (remaining > 0) Result.Unplaced.Add($"{part.Name}: còn {remaining} / {part.Quantity} chi tiết chưa xếp.");
        }
    }

    private void RefreshSheetItems()
    {
        if (Result == null) return;
        int selectedIndex = SelectedSheet == null ? -1 : Sheets.IndexOf(SelectedSheet);
        Sheets.Clear();
        foreach (var sheet in Result.Sheets) Sheets.Add(new SheetItem(sheet));
        SelectedSheet = selectedIndex >= 0 && selectedIndex < Sheets.Count ? Sheets[selectedIndex] : Sheets.FirstOrDefault();
    }

    /// <summary>Dời chi tiết nếu hợp lệ. Trả về null nếu thành công, ngược lại là lý do (không đổi gì).</summary>
    public string? TryMove(int index, double x, double y)
    {
        var sheet = CurrentSheet;
        if (sheet == null || _resultSheet == null) return "Không có tấm đang chọn.";
        var err = _manualPlacementService.TryMove(sheet, DefinitionFor(sheet), index, x, y);
        if (err != null) { Status = "Không thể đặt ở đó: " + err; Toasts.Show(ToastKind.Warning, "Không thể đặt ở đó", err); return err; }
        var p = sheet.Placements[index];
        AfterManualEdit($"Đã dời {p.PartName} #{p.Instance}.");
        return null;
    }

    /// <summary>Dời part theo một trục tới vị trí xa nhất trước vật cản hoặc mép tấm.</summary>
    public bool MoveUntilBlocked(int index, double dx, double dy)
    {
        var sheet = CurrentSheet;
        if (sheet == null || _resultSheet == null || index < 0 || index >= sheet.Placements.Count) return false;
        bool moved = _manualPlacementService.MoveUntilBlocked(sheet, DefinitionFor(sheet), index, dx, dy);
        if (moved) AfterManualEdit($"Đã dời {sheet.Placements[index].PartName} #{sheet.Placements[index].Instance} đến vị trí tối đa.");
        return moved;
    }

    /// <summary>Thả một instance từ bộ lọc vào sheet; tâm hình được đặt tại tọa độ thả (mm).</summary>
    public bool TryPlaceManually(PartItem part, double centerX, double centerY)
    {
        if (!IsIdle || !part.IsValid || part.Quantity <= 0 || string.IsNullOrWhiteSpace(part.Name)) return false;
        if (IsStale)
        {
            Status = "Kết quả đã cũ; hãy chạy nesting lại trước khi tiếp tục đặt thủ công.";
            Toasts.Show(ToastKind.Warning, "Kết quả đã cũ", Status);
            return false;
        }
        var source = part.ToDefinition();
        if (source.Shape != null && !ShapeOps.Build(source.Shape).Reliable)
        {
            Status = $"Không thể đặt {part.Name}: contour không hợp lệ.";
            Toasts.Show(ToastKind.Warning, "Contour không hợp lệ", Status);
            return false;
        }
        if (Result != null && SelectedSheet == null)
        {
            if (Sheets.Count == 0) return false;
            SelectedSheet = Sheets[0];
        }

        var preset = part.AssignedPreset ?? SelectedPreset;
        string partMaterial = string.IsNullOrWhiteSpace(part.Material) ? preset?.Material ?? "" : part.Material;
        double partThickness = part.Thickness > 0 ? part.Thickness : preset?.Thickness ?? 0;
        if (SelectedSheet != null &&
            ((!string.IsNullOrWhiteSpace(partMaterial) && !string.Equals(MaterialKey(SelectedSheet.Material), MaterialKey(partMaterial), StringComparison.Ordinal)) ||
             (partThickness > 0 && Math.Abs(SelectedSheet.Thickness - partThickness) > 1e-4)))
        {
            Status = "Vật liệu hoặc độ dày của chi tiết không khớp với tấm đang chọn.";
            Toasts.Show(ToastKind.Warning, "Khác vật liệu/độ dày", Status);
            return false;
        }
        double width = SelectedSheet?.Sheet.Width ?? preset?.Width ?? Settings.SheetWidth;
        double height = SelectedSheet?.Sheet.Height ?? preset?.Height ?? Settings.SheetHeight;
        string material = SelectedSheet?.Sheet.Material ?? partMaterial;
        double thickness = SelectedSheet?.Sheet.Thickness ?? partThickness;
        int alreadyPlaced = Result?.Sheets.SelectMany(s => s.Placements)
            .Count(p => string.Equals(p.PartName, part.Name, StringComparison.OrdinalIgnoreCase)) ?? 0;
        if (alreadyPlaced >= part.Quantity)
        {
            Status = $"Đã đặt đủ số lượng của {part.Name}.";
            Toasts.Show(ToastKind.Warning, "Đã đủ số lượng", Status);
            return false;
        }

        var definition = new SheetDefinition(width, height, Settings.Margin, Settings.Spacing);
        double partWidth = source.Width, partHeight = source.Height;
        var candidate = new Placement(part.Name, alreadyPlaced + 1, centerX - partWidth / 2, centerY - partHeight / 2,
            partWidth, partHeight, false, source.Shape, 0,
            source.Shape == null ? partWidth * partHeight : ShapeOps.NetArea(source.Shape));
        bool createResult = Result == null;
        var sheet = createResult
            ? new SheetResult { Index = 1, Width = width, Height = height, Material = material, Thickness = thickness, StockName = preset?.Name ?? "Đặt thủ công" }
            : SelectedSheet!.Sheet;
        string? error = Validator.CheckPlacement(sheet, -1, candidate, definition);
        if (error != null)
        {
            Status = "Không thể đặt ở đó: " + error;
            Toasts.Show(ToastKind.Warning, "Không thể đặt chi tiết", error);
            return false;
        }
        if (createResult)
        {
            _resultSheet = definition;
            var manualResult = new NestResult { Strategy = "Bố trí thủ công" };
            manualResult.Sheets.Add(sheet);
            Result = manualResult;
            Sheets.Clear();
            Sheets.Add(new SheetItem(sheet));
            SelectedSheet = Sheets[0];
            IsStale = false;
        }
        sheet.Placements.Add(candidate);
        OnPropertyChanged(nameof(PlacedCount));
        OnPropertyChanged(nameof(RemainingCount));
        int sheetIndex = Sheets.IndexOf(SelectedSheet!);
        if (sheetIndex >= 0)
        {
            Sheets[sheetIndex] = new SheetItem(sheet);
            SelectedSheet = Sheets[sheetIndex];
        }
        if (Result != null)
        {
            UpdateUnplacedFromRemaining();
            OnPropertyChanged(nameof(PartGroups));
        }
        SelectedPlacement = sheet.Placements.Count - 1;
        AfterManualEdit($"Đã đặt thủ công {part.Name} #{candidate.Instance}.");
        return true;
    }

    public void MirrorSelected()
    {
        var sheet = CurrentSheet;
        if (sheet == null || SelectedPlacement < 0 || SelectedPlacement >= sheet.Placements.Count) return;
        var definition = DefinitionFor(sheet);
        if (!_manualPlacementService.TryMirror(sheet, definition, SelectedPlacement))
        {
            var error = Validator.CheckPlacement(sheet, SelectedPlacement, sheet.Placements[SelectedPlacement], definition);
            Status = "Không lật được: " + (error ?? "không xác định");
            Toasts.Show(ToastKind.Warning, "Không lật được", Status);
            return;
        }
        AfterManualEdit($"Đã lật {sheet.Placements[SelectedPlacement].PartName} #{sheet.Placements[SelectedPlacement].Instance} theo trục dọc.");
    }

    public void RotateSelected()
    {
        var s = CurrentSheet;
        if (s == null || _resultSheet == null || SelectedPlacement < 0 || SelectedPlacement >= s.Placements.Count) return;
        var definition = DefinitionFor(s);
        if (!_manualPlacementService.TryRotate(s, definition, SelectedPlacement))
        {
            var error = Validator.CheckPlacement(s, SelectedPlacement, s.Placements[SelectedPlacement], definition);
            Status = "Không xoay được: " + (error ?? "không xác định");
            Toasts.Show(ToastKind.Warning, "Không xoay được", Status);
            return;
        }
        AfterManualEdit($"Đã xoay {s.Placements[SelectedPlacement].PartName} #{s.Placements[SelectedPlacement].Instance} thêm 90°.");
    }

    private void AfterManualEdit(string msg)
    {
        IsEdited = true;
        int[] keep = _selectedPlacementIndices.ToArray();
        int primary = SelectedPlacement;
        RebuildScene();
        SetSelectedPlacements(keep, primary);
        Status = msg + " Kết quả đã được chỉnh tay; xuất DXF sẽ dùng vị trí mới.";
    }

    // ---------- xuất ----------

    public void ExportDxf() => ExportDxf(new DxfExportOptions
    {
        FileLayout = DxfFileLayout.MultipleFiles,
        AddPartLabels = true,
        OutputSheets = true
    });

    public void ExportDxf(DxfExportOptions options)
    {
        if (Result == null || _resultSheet == null) return;
        var errors = ValidateResult(Result);
        if (errors.Count > 0 && !_dialogs.Confirm($"Kết quả hiện có {errors.Count} lỗi (chồng lấn/quá gần/ra ngoài tấm), ví dụ:\n{errors[0]}\n\nVẫn xuất?"))
            return;
        string? destination = options.FileLayout == DxfFileLayout.SingleFile
            ? _dialogs.PickSaveFile("Lưu bản vẽ DXF", "DXF (*.dxf)|*.dxf", "nesting.dxf")
            : _dialogs.PickFolder("Chọn thư mục để xuất từng tấm DXF");
        if (destination == null) return;
        try
        {
            var files = _exportCoordinator.ExportSheets(Result, _resultSheet, destination, options);
            string location = options.FileLayout == DxfFileLayout.SingleFile ? files[0] : destination;
            Status = $"Đã xuất {files.Count} DXF: {location}";
            Toasts.Show(ToastKind.Success, "Đã xuất DXF", options.FileLayout == DxfFileLayout.SingleFile ? Path.GetFileName(location) : $"{files.Count} file trong {location}");
        }
        catch (Exception ex) { Fail("Không xuất được: " + ex.Message); }
    }

    // ---------- dự án ----------

    public void NewProject()
    {
        if (IsProjectDirty && !_dialogs.Confirm("Các thay đổi của dự án hiện tại chưa được lưu. Tạo dự án mới và bỏ các thay đổi này?")) return;
        Parts.Clear();
        ClearResult();
        ProjectGroups.Clear();
        var group = _projectLifecycleService.CreateEmptyGroup(ProjectPath ?? "", "Nhóm vật liệu 1");
        ProjectGroups.Add(group);
        _activeProjectGroup = group;
        _syncingProjectGroups = true;
        SelectedProjectGroup = group;
        _syncingProjectGroups = false;
        NewProjectGroupName = "";
        ProjectPath = null;
        Messages.Clear();
        IsProjectDirty = false;
        OnPropertyChanged(nameof(Title));
        RaiseCommands();
        Status = "Dự án mới.";
    }

    private void ClearResult()
    {
        Result = null;
        _resultSheet = null;
        _selectedPlacementIndices.Clear();
        SelectedPlacement = -1;
        OnPropertyChanged(nameof(SelectedPlacementIndices));
        Sheets.Clear();
        SelectedSheet = null;
        Scene = PreviewScene.Empty(Settings.ToSheet());
        IsStale = false;
        IsEdited = false;
        Summary = "";
        UpdateRemainingQuantities();
    }

    public void OpenProjectDialog()
    {
        var f = _dialogs.PickOpenFile("Mở dự án", "Cyclone Nest2D (*.nest2d)|*.nest2d");
        if (f != null) OpenProject(f);
    }

    public bool OpenProject(string path)
    {
        if (IsProjectDirty && !_dialogs.Confirm("Các thay đổi của dự án hiện tại chưa được lưu. Mở dự án khác và bỏ các thay đổi này?"))
            return false;
        try
        {
            var problems = new List<string>();
            string fullPath = Path.GetFullPath(path);
            bool isProjectIndex = ProjectStore.IsProjectIndex(fullPath);
            List<ProjectStore.GroupReferenceDto>? references = null;
            ProjectStore.WorkspaceSnapshot? legacySnapshot = null;
            NestSettings? legacySettings = null;
            if (isProjectIndex)
                references = ProjectStore.LoadProjectIndex(fullPath);
            else
            {
                legacySettings = new NestSettings();
                legacySnapshot = ProjectStore.LoadWorkspace(fullPath, legacySettings, problems);
            }

            ProjectGroups.Clear();
            if (isProjectIndex)
            {
                foreach (var group in _projectLifecycleService.BuildProjectGroupsFromIndex(fullPath, problems))
                    ProjectGroups.Add(group);
                ProjectPath = fullPath;
                _activeProjectGroup = null;
                _syncingProjectGroups = true;
                SelectedProjectGroup = null;
                _syncingProjectGroups = false;
                Parts.Clear();
                ClearResult();
                foreach (var group in ProjectGroups.Where(group => File.Exists(group.Path)))
                {
                    try
                    {
                        _activeProjectGroup = group;
                        LoadProjectGroup(group, problems);
                        _syncingProjectGroups = true;
                        SelectedProjectGroup = group;
                        _syncingProjectGroups = false;
                        break;
                    }
                    catch (Exception ex)
                    {
                        _activeProjectGroup = null;
                        problems.Add($"Không mở được nhóm '{group.Name}': {ex.Message}");
                    }
                }
                if (_activeProjectGroup == null)
                    NewProjectGroupName = "";
            }
            else
            {
                Settings.FromJson(legacySettings!.ToJson());
                _preset = Settings.SheetPresets.FirstOrDefault(preset =>
                    Math.Abs(preset.Width - Settings.SheetWidth) < 1e-6 &&
                    Math.Abs(preset.Height - Settings.SheetHeight) < 1e-6);
                OnPropertyChanged(nameof(SelectedPreset));
                var snapshot = legacySnapshot!;
                var legacyGroup = _projectLifecycleService.CreateLegacyGroup(fullPath, snapshot.Parts, snapshot.Result);
                ProjectGroups.Add(legacyGroup);
                ProjectPath = fullPath;
                _activeProjectGroup = legacyGroup;
                _syncingProjectGroups = true;
                SelectedProjectGroup = legacyGroup;
                _syncingProjectGroups = false;
                RestoreWorkspace(snapshot, problems);
            }
            Messages.Clear();
            PostMessages(problems);
            SelectedPart = Parts.Count > 0 ? Parts[0] : null;
            IsProjectDirty = false;
            OnPropertyChanged(nameof(Title));
            RaiseCommands();
            Status = $"Đã mở dự án: {ProjectGroups.Count} nhóm, {Parts.Count} chi tiết đang mở" +
                     (problems.Count > 0 ? $", {problems.Count} vấn đề (xem Thông báo)." : ".");
            return true;
        }
        catch (Exception ex) { Fail("Không mở được dự án: " + ex.Message); return false; }
    }

    public bool SaveProject(bool saveAs)
    {
        string? path = ProjectPath;
        if (saveAs || path == null)
        {
            path = _dialogs.PickSaveFile("Lưu dự án", "Cyclone Nest2D (*.nest2d)|*.nest2d", "du_an" + ProjectStore.Extension);
            if (path == null) return false;
            if (!path.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase)) path += ProjectStore.Extension;
        }
        try
        {
            string fullPath = Path.GetFullPath(path);
            _projectLifecycleService.SyncGroupPathsForProject(ProjectPath, fullPath, ProjectGroups);

            var active = _activeProjectGroup;
            if (active != null)
            {
                if (string.IsNullOrWhiteSpace(active.Path) ||
                    string.Equals(Path.GetFullPath(active.Path), fullPath, StringComparison.OrdinalIgnoreCase))
                    active.Path = _projectLifecycleService.BuildChildProjectPath(fullPath, active.Id);
                SaveActiveWorkspace();
            }
            _projectLifecycleService.SaveProjectIndex(fullPath, ProjectGroups);
            ProjectPath = fullPath;
            IsProjectDirty = false;
            OnPropertyChanged(nameof(Title));
            RaiseCommands();
            Status = "Đã lưu dự án: " + fullPath;
            Toasts.Show(ToastKind.Success, "Đã lưu dự án", Path.GetFileName(fullPath));
            return true;
        }
        catch (Exception ex) { Fail("Không lưu được dự án: " + ex.Message); return false; }
    }

    public bool CreateProjectGroup(string name)
    {
        if (ProjectPath == null && !SaveProject(true)) return false;
        if (IsProjectDirty && !SaveProject(false)) return false;

        if (!_projectLifecycleService.TryCreateGroup(ProjectGroups, ProjectPath, name, _dialogs, out var group, out var message))
        {
            if (!string.IsNullOrEmpty(message)) Status = message;
            return false;
        }

        _projectLifecycleService.SaveEmptyGroupWorkspace(group!, Settings);
        ProjectGroups.Add(group!);
        IsProjectDirty = true;
        NewProjectGroupName = "";
        SelectedProjectGroup = group!;
        SaveProject(false);
        return true;
    }

    public bool RenameProjectGroup(ProjectGroupItem? group, string name)
    {
        if (!_projectLifecycleService.TryRenameGroup(ProjectGroups, group, name, _dialogs, out var message))
        {
            if (!string.IsNullOrEmpty(message)) Status = message;
            return false;
        }

        NewProjectGroupName = "";
        IsProjectDirty = true;
        OnPropertyChanged(nameof(Title));
        return SaveProject(false);
    }

    public bool DuplicateProjectGroup()
    {
        var source = _activeProjectGroup;
        if (source == null || ProjectPath == null) return false;
        if (!SaveProject(false)) return false;

        if (!_projectLifecycleService.TryDuplicateGroup(ProjectGroups, ProjectPath, source, out var duplicate, out var message))
        {
            if (!string.IsNullOrEmpty(message)) Status = message;
            return false;
        }

        ProjectGroups.Add(duplicate!);
        IsProjectDirty = true;
        SelectedProjectGroup = duplicate!;
        return SaveProject(false);
    }

    public bool RemoveProjectGroup(ProjectGroupItem? group)
    {
        if (!_projectLifecycleService.TryRemoveGroup(ProjectGroups, group, _dialogs, out var reason))
        {
            if (!string.IsNullOrEmpty(reason) && reason != "Người dùng hủy bỏ") Status = reason;
            return false;
        }

        if (group == _activeProjectGroup)
        {
            var next = ProjectGroups.First(other => other != group);
            SelectedProjectGroup = next;
        }
        ProjectGroups.Remove(group!);
        IsProjectDirty = true;
        RaiseCommands();
        return SaveProject(false);
    }

    private void ActivateProjectGroup(ProjectGroupItem group)
    {
        if (ReferenceEquals(group, _activeProjectGroup)) return;
        var previous = _activeProjectGroup;
        if (ProjectPath == null)
        {
            _dialogs.ShowMessage("Hãy lưu dự án trước khi chuyển hoặc tạo nhóm vật liệu.");
            RestoreSelectedProjectGroup(previous);
            return;
        }
        try
        {
            if (!SaveProject(false)) { RestoreSelectedProjectGroup(previous); return; }

            if (!File.Exists(group.Path))
                throw new FileNotFoundException($"Không tìm thấy file nhóm '{group.Name}'.", group.Path);
            _activeProjectGroup = group;
            var problems = new List<string>();
            LoadProjectGroup(group, problems);
            Messages.Clear();
            PostMessages(problems);
            IsProjectDirty = false;
            NewProjectGroupName = group.Name;
            OnPropertyChanged(nameof(Title));
            RaiseCommands();
            Status = $"Đang mở nhóm {group.Name}: {Parts.Count} chi tiết, {Sheets.Count} tấm.";
        }
        catch (Exception ex)
        {
            _dialogs.ShowMessage("Không mở được nhóm: " + ex.Message);
            _activeProjectGroup = previous;
            RestoreSelectedProjectGroup(previous);
        }
    }

    private void RestoreSelectedProjectGroup(ProjectGroupItem? group)
    {
        _projectLifecycleService.RestoreSelectedProjectGroup(group, value =>
        {
            _syncingProjectGroups = true;
            SelectedProjectGroup = value;
            _syncingProjectGroups = false;
            OnPropertyChanged(nameof(Title));
        });
    }

    private void LoadProjectGroup(ProjectGroupItem group, List<string> problems)
    {
        var settings = new NestSettings();
        var snapshot = ProjectStore.LoadWorkspace(group.Path, settings, problems);
        Settings.FromJson(settings.ToJson());
        _preset = Settings.SheetPresets.FirstOrDefault(preset =>
            Math.Abs(preset.Width - Settings.SheetWidth) < 1e-6 &&
            Math.Abs(preset.Height - Settings.SheetHeight) < 1e-6);
        OnPropertyChanged(nameof(SelectedPreset));
        RestoreWorkspace(snapshot, problems);
        group.PartCount = Parts.Count;
        group.SheetCount = Sheets.Count;
        NewProjectGroupName = group.Name;
    }

    private void RestoreWorkspace(ProjectStore.WorkspaceSnapshot snapshot, List<string> problems)
    {
        Parts.Clear();
        ClearResult();
        foreach (var part in snapshot.Parts) Parts.Add(part);
        LinkAllMatchingParts();
        if (snapshot.Result != null && snapshot.ResultSheet != null)
        {
            Result = snapshot.Result;
            _resultSheet = snapshot.ResultSheet;
            OnPropertyChanged(nameof(ResultSheet));
            IsStale = snapshot.IsStale;
            IsEdited = snapshot.IsEdited;
            LastNestDuration = snapshot.LastNestDuration;
            LastNestAt = snapshot.LastNestAt;
            Sheets.Clear();
            foreach (var sheet in snapshot.Result.Sheets) Sheets.Add(new SheetItem(sheet));
            Summary = ReportBuilder.Summary(snapshot.Result, snapshot.ResultSheet);
            SelectedSheet = Sheets.FirstOrDefault();
        }
        SelectedPart = Parts.FirstOrDefault();
        if (Sheets.Count == 0) Scene = PreviewScene.Empty(Settings.ToSheet());
        UpdateActiveGroupSummary();
        IsProjectDirty = false;
    }

    private void SaveActiveWorkspace()
    {
        _projectLifecycleService.SaveActiveWorkspace(_activeProjectGroup, Settings, Parts, Result, _resultSheet,
            IsStale, IsEdited, LastNestDuration, LastNestAt);
        UpdateActiveGroupSummary();
    }

    private void UpdateActiveGroupSummary() =>
        _projectLifecycleService.UpdateGroupSummary(_activeProjectGroup, Parts.Count, Result?.Sheets.Count ?? 0, NotifyProjectSummary);

    private void NotifyProjectSummary()
    {
        OnPropertyChanged(nameof(ProjectGroupCount));
        OnPropertyChanged(nameof(ProjectPartCount));
        OnPropertyChanged(nameof(ProjectSheetCount));
    }

    private void Fail(string msg)
    {
        Status = msg;
        Messages.Add(msg);
        Toasts.Show(ToastKind.Error, "Có lỗi", msg);
        _dialogs.ShowMessage(msg);
    }
}
