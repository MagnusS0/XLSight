using XLSight.Analysis;
using XLSight.Layout;
using XLSight.Query;

namespace XLSight.FastBench;

internal readonly record struct OpResult(ulong Digest, long Units);

/// <summary>
/// The workloads a manifest case can name. Each operation opens its own workbook so open cost is
/// part of every measurement, consumes every produced value into a digest (defeating dead-code
/// elimination), and reports a work denominator for throughput.
/// </summary>
internal static class Operations
{
    public static OpResult Run(CaseDef def, string? fixturePath, bool deep) => def.Op switch
    {
        "open" => Open(fixturePath!),
        "readCell" => ReadCell(fixturePath!, def, deep),
        "scanSheet" => Scan(fixturePath!, def, deep, snapshots: false),
        "streamSheet" => Scan(fixturePath!, def, deep, snapshots: true),
        "rangeReader" => RangeReader(fixturePath!, def, deep),
        "readRange" => ReadRange(fixturePath!, def, deep),
        "analyze" => Analyze(fixturePath!, def, deep),
        "layout" => Layout(fixturePath!, def),
        "query" => Query(fixturePath!, def, deep),
        "queryBuilderFilterSum" => QueryBuilderFilterSum(fixturePath!, def, deep),
        "distinct" => Distinct(fixturePath!, def, deep),
        "sleep" => Sleep(def),
        _ => throw new InvalidOperationException($"Unknown op '{def.Op}' in case '{def.Id}'."),
    };

    private static ReadMode Mode(CaseDef def) =>
        string.Equals(def.Mode, "Formulas", StringComparison.OrdinalIgnoreCase) ? ReadMode.Formulas : ReadMode.Values;

    private static OpResult Open(string path)
    {
        using var workbook = ExcelWorkbook.Open(path);
        var digest = new Digest();
        foreach (string name in workbook.SheetNames)
        {
            digest.AddText(name, deep: true);
        }

        digest.Add(workbook.IsDate1904);
        digest.Add(workbook.HasMacros);
        return new(digest.Value, workbook.SheetNames.Count);
    }

    private static OpResult ReadCell(string path, CaseDef def, bool deep)
    {
        using var workbook = ExcelWorkbook.Open(path);
        ExcelCellValue cell = workbook.ReadCell(def.Sheet!, def.Address!, Mode(def));
        var digest = new Digest();
        digest.AddCell(cell, deep);
        return new(digest.Value, 1);
    }

    private static OpResult Scan(string path, CaseDef def, bool deep, bool snapshots)
    {
        using var workbook = ExcelWorkbook.Open(path);
        var digest = new Digest();
        long cells = 0;
        ReadMode mode = Mode(def);
        IReadOnlyList<string> sheets = def.Sheet is null or "*" ? workbook.SheetNames : [def.Sheet];
        foreach (string sheet in sheets)
        {
            if (snapshots)
            {
                foreach (ExcelRow row in workbook.StreamSheet(sheet, mode))
                {
                    cells += AddRow(ref digest, row, deep);
                }
            }
            else
            {
                using ExcelSheetReader reader = workbook.GetSheetReader(sheet, mode);
                while (reader.Read())
                {
                    cells += AddRow(ref digest, reader.Current, deep);
                }
            }
        }

        return new(digest.Value, cells);
    }

    private static OpResult RangeReader(string path, CaseDef def, bool deep)
    {
        using var workbook = ExcelWorkbook.Open(path);
        using ExcelSheetReader reader = workbook.GetRangeReader(def.Sheet!, def.Range!, Mode(def));
        var digest = new Digest();
        long cells = 0;
        while (reader.Read())
        {
            cells += AddRow(ref digest, reader.Current, deep);
        }

        return new(digest.Value, cells);
    }

    private static OpResult ReadRange(string path, CaseDef def, bool deep)
    {
        using var workbook = ExcelWorkbook.Open(path);
        RangeResult result = workbook.ReadRange(def.Sheet!, def.Range!, Mode(def));
        var digest = new Digest();
        long cells = 0;
        foreach (ExcelRow row in result.Rows)
        {
            cells += AddRow(ref digest, row, deep);
        }

        return new(digest.Value, cells);
    }

    private static int AddRow(ref Digest digest, in ExcelRow row, bool deep)
    {
        digest.Add(row.RowIndex);
        digest.Add(row.StartColumn);
        ReadOnlySpan<ExcelCellValue> cells = row.Cells;
        for (int i = 0; i < cells.Length; i++)
        {
            digest.AddCell(cells[i], deep);
        }

        return cells.Length;
    }

