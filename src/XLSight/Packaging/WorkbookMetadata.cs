namespace XLSight.Packaging;

internal sealed record WorkbookMetadata(
    IReadOnlyList<WorkbookMetadata.WorkbookSheetInfo> Sheets,
    IReadOnlyList<WorkbookMetadata.WorkbookNamedRange> NamedRanges,
    bool UsesDate1904,
    bool HasMacros)
{
    public sealed record WorkbookSheetInfo(string Name, string Path);

    public sealed record WorkbookNamedRange(string Name, string Reference, string? ScopeSheetName);
}
