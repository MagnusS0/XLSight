namespace XLSight.Analysis;

/// <summary>Describes a VBA module stored in an Open XML macro project.</summary>
public sealed class VbaModuleInfo
{
    /// <summary>Gets the module name as declared in the VBA project metadata.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the CFB stream name containing the module source.</summary>
    public required string StreamName { get; init; }

    /// <summary>Gets the module kind when known.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the byte offset where compressed source starts in the module stream.</summary>
    public required int TextOffset { get; init; }

    /// <summary>Gets the decompressed raw source byte length.</summary>
    public required int RawByteLength { get; init; }
}
