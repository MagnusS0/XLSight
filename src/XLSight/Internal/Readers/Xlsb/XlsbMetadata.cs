#pragma warning disable MA0048 // Keep the small XLSB metadata model together.

namespace XLSight.Internal.Readers.Xlsb;

internal sealed class XlsbMetadata(
    IReadOnlyList<XlsbSheetInfo> sheets,
    bool usesDate1904,
    IReadOnlyList<XlsbDefinedNameInfo>? definedNames = null,
    IReadOnlyList<XlsbExternSheetInfo>? externSheets = null)
{
    internal IReadOnlyList<XlsbSheetInfo> Sheets { get; } = sheets;
    internal bool UsesDate1904 { get; } = usesDate1904;
    internal IReadOnlyList<XlsbDefinedNameInfo> DefinedNames { get; } = definedNames ?? [];
    internal XlsbFormulaContext FormulaContext { get; } = new(sheets, externSheets ?? []);
}

internal readonly record struct XlsbSheetInfo(string Name, string Path);

internal sealed record XlsbDefinedNameInfo(string Name, string Reference, string? ScopeSheetName);

internal sealed record XlsbExternSheetInfo(uint ExternalLink, int FirstSheet, int LastSheet);

internal sealed class XlsbFormulaContext(
    IReadOnlyList<XlsbSheetInfo> sheets,
    IReadOnlyList<XlsbExternSheetInfo> externSheets)
{
    internal bool TryResolveSheet(
        int externSheetIndex,
        out string sheetName,
        out string formulaPrefix)
    {
        sheetName = string.Empty;
        formulaPrefix = string.Empty;
        if ((uint)externSheetIndex >= (uint)externSheets.Count)
        {
            return false;
        }

        XlsbExternSheetInfo externSheet = externSheets[externSheetIndex];
        if (externSheet.ExternalLink != 0 ||
            externSheet.FirstSheet < 0 ||
            externSheet.LastSheet < externSheet.FirstSheet ||
            externSheet.LastSheet >= sheets.Count)
        {
            return false;
        }

        string firstSheet = sheets[externSheet.FirstSheet].Name;
        if (externSheet.FirstSheet == externSheet.LastSheet)
        {
            sheetName = firstSheet;
            formulaPrefix = QuoteSheetName(firstSheet);
            return true;
        }

        string lastSheet = sheets[externSheet.LastSheet].Name;
        sheetName = $"{firstSheet}:{lastSheet}";
        formulaPrefix = $"{QuoteSheetName(firstSheet)}:{QuoteSheetName(lastSheet)}";
        return true;
    }

    private static string QuoteSheetName(string sheetName) =>
        sheetName.All(static ch => char.IsLetterOrDigit(ch) || ch == '_')
            ? sheetName
            : $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'";
}

#pragma warning restore MA0048
