namespace XLSight.Analysis;

/// <summary>Describes a VBA project reference.</summary>
public sealed class VbaReferenceInfo
{
    /// <summary>Gets the reference name when present.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the display description when present.</summary>
    public required string Description { get; init; }

    /// <summary>Gets the resolved or declared reference path when present.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the reference record kind.</summary>
    public required string Kind { get; init; }
}
