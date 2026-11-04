using System.Text.Json;
using CycloneNest2D.Core;
using CycloneNest2D.UI;
using Xunit;

namespace CycloneNest2D.UI.Tests;

public sealed class ProjectFormatMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "n2d_migration_" + Guid.NewGuid().ToString("N"));

    public ProjectFormatMigrationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    [Trait("Category", "ProjectMigration")]
    public void VersionOneLegacyWorkspaceLoadsAndSavesAsCurrentVersion()
    {
        string path = Path.Combine(_directory, "legacy.nest2d");
        File.WriteAllText(path, """
            {
              "Version": 1,
              "Parts": [
                { "Kind": "Rect", "Name": "Legacy", "Width": 120, "Height": 60, "Quantity": 2 }
              ]
            }
            """);

        var settings = new NestSettings();
        var snapshot = ProjectStore.LoadWorkspace(path, settings, new List<string>());

        Assert.Single(snapshot.Parts);
        Assert.Equal("Legacy", snapshot.Parts[0].Name);
        Assert.Equal(2, snapshot.Parts[0].Quantity);

        ProjectStore.SaveWorkspace(path, settings, snapshot.Parts, snapshot.Result, snapshot.ResultSheet,
            snapshot.IsStale, snapshot.IsEdited, snapshot.LastNestDuration, snapshot.LastNestAt);

        using var saved = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(ProjectStore.CurrentFormatVersion,
            saved.RootElement.GetProperty("Version").GetInt32());
    }

    [Fact]
    [Trait("Category", "ProjectMigration")]
    public void VersionTwoWorkspaceAndProjectIndexLoadAndUpgrade()
    {
        string workspacePath = Path.Combine(_directory, "group.nest2d");
        File.WriteAllText(workspacePath, JsonSerializer.Serialize(new ProjectStore.ProjectDto
        {
            Kind = ProjectStore.GroupKind,
            Version = 2,
            Parts = { new ProjectStore.PartDto { Kind = "Rect", Name = "V2", Width = 90, Height = 40 } }
        }));

        var workspace = ProjectStore.LoadWorkspace(workspacePath, new NestSettings(), new List<string>());
        Assert.Equal("V2", Assert.Single(workspace.Parts).Name);

        string indexPath = Path.Combine(_directory, "project.nest2d");
        File.WriteAllText(indexPath, JsonSerializer.Serialize(new ProjectStore.ProjectDto
        {
            Kind = ProjectStore.ProjectKind,
            Version = 2,
            Groups = { new ProjectStore.GroupReferenceDto { Id = "one", Name = "Nhóm 1", Path = "group.nest2d" } }
        }));

        Assert.Equal("Nhóm 1", Assert.Single(ProjectStore.LoadProjectIndex(indexPath)).Name);
        ProjectStore.SaveProjectIndex(indexPath, ProjectStore.LoadProjectIndex(indexPath));
        using var saved = JsonDocument.Parse(File.ReadAllText(indexPath));
        Assert.Equal(ProjectStore.CurrentFormatVersion,
            saved.RootElement.GetProperty("Version").GetInt32());
    }

    [Fact]
    [Trait("Category", "ProjectMigration")]
    public void WorkspaceWithoutSavedSheetPresetsRecoversInventoryFromNestingResult()
    {
        string path = Path.Combine(_directory, "legacy-inventory.nest2d");
        File.WriteAllText(path, JsonSerializer.Serialize(new ProjectStore.ProjectDto
        {
            Kind = ProjectStore.GroupKind,
            Version = 3,
            Settings = """{"SheetWidth":1800,"SheetHeight":1000,"Direction":"FillWidth","SearchMode":"Balanced"}""",
            Parts =
            {
                new ProjectStore.PartDto
                {
                    Kind = "Rect",
                    Name = "Vỏ",
                    Material = "Tole ZAM",
                    Thickness = 1.2,
                    Width = 120,
                    Height = 60
                }
            },
            Result = new ProjectStore.NestResultDto
            {
                Sheets =
                {
                    new ProjectStore.SheetResultDto
                    {
                        Index = 1,
                        Width = 3000,
                        Height = 1219,
                        Material = "Tole ZAM",
                        Thickness = 1.2,
                        StockName = "ZAM 1.2"
                    }
                }
            }
        }));

        var settings = new NestSettings();
        ProjectStore.LoadWorkspace(path, settings, new List<string>());

        var recovered = Assert.Single(settings.SheetPresets, preset =>
            preset.Material == "Tole ZAM" && preset.Thickness == 1.2);
        Assert.Equal("ZAM 1.2", recovered.Name);
        Assert.Equal(3000, recovered.Width);
        Assert.Equal(1219, recovered.Height);
    }

    [Theory]
    [InlineData("""{"Kind":"CycloneNest2D.Group","Version":3,"Parts":null}""", "danh sách chi tiết")]
    [InlineData("""{"Kind":"CycloneNest2D.Project","Version":3,"Groups":null}""", "danh sách nhóm")]
    [InlineData("""{"Kind":"Unknown","Version":3}""", "Loại dữ liệu")]
    [Trait("Category", "ProjectMigration")]
    public void InvalidProjectShapeIsRejectedWithActionableError(string json, string expectedMessage)
    {
        string path = Path.Combine(_directory, "invalid.nest2d");
        File.WriteAllText(path, json);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
        {
            if (json.Contains(ProjectStore.ProjectKind, StringComparison.Ordinal))
                ProjectStore.LoadProjectIndex(path);
            else
                ProjectStore.LoadWorkspace(path, new NestSettings(), new List<string>());
        });

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
