# Analyzing workbooks

`Analyze` and `AnalyzeSheet` return workbook structure without reading cell data into memory.
Use `AnalysisLevel` to select how much work to do.

| Level | What is included |
|---|---|
| `Exact` | Package metadata: names, tables, charts, merged cells, validation rules, external links, and macros. |
| `Observed` | `Exact` data plus used ranges, counts, column profiles, and formula dependencies. |
| `Full` (default) | `Observed` data plus inferred regions and the inferred header row. |

```csharp
using XLSight;
using XLSight.Analysis;

using var workbook = ExcelWorkbook.Open("report.xlsx");

// All sheets. Full analysis is the default.
WorkbookInfo info = workbook.Analyze();
Console.WriteLine($"Tables: {info.Tables.Count}");
Console.WriteLine($"Has macros: {info.HasMacros}");
Console.WriteLine($"VBA modules: {info.Exact.VbaProject?.Modules.Count ?? 0}");

foreach (SheetInfo sheet in info.Sheets)
{
    Console.WriteLine($"{sheet.SheetName}: {sheet.Tables.Count} tables, {sheet.MergedRegions.Count} merged regions");

    if (sheet.RowCount is { } rowCount)
        Console.WriteLine($"  Used range: {sheet.UsedRange}, {rowCount} rows");

    if (sheet.InferredHeaderRowIndex is { } headerRow)
        Console.WriteLine($"  Inferred header row: {headerRow}");
}

// One sheet at a selected level
SheetInfo observed = workbook.AnalyzeSheet("Sheet1", AnalysisLevel.Observed);
Console.WriteLine($"Used range: {observed.UsedRange}");
Console.WriteLine($"Columns with formulas: {string.Join(", ", observed.FormulaColumns ?? [])}");

// Async
WorkbookInfo infoAsync  = await workbook.AnalyzeAsync();
SheetInfo    sheetAsync = await workbook.AnalyzeSheetAsync("Sheet1");
```

`Exact` is always available. `Observed` and `Inferred` are `null` when the selected level does not
produce them, and so are the convenience properties that read from them. Use `TryGetObserved` or
`TryGetInferred` to get the complete objects.

## Parallelism

`Analyze` scans the sheets of a file-backed workbook in parallel by default. Stream-backed
workbooks are scanned one sheet at a time. See [concurrency.md](concurrency.md).

```csharp
// Default: one task per sheet, bounded by the processor count
WorkbookInfo parallel = workbook.Analyze();

// Sequential: reduces thread-pool pressure on busy servers
WorkbookInfo sequential = workbook.Analyze(maxDegreeOfParallelism: 1);

// Explicit cap
WorkbookInfo capped = await workbook.AnalyzeAsync(
    AnalysisLevel.Full,
    maxDegreeOfParallelism: 4);
```

## Column profiles

At `AnalysisLevel.Observed` and above, `SheetInfo.Columns` holds one `ColumnProfile` per column.
A profile has the dominant cell type, the inferred header, the non-empty count, an estimated
distinct-value count, and the numeric minimum and maximum.

For low-cardinality columns, `DistinctValues` lists the exact values, so a caller can pick filter
values without scanning the data. A column qualifies when its distinct count is at most
`AnalysisOptions.DistinctValuesCap` (default 32). Above the cap, `DistinctValues` is `null`; use
`DistinctValueEstimate` instead. Set the cap to 0 to turn the feature off.

```csharp
SheetInfo sheet = workbook.AnalyzeSheet("Data");

if (sheet.Columns is { } columns)
{
    foreach (ColumnProfile col in columns)
    {
        string header = col.InferredHeader ?? $"Col {col.ColumnIndex}";
        Console.WriteLine($"{header}: {col.DominantType}, {col.NonEmptyCount} rows, ~{col.DistinctValueEstimate} distinct");

        if (col.DistinctValues is { } values)
            Console.WriteLine($"  values: {string.Join(", ", values)}");

        if (col.MinNumericValue.HasValue)
            Console.WriteLine($"  range [{col.MinNumericValue} – {col.MaxNumericValue}]");
    }
}

// Raise the cap
var options = new AnalysisOptions { DistinctValuesCap = 50 };
SheetInfo wider = workbook.AnalyzeSheet("Data", options: options);
```

## VBA metadata

For macro-enabled `.xlsm` and `.xlsb` workbooks, XLSight reads the VBA project without running
any macros.

```csharp
using var workbook = ExcelWorkbook.Open("report.xlsm");

VbaProjectInfo? project = workbook.GetVbaProject();
if (project is not null)
{
    foreach (VbaModuleInfo module in project.Modules)
    {
        Console.WriteLine($"{module.Name}: {module.Kind}");
        string source = workbook.GetVbaModuleSource(module.Name);
    }
}
```

`GetVbaProject` returns project metadata without module source. `GetVbaModuleSource` and
`GetVbaModuleSourceBytes` decode one module when you ask for it.

## Data validations

At `AnalysisLevel.Exact` and above, `SheetInfo.DataValidations` lists the validation rules attached
to cells. Each `DataValidationInfo` has the validation type, operator, formula constraints,
target ranges, and the prompt text shown to users.

```csharp
SheetInfo input = workbook.AnalyzeSheet("Input");

foreach (DataValidationInfo dv in input.DataValidations)
{
    Console.WriteLine($"Type: {dv.Type}, Ranges: {string.Join(" ", dv.Ranges)}");

    if (dv.Formula1 is { } f1) Console.WriteLine($"  Formula1: {f1}");
    if (dv.Formula2 is { } f2) Console.WriteLine($"  Formula2: {f2}");
    if (dv.Operator is { } op) Console.WriteLine($"  Operator: {op}");
}
```

## External workbook links

`WorkbookInfo.ExternalLinks` lists references to other workbooks. Each link can include the
cached sheet names and defined names of the target.

```csharp
WorkbookInfo info = workbook.Analyze();

foreach (ExternalWorkbookLinkInfo link in info.ExternalLinks)
{
    Console.WriteLine($"Target: {link.Target}");
    Console.WriteLine($"  Sheets: {string.Join(", ", link.SheetNames)}");
    Console.WriteLine($"  Defined names: {string.Join(", ", link.DefinedNames)}");
}
```

## Formula dependencies

At `AnalysisLevel.Observed` and above, XLSight records which sheets and workbooks each formula
cell references. `SheetInfo.FormulaDependencies` counts the formulas per target.

```csharp
WorkbookInfo info = workbook.Analyze();

foreach (SheetInfo sheet in info.Sheets)
{
    foreach (FormulaDependencyInfo dep in sheet.FormulaDependencies ?? [])
    {
        string target = dep.TargetWorkbook is { } wb
            ? $"[{wb}]{dep.TargetSheet}"
            : dep.TargetSheet;
        Console.WriteLine($"{sheet.SheetName} → {target}: {dep.FormulaCount} formula(s)");
    }
}
```

## Worksheet layout

Core analysis finds regions and a header row. For a detailed map of axes, data blocks, and table
groups in unknown worksheets, use the optional
[XLSight.Layout](https://github.com/MagnusS0/XLSight/tree/master/src/XLSight.Layout) package.
