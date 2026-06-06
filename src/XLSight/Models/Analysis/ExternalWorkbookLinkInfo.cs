namespace XLSight.Analysis;

/// <summary>Describes a workbook referenced through an external-link package part.</summary>
public sealed class ExternalWorkbookLinkInfo
{
    /// <summary>Gets the relationship target for the external workbook.</summary>
    public required string Target { get; init; }

    /// <summary>Gets sheet names cached in the external-link part.</summary>
    public required IReadOnlyList<string> SheetNames { get; init; }

    /// <summary>Gets defined names cached in the external-link part.</summary>
    public required IReadOnlyList<string> DefinedNames { get; init; }
}
