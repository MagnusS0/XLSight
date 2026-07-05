namespace XLSight.Internal.Sinks;

/// <summary>
/// Chunked append-only store for layout cell facts with the CSR row index and numeric prefix
/// counts built at append time. Chunks stay under the LOH threshold; only the first chunk ever
/// copies, doubling from <see cref="FirstChunkSize"/> up to <see cref="ChunkSize"/> so small
/// sheets don't pay for a full-size chunk, while chunks after it are allocated at full size
/// as today. Peak memory stays proportional to content even on million-cell sheets.
/// </summary>
internal sealed class LayoutCellStore
{
    private const int ChunkShift = 11;
    private const int ChunkSize = 1 << ChunkShift;
    private const int ChunkMask = ChunkSize - 1;
    private const int FirstChunkSize = 64;

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
        if (chunkIndex == 0)
        {
            GrowFirstChunkIfNeeded();
        }
        else if (chunkIndex == _factChunks.Count)
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

    // Only called while chunkIndex == 0, so Count == the write offset into chunk 0. Doubling from
    // FirstChunkSize up to ChunkSize keeps every step a power of two, landing on exactly ChunkSize
    // at the point Count would otherwise roll into chunk 1 — after that, chunk 0 is indistinguishable
    // in size from later chunks and this method is never reached again.
    private void GrowFirstChunkIfNeeded()
    {
        if (_factChunks.Count == 0)
        {
            _factChunks.Add(new LayoutCellFact[FirstChunkSize]);
            _numericBeforeChunks.Add(new int[FirstChunkSize]);
            return;
        }

        if (Count < _factChunks[0].Length)
        {
            return;
        }

        int newSize = Math.Min(_factChunks[0].Length * 2, ChunkSize);
        var facts = new LayoutCellFact[newSize];
        Array.Copy(_factChunks[0], facts, _factChunks[0].Length);
        _factChunks[0] = facts;

        var numericBefore = new int[newSize];
        Array.Copy(_numericBeforeChunks[0], numericBefore, _numericBeforeChunks[0].Length);
        _numericBeforeChunks[0] = numericBefore;
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
