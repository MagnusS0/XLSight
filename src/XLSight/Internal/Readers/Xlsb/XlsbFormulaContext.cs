namespace XLSight.Internal.Readers.Xlsb;

internal sealed class XlsbFormulaContext
{
    private readonly IReadOnlyList<XlsbSheetInfo> _sheets;
    private readonly IReadOnlyList<XlsbExternSheetInfo> _externSheets;

    internal XlsbFormulaContext(
        IReadOnlyList<XlsbSheetInfo> sheets,
        IReadOnlyList<XlsbExternSheetInfo> externSheets)
    {
        _sheets = sheets;
        _externSheets = externSheets;
    }

    internal bool TryResolveSheet(
        int externSheetIndex,
        out string sheetName,
        out string formulaPrefix)
    {
        sheetName = string.Empty;
        formulaPrefix = string.Empty;
        if ((uint)externSheetIndex >= (uint)_externSheets.Count)
        {
            return false;
        }

        XlsbExternSheetInfo externSheet = _externSheets[externSheetIndex];
        if (externSheet.ExternalLink != 0 ||
            externSheet.FirstSheet < 0 ||
            externSheet.LastSheet < externSheet.FirstSheet ||
            externSheet.LastSheet >= _sheets.Count)
        {
            return false;
        }

        string firstSheet = _sheets[externSheet.FirstSheet].Name;
        if (externSheet.FirstSheet == externSheet.LastSheet)
        {
            sheetName = firstSheet;
            formulaPrefix = QuoteSheetName(firstSheet);
            return true;
        }

        string lastSheet = _sheets[externSheet.LastSheet].Name;
        sheetName = $"{firstSheet}:{lastSheet}";
        formulaPrefix = $"{QuoteSheetName(firstSheet)}:{QuoteSheetName(lastSheet)}";
        return true;
    }

    private static string QuoteSheetName(string sheetName)
    {
        foreach (char ch in sheetName)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_')
            {
                return $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'";
            }
        }

        return sheetName;
    }
}
