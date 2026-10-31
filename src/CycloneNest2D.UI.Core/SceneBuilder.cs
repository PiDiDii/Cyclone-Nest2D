using CycloneNest2D.Core;

namespace CycloneNest2D.UI;

public sealed class SceneBuilder
{
    public PreviewScene Build(SheetResult? currentSheet, SheetDefinition? resultSheet, NestSettings settings)
    {
        if (currentSheet == null || resultSheet == null)
            return PreviewScene.Empty(settings.ToSheet());

        return PreviewBuilder.Build(currentSheet, DefinitionFor(currentSheet, resultSheet));
    }

    public SheetDefinition DefinitionFor(SheetResult sheet, SheetDefinition? resultSheet, NestSettings? settings = null)
    {
        var defaults = resultSheet ?? (settings ?? new NestSettings()).ToSheet();
        return new SheetDefinition(sheet.Width, sheet.Height, defaults.Margin, defaults.Spacing);
    }
}
