using System.IO;
using System.Text.Json;

namespace CycloneNest2D.Desktop;

internal sealed class ReportPreviewPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public NestingReportType ReportType { get; set; } = NestingReportType.SheetParameters;
    public NestingPageOrientation Orientation { get; set; } = NestingPageOrientation.Landscape;
    public NestingPageLayout Layout { get; set; } = NestingPageLayout.OnePerPage;
    public double ZoomPercent { get; set; } = 100;
    public ReportExportScope ExportScope { get; set; } = ReportExportScope.CurrentReport;
    public ReportPageSelection PageSelection { get; set; } = ReportPageSelection.AllPages;
    public int FromPage { get; set; } = 1;
    public int ToPage { get; set; } = 1;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nesting2D", "report-preview.json");

    public static ReportPreviewPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new ReportPreviewPreferences();
            var loaded = JsonSerializer.Deserialize<ReportPreviewPreferences>(File.ReadAllText(FilePath));
            if (loaded is null) return new ReportPreviewPreferences();
            if (!Enum.IsDefined(loaded.ReportType)) loaded.ReportType = NestingReportType.SheetParameters;
            if (!Enum.IsDefined(loaded.Orientation)) loaded.Orientation = NestingPageOrientation.Landscape;
            if (!Enum.IsDefined(loaded.Layout)) loaded.Layout = NestingPageLayout.OnePerPage;
            if (!Enum.IsDefined(loaded.ExportScope)) loaded.ExportScope = ReportExportScope.CurrentReport;
            if (!Enum.IsDefined(loaded.PageSelection)) loaded.PageSelection = ReportPageSelection.AllPages;
            loaded.ZoomPercent = Math.Clamp(double.IsFinite(loaded.ZoomPercent) ? loaded.ZoomPercent : 100, 10, 500);
            loaded.FromPage = Math.Max(1, loaded.FromPage);
            loaded.ToPage = Math.Max(1, loaded.ToPage);
            return loaded;
        }
        catch
        {
            return new ReportPreviewPreferences();
        }
    }

    public void Save()
    {
        try
        {
            string directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // A preference write failure must not prevent previewing or exporting a report.
        }
    }
}
