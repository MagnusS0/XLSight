namespace XLSight.Internal.Sinks;

internal readonly ref struct FormulaReference
{
    private FormulaReference(
        ReadOnlySpan<byte> workbookUtf8,
        ReadOnlySpan<byte> sheetUtf8,
        string? workbook,
        string? sheet)
    {
        WorkbookUtf8 = workbookUtf8;
        SheetUtf8 = sheetUtf8;
        Workbook = workbook;
        Sheet = sheet;
    }

    internal ReadOnlySpan<byte> WorkbookUtf8 { get; }
    internal ReadOnlySpan<byte> SheetUtf8 { get; }
    internal string? Workbook { get; }
    internal string? Sheet { get; }
    internal bool IsUtf8 => Sheet is null;

    internal static FormulaReference FromUtf8(ReadOnlySpan<byte> workbook, ReadOnlySpan<byte> sheet) =>
        new(workbook, sheet, null, null);

    internal static FormulaReference FromText(string? workbook, string sheet) =>
        new([], [], workbook, sheet);
}
