using CycloneNest2D.Core;
using System.Text;
using Xunit;

namespace CycloneNest2D.Tests;

public sealed class DxfImportLimitsTests
{
    [Fact]
    public void RejectsFileLargerThanConfiguredLimitBeforeParsing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oversized-{Guid.NewGuid():N}.dxf");

        try
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                file.SetLength(DxfReader.MaxFileSizeBytes + 1);

            var exception = Assert.Throws<InvalidDataException>(() => DxfReader.ReadRaw(path));

            Assert.Contains("32 MB", exception.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RejectsInsertArrayWhoseInstanceCountExceedsConfiguredLimit()
    {
        string path = Path.Combine(Path.GetTempPath(), $"insert-limit-{Guid.NewGuid():N}.dxf");
        const string source = """
            0
            SECTION
            2
            BLOCKS
            0
            BLOCK
            8
            0
            2
            B1
            70
            0
            10
            0
            20
            0
            30
            0
            0
            LINE
            8
            0
            10
            0
            20
            0
            11
            1
            21
            0
            0
            ENDBLK
            8
            0
            0
            ENDSEC
            0
            SECTION
            2
            ENTITIES
            0
            INSERT
            8
            0
            2
            B1
            10
            0
            20
            0
            70
            50001
            71
            1
            0
            ENDSEC
            0
            EOF
            """;

        try
        {
            File.WriteAllText(path, source.Replace("\n", "\r\n"));

            var exception = Assert.Throws<InvalidDataException>(() => DxfReader.ReadRaw(path));

            Assert.Contains("instance INSERT", exception.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RejectsGroupCodePairsAboveConfiguredLimit()
    {
        string path = Path.Combine(Path.GetTempPath(), $"group-code-limit-{Guid.NewGuid():N}.dxf");

        try
        {
            using (var writer = new StreamWriter(path, false, Encoding.ASCII))
            {
                for (int i = 0; i <= DxfReader.MaxGroupCodePairs; i++)
                {
                    writer.WriteLine("999");
                    writer.WriteLine("ignored");
                }
            }

            var exception = Assert.Throws<InvalidDataException>(() => DxfReader.ReadRaw(path));

            Assert.Contains("cặp group code", exception.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RejectsTooManyLinesBeforeSplittingInput()
    {
        string path = Path.Combine(Path.GetTempPath(), $"line-limit-{Guid.NewGuid():N}.dxf");

        try
        {
            using (var writer = new StreamWriter(path, false, Encoding.ASCII))
            {
                for (int i = 0; i <= DxfReader.MaxLines; i++)
                    writer.WriteLine();
            }

            var exception = Assert.Throws<InvalidDataException>(() => DxfReader.ReadRaw(path));

            Assert.Contains("dòng", exception.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RejectsExpandedBlockEntityCountAboveConfiguredLimit()
    {
        string path = Path.Combine(Path.GetTempPath(), $"expanded-entity-limit-{Guid.NewGuid():N}.dxf");
        string blockEntities = string.Concat(Enumerable.Repeat("0\nUNKNOWN_ENTITY\n", 6));
        string source = $$"""
            0
            SECTION
            2
            BLOCKS
            0
            BLOCK
            8
            0
            2
            B1
            70
            0
            10
            0
            20
            0
            30
            0
            {{blockEntities.TrimEnd()}}
            0
            ENDBLK
            8
            0
            0
            ENDSEC
            0
            SECTION
            2
            ENTITIES
            0
            INSERT
            8
            0
            2
            B1
            10
            0
            20
            0
            70
            42000
            71
            1
            0
            ENDSEC
            0
            EOF
            """;

        try
        {
            File.WriteAllText(path, source.Replace("\n", "\r\n"));

            var exception = Assert.Throws<InvalidDataException>(() => DxfReader.ReadRaw(path));

            Assert.Contains("entity sau khi mở rộng", exception.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RejectsPolylineWithTooManyPoints()
    {
        string path = Path.Combine(Path.GetTempPath(), $"polyline-point-limit-{Guid.NewGuid():N}.dxf");

        try
        {
            using (var writer = new StreamWriter(path, false, Encoding.ASCII))
            {
                writer.WriteLine("0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n8\n0");
                for (int i = 0; i <= DxfReader.MaxPolylinePoints; i++)
                {
                    writer.WriteLine("10");
                    writer.WriteLine(i);
                    writer.WriteLine("20");
                    writer.WriteLine(i);
                }
                writer.WriteLine("0\nENDSEC\n0\nEOF");
            }

            var exception = Assert.Throws<InvalidDataException>(() => DxfReader.ReadRaw(path));

            Assert.Contains("điểm trên một polyline", exception.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RejectsSplineWithTooManyControlPoints()
    {
        string path = Path.Combine(Path.GetTempPath(), $"spline-point-limit-{Guid.NewGuid():N}.dxf");

        try
        {
            using (var writer = new StreamWriter(path, false, Encoding.ASCII))
            {
                writer.WriteLine("0\nSECTION\n2\nENTITIES\n0\nSPLINE\n8\n0\n71\n3");
                for (int i = 0; i <= DxfReader.MaxSplineControlPoints; i++)
                {
                    writer.WriteLine("10");
                    writer.WriteLine(i);
                    writer.WriteLine("20");
                    writer.WriteLine(i);
                }
                writer.WriteLine("0\nENDSEC\n0\nEOF");
            }

            var exception = Assert.Throws<InvalidDataException>(() => DxfReader.ReadRaw(path));

            Assert.Contains("điểm điều khiển", exception.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RejectsSplineAboveConfiguredDegree()
    {
        string path = Path.Combine(Path.GetTempPath(), $"spline-degree-limit-{Guid.NewGuid():N}.dxf");
        string source = $$"""
            0
            SECTION
            2
            ENTITIES
            0
            SPLINE
            8
            0
            71
            {{DxfReader.MaxSplineDegree + 1}}
            0
            ENDSEC
            0
            EOF
            """;

        try
        {
            File.WriteAllText(path, source.Replace("\n", "\r\n"));

            var exception = Assert.Throws<InvalidDataException>(() => DxfReader.ReadRaw(path));

            Assert.Contains("bậc", exception.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RejectsEllipseRequiringTooManyApproximationSegments()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ellipse-segment-limit-{Guid.NewGuid():N}.dxf");
        const string source = """
            0
            SECTION
            2
            ENTITIES
            0
            ELLIPSE
            8
            0
            10
            0
            20
            0
            11
            1
            21
            0
            40
            1
            41
            0
            42
            1000
            0
            ENDSEC
            0
            EOF
            """;

        try
        {
            File.WriteAllText(path, source.Replace("\n", "\r\n"));

            var exception = Assert.Throws<InvalidDataException>(() => DxfReader.ReadRaw(path));

            Assert.Contains("đoạn xấp xỉ", exception.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
