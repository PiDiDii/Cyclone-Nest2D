using System.Text.Json;
using CycloneNest2D.UI;
using Xunit;

namespace CycloneNest2D.UI.Tests;

public sealed class SettingsPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "n2d_settings_" + Guid.NewGuid().ToString("N"));

    public SettingsPersistenceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    [Trait("Category", "SettingsPersistence")]
    public void InvalidJsonIsReportedWithoutChangingCurrentSettings()
    {
        string path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{");
        var settings = new NestSettings { SheetWidth = 1800 };

        Assert.Throws<JsonException>(() => settings.LoadFrom(path));

        Assert.Equal(1800, settings.SheetWidth);
        Assert.Null(settings.Validate());
    }

    [Fact]
    [Trait("Category", "SettingsPersistence")]
    public void InvalidValuesAreReportedWithoutPartiallyApplyingSettings()
    {
        string path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """
            {
              "SheetWidth": 1800,
              "SheetHeight": 1000,
              "Margin": -1,
              "Direction": "Unknown"
            }
            """);
        var settings = new NestSettings { SheetWidth = 1600 };

        Assert.Throws<InvalidDataException>(() => settings.LoadFrom(path));

        Assert.Equal(1600, settings.SheetWidth);
        Assert.Null(settings.Validate());
    }

    [Fact]
    [Trait("Category", "SettingsPersistence")]
    public void InvalidSheetPresetIsReportedWithoutChangingCurrentSettingsOrPresets()
    {
        string path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """
            {
              "SheetWidth": 1800,
              "SheetHeight": 1000,
              "Margin": 5,
              "Spacing": 3,
              "Direction": "FillWidth",
              "SheetPresets": [
                { "Name": "Tấm lỗi", "Width": 0, "Height": 1200, "Material": "Thép", "Thickness": 1.2 }
              ]
            }
            """);
        var settings = new NestSettings { SheetWidth = 1600 };
        settings.SheetPresets.Clear();
        settings.SheetPresets.Add(new SheetPreset("Tấm hiện có", 2400, 1200, "Thép", 1.2));

        var error = Assert.Throws<InvalidDataException>(() => settings.LoadFrom(path));

        Assert.Contains("Khổ tấm thứ 1", error.Message);
        Assert.Equal(1600, settings.SheetWidth);
        var preset = Assert.Single(settings.SheetPresets);
        Assert.Equal("Tấm hiện có", preset.Name);
    }

    [Fact]
    [Trait("Category", "SettingsPersistence")]
    public void MissingSettingsFileKeepsCurrentValues()
    {
        var settings = new NestSettings { SheetWidth = 1600 };

        settings.LoadFrom(Path.Combine(_directory, "missing.json"));

        Assert.Equal(1600, settings.SheetWidth);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
