using System.Windows;
using Microsoft.Win32;
using CycloneNest2D.UI;

namespace CycloneNest2D.Desktop;

public sealed class WpfDialogService : IDialogService
{
    private readonly Window _owner;
    public WpfDialogService(Window owner) => _owner = owner;

    public string[] PickOpenFiles(string title, string filter)
    {
        var d = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true, CheckFileExists = true };
        return d.ShowDialog(_owner) == true ? d.FileNames : Array.Empty<string>();
    }

    public string? PickOpenFile(string title, string filter)
    {
        var d = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return d.ShowDialog(_owner) == true ? d.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var d = new OpenFolderDialog { Title = title };
        return d.ShowDialog(_owner) == true ? d.FolderName : null;
    }

    public string? PickSaveFile(string title, string filter, string defaultName)
    {
        var d = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName, OverwritePrompt = true };
        return d.ShowDialog(_owner) == true ? d.FileName : null;
    }

    public void ShowMessage(string message) =>
        MessageBox.Show(_owner, message, "Cyclone Nest2D", MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string message) =>
        ConfirmationDialog.Confirm(_owner, "Xác nhận thao tác", message, "Tiếp tục");
}
