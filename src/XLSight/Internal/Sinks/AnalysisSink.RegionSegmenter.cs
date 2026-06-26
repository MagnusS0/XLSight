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
        // ponytail: per-cell in Full only; restrict to candidate header rows if profiled hot
        if (_pendingRowSpans.Count == 0 || column > _pendingRowSpans[^1].EndCol + 1)
        {
            _pendingRowSpans.Add(new RowSpanState
            {
                StartCol = column,
                EndCol = column,
                FirstCellType = value.CellType == CellType.Text ? 1 : 2,
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
        if (IsValueLike(value)) { current.ValueLikeCount++; }
        _pendingRowSpans[^1] = current;
    }

    private static bool IsValueLike(in ExcelCellValue v)
    {
        if (v.CellType is CellType.Number or CellType.Date) { return true; }
        if (v.CellType != CellType.Text) { return false; }
        ReadOnlySpan<char> s = v.AsText().AsSpan().Trim();
        if (s.IsEmpty) { return false; }
        if (double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out _)) { return true; }
        if (ContainsYearPattern(s)) { return true; }   // 19xx / 20xx year anywhere in text
        return (s[0] is 'Q' or 'q' or 'H' or 'h' or 'W' or 'w') && s.Length > 1 && char.IsAsciiDigit(s[1]);   // Q1/H2/W3
    }

    private static bool ContainsYearPattern(ReadOnlySpan<char> s)
    {
        for (int i = 0; i + 4 <= s.Length; i++)
        {
            bool is19 = s[i] == '1' && s[i + 1] == '9';
            bool is20 = s[i] == '2' && s[i + 1] == '0';
            if ((is19 || is20) && char.IsAsciiDigit(s[i + 2]) && char.IsAsciiDigit(s[i + 3]))
            {
                return true;
            }
        }

        return false;
    }

    private void FinalizeCurrentRow()
    {
        foreach (ref var b in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_activeBlocks))
        {
            b.TouchedThisRow = false;
        }

        foreach (var span in _pendingRowSpans)
        {
            MergeSpanIntoBlock(span);
        }

        SealExpiredBlocks();

        _pendingRowSpans.Clear();
        _hasPendingRow = false;
    }

    private void MergeSpanIntoBlock(RowSpanState span)
    {
        int bestIndex = FindMatchingBlock(span);
        if (bestIndex < 0)
        {
            // New block: snapshot this span's counts as the top-row state.
            _activeBlocks.Add(new ActiveBlockState
            {
                StartRow = _currentRow, EndRow = _currentRow,
                StartCol = span.StartCol, EndCol = span.EndCol,
                TotalCells = span.CellCount, TextCells = span.TextCount,
                NumericCells = span.NumericCount, TouchedThisRow = true, GapRows = 0,
                TopRowText = span.TextCount,
                TopRowNumeric = span.NumericCount,
                TopRowCells = span.CellCount,
                TopRowValueLike = span.ValueLikeCount,
            });
            return;
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

        // Detect left-label rows: leftmost cell is text, and the REST of the span
        // (excluding that label) is numeric-dominant. Excluding the label cell is
        // what lets a 1-label + 1-value parameter row qualify (numeric >= text).
        if (span.StartCol == block.StartCol && span.FirstCellType == 1
            && span.NumericCount > 0 && span.NumericCount >= span.TextCount)
        {
            block.LeftLabelRows++;
        }

        _activeBlocks[bestIndex] = block;
    }

    private void SealExpiredBlocks()
    {
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
            int spanW = span.EndCol - span.StartCol + 1;
            int blockW = block.EndCol - block.StartCol + 1;
            bool connected = overlap > 0 && (double)overlap / Math.Max(spanW, blockW) >= 0.5;
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

        int width = range.Width;
        int height = range.Height;

        // Compute the three orientation signals.
        double topRowTextRatio = block.TopRowCells == 0 ? 0.0 : (double)block.TopRowText / block.TopRowCells;
        int bodyNumericCells = block.NumericCells - block.TopRowNumeric;
        int bodyTotalCells = Math.Max(1, block.TotalCells - block.TopRowCells);
        double bodyNumericRatio = (double)bodyNumericCells / bodyTotalCells;
        double colCRatio = height <= 1 ? 0.0 : (double)block.LeftLabelRows / height;
        double topVL = block.TopRowCells == 0 ? 0.0 : (double)block.TopRowValueLike / block.TopRowCells;

        (RegionKind kind, int keyCol, double confidence) = InferRegionKind(
            block, width, height, topRowTextRatio, bodyNumericRatio, colCRatio, topVL);

        int evidenceFlags = 0;
        if (block.TextCells > 0) { evidenceFlags |= 1; }
        if (block.NumericCells > 0) { evidenceFlags |= 2; }
        if (width >= 3 && height >= 3) { evidenceFlags |= 4; }
        if (width <= 3 && block.TextCells > 0 && block.NumericCells > 0) { evidenceFlags |= 8; }

        IReadOnlyList<int> headerRows = kind is RegionKind.DataTable or RegionKind.TitleRow
            or RegionKind.Crosstab or RegionKind.Transposed
            ? [block.StartRow]
            : [];

        _sealedRegions.Add(new RegionInfo
        {
            Kind = kind,
            Range = range,
            CellCount = block.TotalCells,
            RowCount = height,
            ColumnCount = width,
            FormulaCount = block.FormulaCells,
            HeaderRows = headerRows,
            Evidence = s_evidenceByFlags[evidenceFlags],
            KeyColumnIndex = keyCol,
            Confidence = Math.Clamp(confidence, 0.0, 1.0),
        });
    }

    private static (RegionKind Kind, int KeyCol, double Confidence) InferRegionKind(
        ActiveBlockState block,
        int width,
        int height,
        double topRowTextRatio,
        double bodyNumericRatio,
        double colCRatio,
        double topVL)
    {
        if (height == 1 && topRowTextRatio >= 0.6)
        {
            return (RegionKind.TitleRow, 0, topRowTextRatio);
        }

        if (width <= 3 && colCRatio >= 0.4 && topRowTextRatio < 0.6)
        {
            return (RegionKind.ParameterBlock, block.StartCol, colCRatio);
        }

        if (colCRatio >= 0.5 && topVL >= 0.6)
        {
            return (RegionKind.Crosstab, block.StartCol, colCRatio);
        }

        bool rowCHigh = topRowTextRatio >= 0.6 && bodyNumericRatio >= 0.5;
        if (rowCHigh)
        {
            return (RegionKind.DataTable, 0, bodyNumericRatio);
        }

        if (colCRatio >= 0.5)
        {
            return (RegionKind.Transposed, block.StartCol, colCRatio);
        }

        if (width >= 2 && height >= 2 && bodyNumericRatio > 0.5)
        {
            return (RegionKind.SummaryBlock, 0, bodyNumericRatio);
        }

        return (RegionKind.Unknown, 0, 0.0);
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
        /// <summary>0 = none, 1 = text, 2 = numeric. Set for the FIRST cell added to the span.</summary>
        public int FirstCellType;
        /// <summary>Count of value-like cells in this span (Number, Date, or period-pattern text).</summary>
        public int ValueLikeCount;
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
        // Orientation-signal accumulators (snapshotted from first span at block creation).
        public int TopRowText;
        public int TopRowNumeric;
        public int TopRowCells;
        public int TopRowValueLike;
        // Count of rows whose leftmost cell (at StartCol) is text and the rest of the row is numeric-dominant.
        public int LeftLabelRows;
    }
}
