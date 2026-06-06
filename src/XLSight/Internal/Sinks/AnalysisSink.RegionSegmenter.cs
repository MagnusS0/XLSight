using System.Runtime.InteropServices;
using XLSight.Analysis;

namespace XLSight.Internal.Sinks;

internal partial struct AnalysisSink
{
    // Pre-built evidence lists for all 16 flag combinations (bits: text, numeric, rectangular, label-value).
    // Zero allocation per SealBlock call.
    private static readonly IReadOnlyList<string>[] s_evidenceByFlags = BuildEvidenceByFlags();

    private static IReadOnlyList<string>[] BuildEvidenceByFlags()
    {
        ReadOnlySpan<string> names = ["text-present", "numeric-present", "rectangular-block", "label-value-pattern"];
        var sets = new IReadOnlyList<string>[16];
        for (int mask = 0; mask < 16; mask++)
        {
            var list = new List<string>(4);
            for (int bit = 0; bit < 4; bit++)
            {
                if ((mask & (1 << bit)) != 0) { list.Add(names[bit]); }
            }

            sets[mask] = list.Count == 0 ? [] : list.AsReadOnly();
        }

        return sets;
    }

    private void AddRowCell(int column, ExcelCellValue value, bool isFormula)
    {
        if (_pendingRowSpans.Count == 0 || column > _pendingRowSpans[^1].EndCol + 1)
        {
            _pendingRowSpans.Add(new RowSpanState
            {
                StartCol = column,
                EndCol = column,
            });
        }
        else
        {
            var span = _pendingRowSpans[^1];
            span.EndCol = column;
            _pendingRowSpans[^1] = span;
        }

        var current = _pendingRowSpans[^1];
        current.CellCount++;
        if (isFormula) { current.FormulaCount++; }
        if (value.CellType == CellType.Text) { current.TextCount++; }
        if (value.CellType is CellType.Number or CellType.Date or CellType.Boolean) { current.NumericCount++; }
        _pendingRowSpans[^1] = current;
    }

    private void FinalizeCurrentRow()
    {
        foreach (ref var b in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_activeBlocks))
        {
            b.TouchedThisRow = false;
        }

        foreach (var span in _pendingRowSpans)
        {
            int bestIndex = FindMatchingBlock(span);
            if (bestIndex < 0)
            {
                _activeBlocks.Add(new ActiveBlockState
                {
                    StartRow = _currentRow, EndRow = _currentRow,
                    StartCol = span.StartCol, EndCol = span.EndCol,
                    TotalCells = span.CellCount, TextCells = span.TextCount,
                    NumericCells = span.NumericCount, TouchedThisRow = true, GapRows = 0,
                });
                continue;
            }

            var block = _activeBlocks[bestIndex];
            block.EndRow = _currentRow;
            block.StartCol = Math.Min(block.StartCol, span.StartCol);
            block.EndCol = Math.Max(block.EndCol, span.EndCol);
            block.TotalCells += span.CellCount;
            block.TextCells += span.TextCount;
            block.NumericCells += span.NumericCount;
            block.FormulaCells += span.FormulaCount;
            block.TouchedThisRow = true;
            block.GapRows = 0;
            _activeBlocks[bestIndex] = block;
        }

        for (int i = _activeBlocks.Count - 1; i >= 0; i--)
        {
            var block = _activeBlocks[i];
            if (block.TouchedThisRow) { continue; }

            block.GapRows++;
            if (block.GapRows > VerticalGapTolerance)
            {
                SealBlock(block);
                _activeBlocks.RemoveAt(i);
                continue;
            }

            _activeBlocks[i] = block;
        }

        _pendingRowSpans.Clear();
        _hasPendingRow = false;
    }

    private int FindMatchingBlock(RowSpanState span)
    {
        int bestIndex = -1;
        int bestOverlap = -1;

        for (int i = 0; i < _activeBlocks.Count; i++)
        {
            var block = _activeBlocks[i];
            if (_currentRow - block.EndRow > VerticalGapTolerance + 1) { continue; }

            int overlap = Overlap(span.StartCol, span.EndCol, block.StartCol, block.EndCol);
            bool connected = overlap > 0 ||
                (span.EndCol >= block.StartCol - HorizontalGapTolerance &&
                 span.StartCol <= block.EndCol + HorizontalGapTolerance);
            if (!connected) { continue; }

            if (overlap > bestOverlap)
            {
                bestIndex = i;
                bestOverlap = overlap;
            }
        }

        return bestIndex;
    }

    private void SealRemainingBlocks()
    {
        foreach (var block in _activeBlocks)
        {
            SealBlock(block);
        }

        _activeBlocks.Clear();
    }

    private void SealBlock(ActiveBlockState block)
    {
        var range = new ExcelRange(
            new ExcelAddress(block.StartCol, block.StartRow),
            new ExcelAddress(block.EndCol, block.EndRow));

        RegionKind kind = InferRegionKind(block);

        int evidenceFlags = 0;
        if (block.TextCells > 0) { evidenceFlags |= 1; }
        if (block.NumericCells > 0) { evidenceFlags |= 2; }
        if (range.Width >= 3 && range.Height >= 3) { evidenceFlags |= 4; }
        if (range.Width <= 3 && block.TextCells > 0 && block.NumericCells > 0) { evidenceFlags |= 8; }

        _sealedRegions.Add(new RegionInfo
        {
            Kind = kind,
            Range = range,
            CellCount = block.TotalCells,
            RowCount = range.Height,
            ColumnCount = range.Width,
            FormulaCount = block.FormulaCells,
            HeaderRows = kind is RegionKind.DataTable or RegionKind.HeaderBand ? [block.StartRow] : [],
            Evidence = s_evidenceByFlags[evidenceFlags],
        });
    }

    private static RegionKind InferRegionKind(ActiveBlockState block)
    {
        int width = block.EndCol - block.StartCol + 1;
        int height = block.EndRow - block.StartRow + 1;
        double textRatio = block.TotalCells == 0 ? 0 : (double)block.TextCells / block.TotalCells;
        double numericRatio = block.TotalCells == 0 ? 0 : (double)block.NumericCells / block.TotalCells;

        if (height == 1 && textRatio >= 0.6) { return RegionKind.HeaderBand; }
        if (width >= 3 && height >= 3 && (textRatio > 0.15 || numericRatio > 0.3)) { return RegionKind.DataTable; }
        if (width <= 3 && height >= 2 && block.TextCells > 0 && block.NumericCells > 0) { return RegionKind.ParameterBlock; }
        if (width >= 2 && height >= 2 && numericRatio > 0.5) { return RegionKind.SummaryBlock; }
        return RegionKind.Unknown;
    }

    private static int Overlap(int start1, int end1, int start2, int end2)
        => Math.Max(0, Math.Min(end1, end2) - Math.Max(start1, start2) + 1);

    [StructLayout(LayoutKind.Auto)]
    private struct RowSpanState
    {
        public int StartCol;
        public int EndCol;
        public int CellCount;
        public int TextCount;
        public int NumericCount;
        public int FormulaCount;
    }

    [StructLayout(LayoutKind.Auto)]
    private struct ActiveBlockState
    {
        public int StartRow;
        public int EndRow;
        public int StartCol;
        public int EndCol;
        public int TotalCells;
        public int TextCells;
        public int NumericCells;
        public int FormulaCells;
        public bool TouchedThisRow;
        public int GapRows;
    }
}
