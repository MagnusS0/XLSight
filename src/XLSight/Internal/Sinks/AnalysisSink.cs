using System.Buffers;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using XLSight.Internal.Analysis;
using XLSight.Analysis;

namespace XLSight.Internal.Sinks;

[StructLayout(LayoutKind.Auto)]
internal partial struct AnalysisSink : IByteSheetSink
{
    private const int VerticalGapTolerance = 1;

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
    private readonly ISharedStringSource _sst;
    private readonly AnalysisLevel _level;
    private readonly int _distinctValuesCap;

    // ── Column tracking ───────────────────────────────────────────────────────────
    private Dictionary<int, ColumnState> _columnStates;
    private List<MergedRegion> _mergedRegions;
    private int _firstRowIndex;
    private List<(int Column, ExcelCellValue Value)>? _firstRowByColumn;

    // ── Region segmenter state ────────────────────────────────────────────────────
    private List<RowSpanState> _pendingRowSpans;
    private List<ActiveBlockState> _activeBlocks;
    private List<RegionInfo> _sealedRegions;
    private const int LayoutTextSampleBudget = 50_000;

    private LayoutCellStore _layoutCells;
    private int _layoutTextSamplesRemaining;

    // ── Scan-time metadata (exact counts, populated by sink callbacks) ─────────────
    private ExcelRange? _declaredDimension;
    private int _cfCount;
    private int _dvCount;
    private int _hyperlinkCount;
    private int _formulaCount;
    private int _arrayFormulaCount;
    private Dictionary<int, int>? _formulaCountByColumn;
    private Dictionary<FormulaDependencyKey, int>? _formulaDependencies;
    private HashSet<FormulaDependencyKey>? _currentFormulaTargets;
    private Dictionary<int, FormulaDependencyKey[]>? _sharedFormulaTargets;
    private int? _pendingSharedFormulaIndex;
    private bool _nextCellIsFormula;
    private List<DataValidationInfo>? _dataValidations;
    private readonly string _sheetName;

    public AnalysisSink(ISharedStringSource sst, string sheetName, AnalysisLevel level = AnalysisLevel.Full, AnalysisOptions? options = null)
    {
        _sst = sst;
        _sheetName = sheetName;
        _level = level;
        _distinctValuesCap = (options ?? AnalysisOptions.Default).DistinctValuesCap;
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
        _layoutCells = new LayoutCellStore();
        _layoutTextSamplesRemaining = LayoutTextSampleBudget;
    }

    public bool NeedsDecodedValue => false;
    public bool TracksFormulas => _level != AnalysisLevel.Exact;
    public bool TracksFormulaReferences => _level != AnalysisLevel.Exact;

    // Deliberately no layout-fact presizing from the declared dimension: sparse sheets
    // routinely declare far more cells than they hold, so trusting it reserves memory
    // the scan never fills.
    public void OnDimension(in ExcelRange dimension) { _declaredDimension = dimension; }

    public void OnRowStart(int rowIndex)
    {
        if (_level == AnalysisLevel.Exact) { return; }

        if (_level >= AnalysisLevel.Full && _hasPendingRow)
        {
            FinalizeCurrentRow();
        }

        _currentRow = rowIndex;
        if (_level >= AnalysisLevel.Full) { _hasPendingRow = true; }

        if (_firstRowIndex == 0)
        {
            _firstRowIndex = rowIndex;
            _firstRowByColumn = [];  // List — insertion order = column scan order
        }
    }

    public void OnFormula(int column, bool isArray)
    {
        FinalizeSharedFormulaDefinition();
        _currentFormulaTargets?.Clear();
        _formulaCount++;
        _nextCellIsFormula = true;
        if (isArray) { _arrayFormulaCount++; }
        _formulaCountByColumn ??= [];
        _formulaCountByColumn.TryGetValue(column, out int existing);
        _formulaCountByColumn[column] = existing + 1;
    }

    public void OnSharedFormulaDefinition(int sharedIndex)
    {
        _pendingSharedFormulaIndex = sharedIndex;
    }

    public void OnSharedFormulaReference(int sharedIndex)
    {
        if (_sharedFormulaTargets is null || !_sharedFormulaTargets.TryGetValue(sharedIndex, out FormulaDependencyKey[]? targets))
        {
            return;
        }

        foreach (FormulaDependencyKey target in targets)
        {
            IncrementFormulaDependency(target);
        }
    }

