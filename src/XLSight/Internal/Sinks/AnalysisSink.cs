using System.Buffers;
using System.Runtime.InteropServices;
using XLSight.Internal.Analysis;
using XLSight.Internal.Metadata;
using XLSight.Analysis;

namespace XLSight.Internal.Sinks;

[StructLayout(LayoutKind.Auto)]
internal partial struct AnalysisSink : IByteSheetSink
{
    private const int VerticalGapTolerance = 1;
    private const int HorizontalGapTolerance = 1;

    // ── Bounds tracking ───────────────────────────────────────────────────────────
    private int _minValueRow;
    private int _maxValueRow;
    private int _minValueCol;
    private int _maxValueCol;
    private int _minStyledRow;
    private int _maxStyledRow;
    private int _minStyledCol;
    private int _maxStyledCol;
    private int _cellCount;
    private int _currentRow;
    private bool _hasPendingRow;

    // ── Configuration ─────────────────────────────────────────────────────────────
    private readonly SharedStringTable _sst;
    private readonly AnalysisLevel _level;

    // ── Column tracking ───────────────────────────────────────────────────────────
    private Dictionary<int, ColumnState> _columnStates;
    private List<MergedRegion> _mergedRegions;
    private int _firstRowIndex;
    private List<(int Column, ExcelCellValue Value)>? _firstRowByColumn;

    // ── Region segmenter state ────────────────────────────────────────────────────
    private List<RowSpanState> _pendingRowSpans;
    private List<ActiveBlockState> _activeBlocks;
    private List<RegionInfo> _sealedRegions;

    // ── Scan-time metadata (exact counts, populated by sink callbacks) ─────────────
    private ExcelRange? _declaredDimension;
    private int _cfCount;
    private int _dvCount;
    private int _hyperlinkCount;
    private int _formulaCount;
    private int _arrayFormulaCount;
    private Dictionary<int, int>? _formulaCountByColumn;

    public AnalysisSink(SharedStringTable sst, AnalysisLevel level = AnalysisLevel.Full)
    {
        _sst = sst;
        _level = level;
        _minValueRow = int.MaxValue;
        _maxValueRow = int.MinValue;
        _minValueCol = int.MaxValue;
        _maxValueCol = int.MinValue;
        _minStyledRow = int.MaxValue;
        _maxStyledRow = int.MinValue;
        _minStyledCol = int.MaxValue;
        _maxStyledCol = int.MinValue;
        _cellCount = 0;
        _currentRow = 0;
        _hasPendingRow = false;
        _columnStates = [];
        _mergedRegions = [];
        _firstRowIndex = 0;
        _firstRowByColumn = null;  // lazily initialised on first row
        _pendingRowSpans = [];
        _activeBlocks = [];
        _sealedRegions = [];
    }

    public bool NeedsDecodedValue => false;
    public bool TracksFormulas => true;

    public void OnDimension(in ExcelRange dimension) { _declaredDimension = dimension; }

    public void OnRowStart(int rowIndex)
    {
        if (_level == AnalysisLevel.Exact) { return; }

        if (_hasPendingRow)
        {
            FinalizeCurrentRow();
        }

        _currentRow = rowIndex;
        _hasPendingRow = true;

        if (_firstRowIndex == 0)
        {
            _firstRowIndex = rowIndex;
            _firstRowByColumn = [];  // List — insertion order = column scan order
        }
    }

    public void OnFormula(int column, bool isArray)
    {
        _formulaCount++;
        if (isArray) { _arrayFormulaCount++; }
        _formulaCountByColumn ??= [];
        _formulaCountByColumn.TryGetValue(column, out int existing);
        _formulaCountByColumn[column] = existing + 1;
    }

    public void OnConditionalFormatting() { _cfCount++; }
    public void OnDataValidation() { _dvCount++; }
    public void OnHyperlink() { _hyperlinkCount++; }

    public bool OnCell(int column, CellDataKind kind, int styleIdx, ExcelCellValue value, int rawIndex)
    {
        if (_level == AnalysisLevel.Exact) { return true; }

        bool isSharedString = rawIndex >= 0;
        bool isCellNonEmpty = !value.IsEmpty || isSharedString;
        bool isStyledCell = styleIdx != 0 || isCellNonEmpty;

        if (isStyledCell) { UpdateStyledBounds(_currentRow, column); }
        if (!isCellNonEmpty) { return true; }

        UpdateValueBounds(_currentRow, column);
        _cellCount++;

        // Materialise shared-string value once — used by first-row tracking and RecordValue.
        if (isSharedString && value.IsEmpty)
        {
            value = ExcelCellValue.FromSharedString(_sst.GetString(rawIndex), rawIndex);
        }

        var state = GetOrAddColumnState(column);
        if (isSharedString && _currentRow != _firstRowIndex)
        {
            state.RecordSharedString(rawIndex, _sst);
        }
        else
        {
            state.RecordValue(value);
        }

        if (_currentRow == _firstRowIndex && _firstRowByColumn is not null)
        {
            _firstRowByColumn.Add((column, value));
        }

        AddRowCell(column, value);
        return true;
    }

    public void OnMergeCell(in MergedRegion region) { _mergedRegions.Add(region); }

