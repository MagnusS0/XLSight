namespace XLSight.Worksheets;

internal readonly struct ParsedCell
{
    internal readonly int Row;           // 1-based row index
    internal readonly int Column;        // 1-based column index
    internal readonly int StyleIndex;    // s attribute — for date detection
    internal readonly CellDataKind DataKind;
    internal readonly ReadOnlyMemory<char> RawValue;   // <v> content, backed by rented buffer
    internal readonly string? InlineString;             // <is> content (already decoded)
    internal readonly string? FormulaText;              // <f> content

    internal ParsedCell(
        int row, int column, int styleIndex, CellDataKind dataKind,
        ReadOnlyMemory<char> rawValue, string? inlineString, string? formulaText)
    {
        Row = row;
        Column = column;
        StyleIndex = styleIndex;
        DataKind = dataKind;
        RawValue = rawValue;
        InlineString = inlineString;
        FormulaText = formulaText;
    }
}
