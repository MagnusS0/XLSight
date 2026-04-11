namespace XLSight;

/// <summary>
/// Thrown when a requested range would require allocating more cells than
/// <see cref="ExcelLimits.MaxCells"/>.
/// </summary>
public sealed class RangeTooLargeException : ExcelException
{
    /// <summary>The number of cells in the requested range.</summary>
    public long RequestedCells { get; }

    /// <summary>The maximum number of cells permitted.</summary>
    public long MaxCells { get; }

    /// <summary>Initializes a new instance with the requested and maximum cell counts.</summary>
    /// <param name="requestedCells">The number of cells that were requested.</param>
    /// <param name="maxCells">The maximum number of cells allowed.</param>
    public RangeTooLargeException(long requestedCells, long maxCells)
        : base($"Requested range of {requestedCells:N0} cells exceeds the maximum of {maxCells:N0}.")
    {
        RequestedCells = requestedCells;
        MaxCells = maxCells;
    }
}
