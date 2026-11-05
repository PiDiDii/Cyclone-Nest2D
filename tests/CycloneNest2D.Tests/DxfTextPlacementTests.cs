using CycloneNest2D.Core;
using Xunit;

namespace CycloneNest2D.Tests;

public sealed class DxfTextPlacementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExportedTextFollowsRotatedPlacement(bool exportPartsAsBlocks)
    {
        var text = new DxfTextEntity(
            "TEXT", "NOTES", 30, 40, "Aligned", 4, 25, 0, 0, "STANDARD",
            35, 45, HorizontalAlignment: 1, VerticalAlignment: 2);
        var mtext = new DxfTextEntity(
            "MTEXT", "NOTES", 50, 50, "MText", 4, 10, 20, 1, "STANDARD");
        var shape = new DxfShape(
            "Part",
            new Seg[]
            {
                new LineSeg(10, 20, 110, 20),
                new LineSeg(110, 20, 110, 70),
                new LineSeg(110, 70, 10, 70),
                new LineSeg(10, 70, 10, 20)
            },
            1,
            new Dictionary<string, int>(),
            Array.Empty<string>(),
            0,
            texts: new[] { text, mtext });
        var sheet = new SheetResult { Index = 1, Width = 200, Height = 200 };
        sheet.Placements.Add(new Placement("Part", 1, 40, 60, 50, 100, true, shape));
        string outputPath = Path.Combine(Path.GetTempPath(), $"text-placement-{Guid.NewGuid():N}.dxf");

        try
        {
            DxfExportWriter.Write(new[] { sheet }, outputPath, new DxfExportOptions
            {
                OutputSheets = false,
                ExportPartsAsBlocks = exportPartsAsBlocks,
                TargetVersion = DxfTargetVersion.AutoCAD2000
            });

            var texts = DxfReader.ReadRaw(outputPath).Texts;
            var aligned = Assert.Single(texts, item => item.Content == "Aligned");
            Assert.Equal(70, aligned.X, 8);
            Assert.Equal(80, aligned.Y, 8);
            Assert.Equal(65, aligned.AlignmentX!.Value, 8);
            Assert.Equal(85, aligned.AlignmentY!.Value, 8);
            Assert.Equal(115, aligned.Rotation, 8);

            var multiLine = Assert.Single(texts, item => item.Content == "MText");
            Assert.Equal(60, multiLine.X, 8);
            Assert.Equal(100, multiLine.Y, 8);
            Assert.Equal(100, multiLine.Rotation, 8);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}
