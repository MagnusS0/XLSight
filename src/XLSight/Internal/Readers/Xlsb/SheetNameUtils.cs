namespace XLSight.Internal.Readers.Xlsb;

internal static class SheetNameUtils
{
    internal static string QuoteSheetName(string sheetName)
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
