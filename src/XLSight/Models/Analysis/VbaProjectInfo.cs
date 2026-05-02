namespace XLSight.Analysis;

/// <summary>Describes an Open XML VBA macro project.</summary>
public sealed class VbaProjectInfo
{
    /// <summary>Gets VBA modules declared by the project.</summary>
    public required IReadOnlyList<VbaModuleInfo> Modules { get; init; }

    /// <summary>Gets VBA project references declared by the project.</summary>
    public required IReadOnlyList<VbaReferenceInfo> References { get; init; }

    /// <summary>Gets the VBA project code page when present in the dir stream.</summary>
    public required int? CodePage { get; init; }

    /// <summary>Gets the .NET encoding web name used for best-effort text decoding.</summary>
    public required string? EncodingName { get; init; }

    /// <summary>Gets non-fatal warnings produced while parsing the project.</summary>
    public required IReadOnlyList<AnalysisWarning> Warnings { get; init; }
}
