using System.Collections.ObjectModel;

namespace CycloneNest2D.UI;

public enum ToastKind { Info, Success, Warning, Error }

/// <summary>Một thông báo nhanh ở góc phải dưới. Dữ liệu hiển thị (màu, icon) tính sẵn để XAML chỉ cần binding.</summary>
public sealed class ToastItem
{
    private readonly ToastService _owner;

    internal ToastItem(ToastService owner, ToastKind kind, string title, string text)
    {
        _owner = owner;
        Kind = kind;
        Title = title;
        Text = text;
        DismissCommand = new RelayCommand(() => _owner.Dismiss(this));
    }

    public ToastKind Kind { get; }
    public string Title { get; }
    public string Text { get; }
    public RelayCommand DismissCommand { get; }
    public string AccentHex => Kind switch { ToastKind.Success => "#2E9E6B", ToastKind.Warning => "#E8920C", ToastKind.Error => "#D64545", _ => "#2185D5" };
    public string TintHex => Kind switch { ToastKind.Success => "#EAF7F0", ToastKind.Warning => "#FFF5E5", ToastKind.Error => "#FDECEC", _ => "#EAF3FC" };
    /// <summary>Hình vector (lưới 24x24, nét liền) cho từng loại.</summary>
    public string IconData => Kind switch
    {
        ToastKind.Success => "M5,12.5 L10,17.5 L19,7",
        ToastKind.Warning => "M12,4 L21,20 H3 Z M12,10 V14 M12,17 V17.5",
        ToastKind.Error => "M6,6 L18,18 M18,6 L6,18",
        _ => "M12,11 V17 M12,7 V7.5",
    };
}

/// <summary>Hàng đợi thông báo nhanh: tự tắt sau vài giây, tối đa vài thẻ cùng lúc.</summary>
public sealed class ToastService
{
    private readonly SynchronizationContext? _sync = SynchronizationContext.Current;
    public const int MaxVisible = 4;

    public ObservableCollection<ToastItem> Items { get; } = new();

    /// <summary>Tắt tự động bằng false (dùng trong test để không có luồng nền).</summary>
    public bool AutoDismiss { get; set; } = true;

    public ToastItem Show(ToastKind kind, string title, string text = "", double seconds = 0)
    {
        if (seconds <= 0) seconds = kind switch { ToastKind.Error => 4, ToastKind.Warning => 2.5, _ => 2 };
        var item = new ToastItem(this, kind, title, text);
        Run(() =>
        {
            Items.Add(item);
            while (Items.Count > MaxVisible) Items.RemoveAt(0);
        });
        if (AutoDismiss)
            Task.Delay(TimeSpan.FromSeconds(seconds)).ContinueWith(_ => Dismiss(item), TaskScheduler.Default);
        return item;
    }

    public void Dismiss(ToastItem item) => Run(() => Items.Remove(item));

    public void Clear() => Run(Items.Clear);

    private void Run(Action a)
    {
        if (_sync != null && SynchronizationContext.Current != _sync) _sync.Post(_ => a(), null);
        else a();
    }
}
