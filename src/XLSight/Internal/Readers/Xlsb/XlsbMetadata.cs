namespace XLSight.Internal.Readers.Xlsb;

internal sealed class XlsbMetadata
{
    internal XlsbMetadata(
        IReadOnlyList<XlsbSheetInfo> sheets,
        bool usesDate1904,
        IReadOnlyList<XlsbDefinedNameInfo>? definedNames = null,
        IReadOnlyList<XlsbExternSheetInfo>? externSheets = null)
    {
        Sheets = sheets;
        UsesDate1904 = usesDate1904;
        DefinedNames = definedNames ?? [];
        FormulaContext = new XlsbFormulaContext(sheets, externSheets ?? []);
    }

    internal IReadOnlyList<XlsbSheetInfo> Sheets { get; }
    internal bool UsesDate1904 { get; }
    internal IReadOnlyList<XlsbDefinedNameInfo> DefinedNames { get; }
    internal XlsbFormulaContext FormulaContext { get; }
}
