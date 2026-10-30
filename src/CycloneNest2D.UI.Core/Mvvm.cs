using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CycloneNest2D.UI;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _run;
    private readonly Func<object?, bool>? _can;

    public RelayCommand(Action run, Func<bool>? can = null)
        : this(_ => run(), can == null ? null : _ => can()) { }

    public RelayCommand(Action<object?> run, Func<object?, bool>? can = null)
    {
        _run = run;
        _can = can;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _run(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Lệnh bất đồng bộ: tự khóa trong lúc đang chạy, lỗi không bị nuốt mà chuyển cho onError.</summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _run;
    private readonly Func<bool>? _can;
    private readonly Action<Exception>? _onError;
    private bool _running;

    public AsyncRelayCommand(Func<Task> run, Func<bool>? can = null, Action<Exception>? onError = null)
    {
        _run = run;
        _can = can;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running && (_can?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        RaiseCanExecuteChanged();
        try { await _run(); }
        catch (Exception ex)
        {
            if (_onError != null) _onError(ex);
            else throw;
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Các hộp thoại cần hệ điều hành; WPF cài đặt thật, test dùng bản giả.</summary>
public interface IDialogService
{
    string[] PickOpenFiles(string title, string filter);
    string? PickFolder(string title);
    string? PickSaveFile(string title, string filter, string defaultName);
    string? PickOpenFile(string title, string filter);
    void ShowMessage(string message);
    bool Confirm(string message);
}