    public void OnFormulaReference(in FormulaReference reference)
    {
        string targetSheet;
        string? targetWorkbook;
        if (reference.IsUtf8)
        {
            targetSheet = DecodeFormulaIdentifier(reference.SheetUtf8);
            targetWorkbook = reference.WorkbookUtf8.IsEmpty
                ? null
                : DecodeFormulaIdentifier(reference.WorkbookUtf8);
        }
        else
        {
            targetSheet = reference.Sheet!;
            targetWorkbook = reference.Workbook;
        }

        if (targetSheet.Length == 0 ||
            (targetWorkbook is null && string.Equals(targetSheet, _sheetName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var key = new FormulaDependencyKey(targetWorkbook, targetSheet);
        if (!(_currentFormulaTargets ??= []).Add(key))
        {
            return;
        }

        IncrementFormulaDependency(key);
    }

    public void OnConditionalFormatting() { _cfCount++; }
    public void OnDataValidation(DataValidationInfo? validation)
    {
        _dvCount++;
        if (validation is not null)
        {
            (_dataValidations ??= []).Add(validation);
        }
    }
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

        bool isFormula = _nextCellIsFormula;
        _nextCellIsFormula = false;
        if (_level >= AnalysisLevel.Full)
        {
            AddRowCell(column, value, isFormula);
            AddLayoutCell(column, value, isFormula);
        }
        return true;
    }

    public void OnMergeCell(in MergedRegion region) { _mergedRegions.Add(region); }

    public void OnEnd()
    {
        FinalizeSharedFormulaDefinition();
        if (_level == AnalysisLevel.Exact) { return; }
        if (_level >= AnalysisLevel.Full)
        {
            if (_hasPendingRow) { FinalizeCurrentRow(); }
            SealRemainingBlocks();
        }
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
            DataValidations = _dataValidations ?? [],
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
                Exact = exact, Observed = null, Inferred = null,
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
            Exact = exact,
            Observed = observed,
            Inferred = level >= AnalysisLevel.Full ? inferred : null,
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
            FormulaDependencies = BuildFormulaDependencies(),
            Columns = ColumnProfiler.BuildProfiles(_columnStates, headersByColumn, _sst, _distinctValuesCap, _formulaCountByColumn),
        };
    }

    private FormulaDependencyInfo[] BuildFormulaDependencies()
    {
        if (_formulaDependencies is null || _formulaDependencies.Count == 0)
        {
            return [];
        }

        return [.. _formulaDependencies
            .OrderBy(static pair => pair.Key.Workbook, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pair => pair.Key.Sheet, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => new FormulaDependencyInfo
            {
                TargetWorkbook = pair.Key.Workbook,
                TargetSheet = pair.Key.Sheet,
                FormulaCount = pair.Value,
            })];
    }

    private void FinalizeSharedFormulaDefinition()
    {
        if (_pendingSharedFormulaIndex is not { } sharedIndex)
        {
            return;
        }

        _sharedFormulaTargets ??= [];
        _sharedFormulaTargets[sharedIndex] = _currentFormulaTargets is { Count: > 0 }
            ? [.. _currentFormulaTargets]
            : [];
        _pendingSharedFormulaIndex = null;
    }

    private void IncrementFormulaDependency(FormulaDependencyKey key)
    {
        _formulaDependencies ??= [];
        _formulaDependencies.TryGetValue(key, out int count);
        _formulaDependencies[key] = count + 1;
    }

    private static string DecodeFormulaIdentifier(ReadOnlySpan<byte> utf8)
    {
        string value = Encoding.UTF8.GetString(utf8);
        if (value.Contains("''", StringComparison.Ordinal))
        {
            value = value.Replace("''", "'", StringComparison.Ordinal);
        }

        return value.Contains('&', StringComparison.Ordinal) ? WebUtility.HtmlDecode(value) : value;
    }

    private readonly record struct FormulaDependencyKey(string? Workbook, string Sheet)
    {
        public bool Equals(FormulaDependencyKey other) =>
            StringComparer.OrdinalIgnoreCase.Equals(Workbook, other.Workbook) &&
            StringComparer.OrdinalIgnoreCase.Equals(Sheet, other.Sheet);

        public override int GetHashCode() =>
            HashCode.Combine(
                Workbook is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Workbook),
                StringComparer.OrdinalIgnoreCase.GetHashCode(Sheet));
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
        // The first DataTable or Crosstab region's StartRow is the canonical
        // header row; prefer it over the first-row heuristic so header bands and
        // HeaderRowIndex agree.
        int headerRowFromRegion = 0;
        foreach (var region in _sealedRegions)
        {
            if (region.Kind is RegionKind.DataTable or RegionKind.Crosstab)
            {
                headerRowFromRegion = region.Range.TopLeft.Row;
                break;
            }
        }

        int effectiveHeaderRow = headerRowFromRegion != 0 ? headerRowFromRegion : headerRow;

        var headerBands = new List<HeaderBandInfo>();
        if (effectiveHeaderRow > 0 && observed.ValueUsedRange is { } usedRange)
        {
            headerBands.Add(new HeaderBandInfo
            {
                Range = new ExcelRange(
                    new ExcelAddress(usedRange.TopLeft.Column, effectiveHeaderRow),
                    new ExcelAddress(usedRange.BottomRight.Column, effectiveHeaderRow)),
                Rows = [effectiveHeaderRow],
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

        SheetLayoutInfo layout = SheetLayoutInference.Infer(_layoutCells);
        return new SheetAnalysisInferred
        {
            Regions = _sealedRegions,
            Layout = layout,
            HeaderBands = headerBands,
            HeaderRowIndex = effectiveHeaderRow,
            Warnings = warnings,
        };
    }
}
