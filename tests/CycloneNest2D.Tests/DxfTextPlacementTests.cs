using CycloneNest2D.Core;
using Xunit;

namespace CycloneNest2D.Tests;

public sealed class DxfTextPlacementTests
{
    [Fact]
    public void TextFollowsAutomaticallyRotatedPartFromDxfImportThroughExport()
    {
        string sourcePath = Path.Combine(Path.GetTempPath(), $"text-source-{Guid.NewGuid():N}.dxf");
        string outputPath = Path.Combine(Path.GetTempPath(), $"text-output-{Guid.NewGuid():N}.dxf");
        const string source = """
            0
            SECTION
            2
            HEADER
            9
            $ACADVER
            1
            AC1027
            0
            ENDSEC
            0
            SECTION
            2
            ENTITIES
            0
            LINE
            8
            0
            10
            10
            20
            20
            11
            110
            21
            20
            0
            LINE
            8
            0
            10
            110
            20
            20
            11
            110
            21
            60
            0
            LINE
            8
            0
            10
            110
            20
            60
            11
            10
            21
            60
            0
            LINE
            8
            0
            10
            10
            20
            60
            11
            10
            21
            20
            0
            TEXT
            8
            0
            10
            30
            20
            25
            40
            4
            1
            Part label
            0
            ENDSEC
            0
            EOF
            """;

        try
        {
            File.WriteAllText(sourcePath, source.Replace("\n", "\r\n"));
            var part = DxfReader.Read(sourcePath);
            var sheet = new SheetDefinition(50, 110, 0, 0);
            var result = NestEngine.Run(new[] { part.ToDefinition() }, sheet, new NestOptions
            {
                AllowRotation = true,
                RotationStep = 90,
                MaxSeconds = 5,
                TargetUtilizationPercent = 0
            });

            Assert.Empty(result.Unplaced);
            var placement = Assert.Single(Assert.Single(result.Sheets).Placements);
            Assert.Equal(90, placement.AngleDeg);

            DxfWriter.Write(result.Sheets[0], sheet, outputPath, new DxfExportOptions
            {
                OutputSheets = false,
                TargetVersion = DxfTargetVersion.AutoCAD2000
            });

            var exportedText = Assert.Single(DxfReader.ReadRaw(outputPath).Texts);
            Assert.Equal(placement.X + 35, exportedText.X, 6);
            Assert.Equal(placement.Y + 20, exportedText.Y, 6);
            Assert.Equal(90, exportedText.Rotation, 6);
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public void R12MTextConversionPreservesTopLeftAttachmentAfterPlacementRotation()
    {
        var mtext = new DxfTextEntity("MTEXT", "NOTES", 10, 20, "Line 1\\PLine 2",
            4, 0, 20, 1, "STANDARD");
        var outline = ShapeOps.RectShape("Part", 100, 50);
        var shape = new DxfShape(outline.Name, outline.Segments, outline.UnitScale, outline.Layers,
            outline.Warnings, outline.ApproximatedEntities, texts: new[] { mtext });
        var sheet = new SheetResult { Index = 1, Width = 200, Height = 200 };
        sheet.Placements.Add(new Placement("Part", 1, 40, 60, 50, 100, true, shape));
        string outputPath = Path.Combine(Path.GetTempPath(), $"text-attachment-{Guid.NewGuid():N}.dxf");

        try
        {
            DxfWriter.Write(sheet, new SheetDefinition(200, 200, 0, 0), outputPath, new DxfExportOptions
            {
                OutputSheets = false,
                TargetVersion = DxfTargetVersion.R12
            });

            var texts = DxfReader.ReadRaw(outputPath).Texts;
            Assert.Equal(2, texts.Count);
            Assert.All(texts, text =>
            {
                Assert.Equal("TEXT", text.Type);
                Assert.Equal(0, text.HorizontalAlignment);
                Assert.Equal(3, text.VerticalAlignment);
                Assert.Equal(90, text.Rotation, 8);
                Assert.Equal(70, text.AnchorY, 8);
            });
            Assert.Equal(70, texts[0].AnchorX, 8);
            Assert.Equal(76, texts[1].AnchorX, 8);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

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
