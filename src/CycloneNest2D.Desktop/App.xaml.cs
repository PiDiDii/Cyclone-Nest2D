using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using CycloneNest2D.UI;

namespace CycloneNest2D.Desktop;

public partial class App : Application
{
    private bool _dispatcherExceptionReported;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        try
        {
            // Mặc định WPF dùng en-US để hiển thị/nhập số. Dùng định dạng số của Windows (dấu phẩy thập phân kiểu Việt Nam...).
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var settings = new NestSettings();
            try
            {
                settings.LoadFrom(NestSettings.DefaultFilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                MessageBox.Show(
                    $"Không thể nạp thiết lập đã lưu. Ứng dụng sẽ dùng thiết lập mặc định.\n\n{ex.Message}",
                    "Không thể đọc thiết lập", MessageBoxButton.OK, MessageBoxImage.Warning);
                settings.ResetDefaults();
            }

            var window = new MainWindow(settings);
            MainWindow = window;
            window.Show();
            window.Activate();

            // Mở file .nest2d / .dxf truyền vào (nhấp đúp file hoặc kéo thả lên biểu tượng chương trình)
            if (e.Args.Length > 0) window.OpenFromCommandLine(e.Args);
        }
        catch (Exception ex)
        {
            string logPath = LogException(ex);
            MessageBox.Show(
                $"Cyclone Nest2D không thể khởi động.\n\n{ex.Message}\n\nChi tiết lỗi: {logPath}",
                "Lỗi khởi động Cyclone Nest2D", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (_dispatcherExceptionReported) return;
        _dispatcherExceptionReported = true;

        string logPath = LogException(e.Exception);
        MessageBox.Show(
            $"Đã xảy ra lỗi khi chạy Cyclone Nest2D.\n\n{e.Exception.Message}\n\nChi tiết lỗi: {logPath}",
            "Lỗi Cyclone Nest2D", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }

    private static string LogException(Exception ex)
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nesting2D");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "startup-error.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}] {ex}\n{new string('-', 80)}\n");
            return path;
        }
        catch
        {
            return "không thể ghi file log trong AppData\\Roaming\\Nesting2D";
        }
    }
}