    private static OpResult Analyze(string path, CaseDef def, bool deep)
    {
        using var workbook = ExcelWorkbook.Open(path);
        AnalysisLevel level = Enum.Parse<AnalysisLevel>(def.Level ?? nameof(AnalysisLevel.Full));
        IReadOnlyList<SheetInfo> sheets;
        var digest = new Digest();
        if (def.Sheet is null or "*")
        {
            WorkbookInfo info = workbook.Analyze(level);
            digest.Add(info.NamedRanges.Count);
            digest.Add(info.Tables.Count);
            digest.Add(info.Charts.Count);
            digest.Add(info.PivotTables.Count);
            digest.Add(info.HasMacros);
            sheets = info.Sheets;
        }
        else
        {
            sheets = [workbook.AnalyzeSheet(def.Sheet, level)];
        }

        long cells = 0;
        foreach (SheetInfo sheet in sheets)
        {
            digest.AddText(sheet.SheetName, deep: true);
            digest.Add(sheet.RowCount ?? -1);
            digest.Add(sheet.ColumnCount ?? -1);
            digest.Add(sheet.CellCount ?? -1);
            digest.AddText(sheet.UsedRange?.ToString(), deep: true);
            digest.Add(sheet.InferredHeaderRowIndex ?? -1);
            digest.Add(sheet.MergedRegions.Count);
            digest.Add(sheet.Tables.Count);
            digest.Add(sheet.DataValidations.Count);
            digest.Add(sheet.FormulaDependencies?.Count ?? -1);
            cells += sheet.CellCount ?? 0;
            foreach (ColumnProfile column in sheet.Columns ?? [])
            {
                digest.Add(column.ColumnIndex);
                digest.AddText(column.InferredHeader, deep);
                digest.Add((int)column.DominantType);
                digest.Add(column.NonEmptyCount);
                digest.Add(column.TextCount);
                digest.Add(column.NumberCount);
                digest.Add(column.DateCount);
                digest.Add(column.BooleanCount);
                digest.Add(column.DistinctValueEstimate);
                digest.Add(column.MinNumericValue ?? double.NaN);
                digest.Add(column.MaxNumericValue ?? double.NaN);
                digest.Add(column.MaxTextLength ?? -1);
                digest.Add(column.HasFormulas);
                digest.Add(column.DistinctValues?.Count ?? -1);
                foreach (string value in column.DistinctValues ?? [])
                {
                    digest.AddText(value, deep);
                }
            }
        }

        return new(digest.Value, cells);
    }

    private static OpResult Layout(string path, CaseDef def)
    {
        using var workbook = ExcelWorkbook.Open(path);
        IReadOnlyList<string> sheets = def.Sheet is null or "*" ? workbook.SheetNames : [def.Sheet];
        var digest = new Digest();
        foreach (string sheet in sheets)
        {
            SheetLayoutInfo layout = workbook.AnalyzeLayout(sheet);
            digest.Add(layout.Axes.Count);
            digest.Add(layout.MeasureFields.Count);
            digest.Add(layout.Groups.Count);
        }

        return new(digest.Value, sheets.Count);
    }

    private static OpResult Query(string path, CaseDef def, bool deep)
    {
        using var workbook = ExcelWorkbook.Open(path);
        return AddResult(workbook.ExecuteQuery(def.Query!), deep);
    }

    private static OpResult QueryBuilderFilterSum(string path, CaseDef def, bool deep)
    {
        using var workbook = ExcelWorkbook.Open(path);
        QueryResult result = workbook.QueryRange(def.Sheet!, def.Range!, def.HeaderRow)
            .Where(def.Column!, QueryOperator.GreaterThan, def.Threshold)
            .Select(QueryAggregates.Sum(def.SumColumn!), QueryAggregates.Count())
            .Execute();
        return AddResult(result, deep);
    }

    private static OpResult AddResult(QueryResult result, bool deep)
    {
        var digest = new Digest();
        digest.Add(result.RowsScanned);
        digest.Add(result.RowsMatched);
        foreach (string column in result.Columns)
        {
            digest.AddText(column, deep: true);
        }

        foreach (QueryResultRow row in result.Rows)
        {
            digest.Add(row.SourceRowIndex ?? -1);
            foreach (ExcelCellValue value in row.Values.Span)
            {
                digest.AddCell(value, deep);
            }
        }

        digest.Add(result.Unaggregatable.Count);
        return new(digest.Value, result.RowsScanned);
    }

    private static OpResult Distinct(string path, CaseDef def, bool deep)
    {
        using var workbook = ExcelWorkbook.Open(path);
        IReadOnlyList<DistinctValueCount> values = workbook.QueryRange(def.Sheet!, def.Range!, def.HeaderRow)
            .DistinctValues(def.Column!, def.Top);
        var digest = new Digest();
        long total = 0;
        foreach (DistinctValueCount value in values)
        {
            digest.AddText(value.Value, deep);
            digest.Add(value.Count);
            total += value.Count;
        }

        return new(digest.Value, total);
    }

    private static OpResult Sleep(CaseDef def)
    {
        Thread.Sleep(def.SleepMs);
        return new(0, 1);
    }
}