    public void OnEnd()
    {
        if (_level == AnalysisLevel.Exact) { return; }
        if (_hasPendingRow) { FinalizeCurrentRow(); }
        SealRemainingBlocks();
    }

    internal SheetInfo Build(
        string sheetName,
        int sheetIndex,
        SheetExactMetadata exactMetadata,
        AnalysisLevel level)
    {
        var exact = new SheetAnalysisExact
        {
            // Dimension and merge regions come from the scan (preferred over pre-cached exactMetadata).
            DeclaredDimension = _declaredDimension ?? exactMetadata.Exact.DeclaredDimension,
            MergedRegions = _mergedRegions.Count > 0 ? _mergedRegions : exactMetadata.Exact.MergedRegions,
            // CF/DV/hyperlinks come from post-sheetData scan.
            ConditionalFormattingCount = _cfCount,
            DataValidationCount = _dvCount,
            HyperlinkCount = _hyperlinkCount,
            // Secondary file data remains from AnalyzerMetadataReader.
            Tables = exactMetadata.Exact.Tables,
            PivotTables = exactMetadata.Exact.PivotTables,
            Charts = exactMetadata.Exact.Charts,
            CommentCount = exactMetadata.Exact.CommentCount,
            DrawingCount = exactMetadata.Exact.DrawingCount,
        };

        if (level == AnalysisLevel.Exact)
        {
            return new SheetInfo
            {
                Level = level, SheetName = sheetName, SheetIndex = sheetIndex,
                Exact = exact, Observed = SheetAnalysisObserved.Empty, Inferred = SheetAnalysisInferred.Empty,
            };
        }

        int headerRow = level >= AnalysisLevel.Full ? InferHeader() : 0;
        var headersByColumn = level >= AnalysisLevel.Full ? BuildHeaders(headerRow) : [];
        var observed = BuildObserved(headersByColumn);
        var inferred = level >= AnalysisLevel.Full
            ? BuildInferred(exact, observed, headerRow)
            : SheetAnalysisInferred.Empty;

        return new SheetInfo
        {
            Level = level, SheetName = sheetName, SheetIndex = sheetIndex,
            Exact = exact, Observed = observed, Inferred = inferred,
        };
    }

    private SheetAnalysisObserved BuildObserved(Dictionary<int, string> headersByColumn)
    {
        return new SheetAnalysisObserved
        {
            ValueUsedRange = BuildRange(_minValueCol, _minValueRow, _maxValueCol, _maxValueRow),
            StyledUsedRange = BuildRange(_minStyledCol, _minStyledRow, _maxStyledCol, _maxStyledRow),
            RowCount = _minValueRow == int.MaxValue ? 0 : _maxValueRow - _minValueRow + 1,
            ColumnCount = _minValueCol == int.MaxValue ? 0 : _maxValueCol - _minValueCol + 1,
            CellCount = _cellCount,
            FormulaCount = _formulaCount,
            ArrayFormulaCount = _arrayFormulaCount,
            FormulaColumns = BuildFormulaColumns(),
            Columns = ColumnProfiler.BuildProfiles(_columnStates, headersByColumn, _formulaCountByColumn),
        };
    }

    private FormulaColumnProfile[] BuildFormulaColumns()
    {
        if (_formulaCountByColumn is null || _formulaCountByColumn.Count == 0) { return []; }

        int count = _formulaCountByColumn.Count;
        int[] keys = ArrayPool<int>.Shared.Rent(count);
        int idx = 0;
        foreach (int k in _formulaCountByColumn.Keys) { keys[idx++] = k; }
        Array.Sort(keys, 0, count);

        var result = new FormulaColumnProfile[count];
        for (int i = 0; i < count; i++)
        {
            int col = keys[i];
            result[i] = new FormulaColumnProfile
            {
                ColumnIndex = col,
                ColumnLabel = ColumnLabel(col),
                FormulaCount = _formulaCountByColumn[col],
            };
        }

        ArrayPool<int>.Shared.Return(keys);
        return result;
    }

    private SheetAnalysisInferred BuildInferred(SheetAnalysisExact exact, SheetAnalysisObserved observed, int headerRow)
    {
        var headerBands = new List<HeaderBandInfo>();
        if (headerRow > 0 && observed.ValueUsedRange is { } usedRange)
        {
            headerBands.Add(new HeaderBandInfo
            {
                Range = new ExcelRange(
                    new ExcelAddress(usedRange.TopLeft.Column, headerRow),
                    new ExcelAddress(usedRange.BottomRight.Column, headerRow)),
                Rows = [headerRow],
                Confidence = 0.8,
            });
        }

        var warnings = new List<AnalysisWarning>();
        if (exact.DeclaredDimension is { } declared && observed.ValueUsedRange is { } actual && declared != actual)
        {
            warnings.Add(new AnalysisWarning
            {
                Code = "declared-dimension-mismatch",
                Message = $"Declared dimension {declared} differs from observed value-used range {actual}.",
            });
        }

        return new SheetAnalysisInferred
        {
            Regions = _sealedRegions,
            HeaderBands = headerBands,
            HeaderRowIndex = headerRow,
            Warnings = warnings,
        };
    }
}
