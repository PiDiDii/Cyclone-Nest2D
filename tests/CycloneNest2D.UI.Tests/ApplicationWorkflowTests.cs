using System.Globalization;
using System.Text;
using CycloneNest2D.Core;
using CycloneNest2D.UI;
using Xunit;

namespace CycloneNest2D.UI.Tests;

public sealed class ApplicationWorkflowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "n2d_workflow_" + Guid.NewGuid().ToString("N"));

    public ApplicationWorkflowTests() => Directory.CreateDirectory(_directory);

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task DxfCanBeNestedEditedSavedReopenedAndExported()
    {
        string sourcePath = Path.Combine(_directory, "Part.dxf");
        File.WriteAllText(sourcePath, CreateDxfRectangle(60, 30), new UTF8Encoding(false));

        PartItem part = PartItem.FromDxf(sourcePath);
        var settings = new NestSettings
        {
            SheetWidth = 200,
            SheetHeight = 120,
            Margin = 5,
            Spacing = 3,
            MaxSeconds = 2,
            TargetUtilizationPercent = 0
        };
        var stock = new SheetPreset("SS400 2mm", 200, 120, "SS400", 2);
        settings.SheetPresets.Add(stock);
        part.AssignPreset(stock);

        NestRun run = await NestingService.RunAsync(
            new[] { part.ToDefinition() }, settings, null, CancellationToken.None);
        Assert.Empty(run.Errors);
        SheetResult sheet = Assert.Single(run.Result.Sheets);
        Assert.Single(sheet.Placements);

        var placementService = new ManualPlacementService();
        double originalX = sheet.Placements[0].X;
        Assert.True(placementService.TryMoveSelected(sheet, run.Sheet, new[] { 0 }, 1, 0));
        Assert.Equal(originalX + 1, sheet.Placements[0].X, 6);
        Assert.Empty(Validator.Check(run.Result, run.Sheet));

        string workspacePath = Path.Combine(_directory, "job.nest2d");
        ProjectStore.SaveWorkspace(workspacePath, settings, new[] { part }, run.Result, run.Sheet,
            isStale: false, isEdited: true, run.Elapsed, DateTime.UtcNow);

        var reopenedSettings = new NestSettings();
        var reopened = ProjectStore.LoadWorkspace(workspacePath, reopenedSettings, new List<string>());
        Assert.Single(reopened.Parts);
        Assert.Equal("SS400", reopened.Parts[0].Material);
        Assert.Contains(reopenedSettings.SheetPresets, preset => preset.Material == "SS400" && preset.Thickness == 2);
        Assert.True(reopened.IsEdited);
        Assert.Equal(sheet.Placements[0].X, Assert.Single(Assert.Single(reopened.Result!.Sheets).Placements).X, 6);

        string exportPath = Path.Combine(_directory, "export.dxf");
        var exportedFiles = ExportService.ExportSheets(
            reopened.Result, reopened.ResultSheet!, exportPath,
            new DxfExportOptions { AddPartLabels = true, OutputSheets = true });
        string exportedPath = Assert.Single(exportedFiles);
        DxfPart exported = DxfReader.Read(exportedPath, new DxfReadOptions
        {
            IncludeLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CUT", "PARTS", "SHEET" }
        });
        Assert.True(exported.Width > 0);
        Assert.True(exported.Height > 0);
    }

    private static string CreateDxfRectangle(double width, double height)
    {
        string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        string Line(double x1, double y1, double x2, double y2) =>
            $"0\nLINE\n8\nCUT\n10\n{F(x1)}\n20\n{F(y1)}\n30\n0\n11\n{F(x2)}\n21\n{F(y2)}\n31\n0\n";
        string entities = Line(0, 0, width, 0) + Line(width, 0, width, height) +
                          Line(width, height, 0, height) + Line(0, height, 0, 0);
        return "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n" +
               "0\nSECTION\n2\nENTITIES\n" + entities + "0\nENDSEC\n0\nEOF\n";
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
