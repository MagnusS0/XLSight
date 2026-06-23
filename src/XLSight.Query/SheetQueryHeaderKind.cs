namespace XLSight.Query;

/// <summary>Defines how a Query DSL statement discovers column headers.</summary>
public enum SheetQueryHeaderKind
{
    /// <summary>Use the first non-empty row in the queried range as the header row.</summary>
    Auto,

    /// <summary>Use an explicit 1-based sheet row as the header row.</summary>
    Row,

    /// <summary>Use an explicit sheet column as headers for a transposed table.</summary>
    Column,
}
