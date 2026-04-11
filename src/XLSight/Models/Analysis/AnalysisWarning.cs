namespace XLSight.Analysis;

/// <summary>Represents a non-fatal issue or caveat surfaced by workbook analysis.</summary>
public sealed class AnalysisWarning
{
    /// <summary>Gets a stable machine-readable warning code.</summary>
    public required string Code { get; init; }

    /// <summary>Gets the human-readable warning message.</summary>
    public required string Message { get; init; }
}
