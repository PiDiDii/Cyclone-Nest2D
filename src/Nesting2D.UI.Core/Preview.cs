using Nesting2D.Core;

namespace Nesting2D.UI;

/// <summary>Một chi tiết đã đặt, ở dạng vẽ được: toạ độ mm trên tấm, gốc dưới-trái (view phải lật trục Y).</summary>
public sealed class PreviewPart
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public int ColorIndex { get; init; }
    public bool IsRotated { get; init; }
    /// <summary>Đa giác kín: tô theo quy tắc even-odd (lỗ tự trống).</summary>
    public IReadOnlyList<(double X, double Y)[]> Loops { get; init; } = Array.Empty<(double, double)[]>();
    /// <summary>Có dùng hình thật không (false = chỉ hộp bao chữ nhật).</summary>
    public bool HasShape { get; init; }

    internal LoopSet? MaterialTest { get; init; }

    /// <summary>Điểm (mm trên tấm) nằm trên vật liệu của chi tiết (lỗ không tính).</summary>
    public bool HitsMaterial(double x, double y)
    {
        if (x < X || x > X + Width || y < Y || y > Y + Height) return false;
        return MaterialTest == null || MaterialTest.ContainsMaterial(x, y);
    }
}

public sealed class PreviewScene
{
    public double SheetWidth { get; init; }
    public double SheetHeight { get; init; }
    public double Margin { get; init; }
    public double Spacing { get; init; }
    public IReadOnlyList<PreviewPart> Parts { get; init; } = Array.Empty<PreviewPart>();
    public string Title { get; init; } = "";

    /// <summary>Chi tiết ở trên cùng tại điểm (mm), hoặc -1.</summary>
    public int HitTest(double x, double y)
    {
        for (int i = Parts.Count - 1; i >= 0; i--)
            if (Parts[i].HitsMaterial(x, y)) return Parts[i].Index;
        return -1;
    }

    public static readonly string[] Palette =
    {
        "#4E79A7", "#F28E2B", "#59A14F", "#B07AA1", "#76B7B2", "#EDC948",
        "#FF9DA7", "#9C755F", "#8CD17D", "#86BCB6", "#D37295", "#A0CBE8",
    };

    public static int ColorIndexForName(string name)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char ch in name.ToUpperInvariant()) { hash ^= ch; hash *= 16777619; }
            return (int)(hash % (uint)Palette.Length);
        }
    }

    public static PreviewScene Empty(SheetDefinition def) => new()
    {
        SheetWidth = def.Width, SheetHeight = def.Height, Margin = def.Margin, Spacing = def.Spacing,
    };
}

public static class PreviewBuilder
{
    public static PreviewScene Build(SheetResult sheet, SheetDefinition def)
    {
        var parts = new List<PreviewPart>(sheet.Placements.Count);

        for (int i = 0; i < sheet.Placements.Count; i++)
        {
            var p = sheet.Placements[i];
            int ci = PreviewScene.ColorIndexForName(p.PartName);

            LoopSet ls;
            bool shaped = p.Shape != null;
            if (shaped)
            {
                var built = ShapeOps.Build(p.Shape!.Place(p.X, p.Y, p.Rotated), p.Width, p.Height);
                ls = built.Reliable && built.Area > 0 ? built : Rect(p);
                shaped = ReferenceEquals(ls, built);
            }
            else ls = Rect(p);

            var loops = ls.Loops.Select(l => Enumerable.Range(0, l.X.Length).Select(k => (l.X[k], l.Y[k])).ToArray()).ToList();
            parts.Add(new PreviewPart
            {
                Index = i, Name = p.PartName, Label = $"{p.PartName} #{p.Instance}",
                X = p.X, Y = p.Y, Width = p.Width, Height = p.Height,
                ColorIndex = ci, IsRotated = p.IsRotated,
                Loops = loops, HasShape = shaped, MaterialTest = ls,
            });
        }

        return new PreviewScene
        {
            SheetWidth = def.Width, SheetHeight = def.Height, Margin = def.Margin, Spacing = def.Spacing,
            Parts = parts,
            Title = $"Tấm {sheet.Index}: {sheet.Placements.Count} chi tiết, hiệu suất {sheet.Utilization:P1}",
        };
    }

    private static LoopSet Rect(Placement p) =>
        ShapeOps.Build(ShapeOps.RectShape(p.PartName, p.Width, p.Height).Place(p.X, p.Y, false), p.Width, p.Height);
}
