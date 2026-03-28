namespace XLSight.Models.Analysis;

/// <summary>Describes a named range or named formula defined in an Excel workbook.</summary>
public sealed class NamedRange
{
    /// <summary>Gets the name of the defined name entry.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the sheet this name is scoped to, or null if it is workbook-scoped.</summary>
    public required string? Sheet { get; init; }

    /// <summary>Gets the raw reference string, e.g. <c>Sheet1!$A$1:$D$100</c>.</summary>
    public required string Reference { get; init; }
}
