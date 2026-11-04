using CycloneNest2D.Core;

namespace CycloneNest2D.UI;

public sealed class ExportCoordinator
{
    public List<string> ValidateResult(NestResult result, Func<SheetResult, SheetDefinition> definitionFor)
    {
        var errors = new List<string>();
        foreach (var sheet in result.Sheets)
        {
            var single = new NestResult();
            single.Sheets.Add(sheet);
            errors.AddRange(Validator.Check(single, definitionFor(sheet)));
        }
        return errors;
    }

    public List<string> ExportSheets(NestResult result, SheetDefinition definition, string destination, DxfExportOptions options)
        => ExportService.ExportSheets(result, definition, destination, options);

    public List<string> ExportPatterns(IReadOnlyList<NestingPatternItem> patterns, SheetDefinition definition,
        string destination, DxfExportOptions options)
        => ExportService.ExportPatterns(patterns, definition, destination, options);
}
