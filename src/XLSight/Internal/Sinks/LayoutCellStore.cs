namespace XLSight.Internal.Sinks;

/// <summary>
/// Chunked append-only store for layout cell facts with the CSR row index and numeric prefix
/// counts built at append time. Chunks stay under the LOH threshold and growth never copies,
/// so peak memory stays proportional to content even on million-cell sheets.
/// </summary>
internal sealed class LayoutCellStore
{
    private const int ChunkShift = 11;
    private const int ChunkSize = 1 << ChunkShift;
    private const int ChunkMask = ChunkSize - 1;

    private readonly List<LayoutCellFact[]> _factChunks = [];
    private readonly List<int[]> _numericBeforeChunks = [];
    private readonly List<int> _rowNumbers = [];
    private readonly List<int> _rowStarts = [];
    private int _numericTotal;
    private int _lastRow;
    private int _lastColumn;

    public int Count { get; private set; }

    /// <summary>Whether cells arrived in scan order (rows ascending, columns strictly ascending within a row).</summary>
    public bool IsSorted { get; private set; } = true;

    public int MinCol { get; private set; } = int.MaxValue;

    public int MaxCol { get; private set; } = int.MinValue;

    /// <summary>Distinct row numbers in first-seen order; ascending when <see cref="IsSorted"/>.</summary>
    public List<int> RowNumbers => _rowNumbers;

    public LayoutCellFact this[int index] => _factChunks[index >> ChunkShift][index & ChunkMask];

    /// <summary>First cell index of the row at <paramref name="rowIndex"/>; <see cref="Count"/> when one past the last row.</summary>
    public int RowStartAt(int rowIndex) => rowIndex == _rowStarts.Count ? Count : _rowStarts[rowIndex];

    /// <summary>Number of numeric cells stored before cell <paramref name="index"/>.</summary>
    public int NumericCountBefore(int index) =>
        index == Count ? _numericTotal : _numericBeforeChunks[index >> ChunkShift][index & ChunkMask];

    public void Add(in LayoutCellFact fact)
    {
        int chunkIndex = Count >> ChunkShift;
        if (chunkIndex == _factChunks.Count)
        {
            _factChunks.Add(new LayoutCellFact[ChunkSize]);
            _numericBeforeChunks.Add(new int[ChunkSize]);
        }

        int offset = Count & ChunkMask;
        _factChunks[chunkIndex][offset] = fact;
        _numericBeforeChunks[chunkIndex][offset] = _numericTotal;

        if (Count == 0 || fact.Row != _lastRow)
        {
            if (Count > 0 && fact.Row < _lastRow)
            {
                IsSorted = false;
            }

            _rowNumbers.Add(fact.Row);
            _rowStarts.Add(Count);
        }
        else if (fact.Column <= _lastColumn)
        {
            IsSorted = false;
        }

        _lastRow = fact.Row;
        _lastColumn = fact.Column;
        if (fact.HasNumericValue)
        {
            _numericTotal++;
        }

        MinCol = Math.Min(MinCol, fact.Column);
        MaxCol = Math.Max(MaxCol, fact.Column);
        Count++;
    }

    /// <summary>Rare fallback for producers that violate scan order: flat copy, sort, re-append.</summary>
    public LayoutCellStore Rebuilt()
    {
        var facts = new LayoutCellFact[Count];
        for (int i = 0; i < Count; i++)
        {
            facts[i] = this[i];
        }

        Array.Sort(facts, static (left, right) => left.Row != right.Row
            ? left.Row.CompareTo(right.Row)
            : left.Column.CompareTo(right.Column));

        var store = new LayoutCellStore();
        foreach (var fact in facts)
        {
            store.Add(fact);
        }

        return store;
    }
}
