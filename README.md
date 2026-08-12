# XLSight

[![NuGet](https://img.shields.io/badge/nuget-v0.8.1-blue)](https://www.nuget.org/packages/XLSight/)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

XLSight is a fast, dependency-free Excel reader and analyzer for .NET 10. It supports `.xlsx`, `.xlsm`, and `.xlsb` files.

XLSight reads worksheet XML as UTF-8 bytes. It bypasses `XmlReader` on hot paths and creates managed strings only when required.

- Reads the NYC 311 workbook with one million rows in **4.10 s** with **157 MB** peak RSS. That is **2.1x faster** than Rust's [`calamine`](https://github.com/tafia/calamine) and **4.7x faster** than both [`ExcelDataReader`](https://github.com/ExcelDataReader/ExcelDataReader) and [`MiniExcel`](https://github.com/mini-software/MiniExcel/tree/master).
- Stops as soon as the caller has read the required N rows. This allows fast sample reads with minimal memory allocation.

> **Scope:** XLSight reads Open XML and binary workbooks. It can inspect VBA metadata without running macros.
> It does not support legacy `.xls` or `.csv` files. Benchmark tables compare equivalent `.xlsx` reads unless stated otherwise.


## Installation

```bash
dotnet add package XLSight
```

## Quick start

### Open a workbook

```csharp
using XLSight;

// Open from file path
using var workbookFromFile = ExcelWorkbook.Open("report.xlsx");

// Open from a stream
using var workbookFromStream = ExcelWorkbook.Open(stream);

// Async variants
await using var workbookFromFileAsync = await ExcelWorkbook.OpenAsync("report.xlsx");
await using var workbookFromStreamAsync = await ExcelWorkbook.OpenAsync(stream);

// Read workbook metadata
Console.WriteLine(string.Join(", ", workbookFromFile.SheetNames)); // "Sheet1, Sheet2"
Console.WriteLine(workbookFromFile.IsDate1904);
Console.WriteLine(workbookFromFile.HasMacros);
```

### Read a cell or range

```csharp
using XLSight;

using var workbook = ExcelWorkbook.Open("report.xlsx");

// Single cell — returns ExcelCellValue directly
ExcelCellValue cell = workbook.ReadCell("Sheet1", "B2");
Console.WriteLine(cell);

// Typed address overload — no string parsing at call site
ExcelCellValue cell2 = workbook.ReadCell("Sheet1", new ExcelAddress(2, 2));

// Addresses are case-insensitive
ExcelCellValue cell3 = workbook.ReadCell("Sheet1", "b2");

// Range — result.Rows gives one ExcelRow per row, consistent with streaming
RangeResult result = workbook.ReadRange("Sheet1", "A1:D10");
foreach (var row in result.Rows)
{
    foreach (var c in row.Cells)
    {
        Console.Write($"{c}\t");
    }

    Console.WriteLine();
}

// Typed range overload
var range = ExcelRange.Parse("A1:D10");
RangeResult result2 = workbook.ReadRange("Sheet1", range);

// Async equivalents
ExcelCellValue cellAsync   = await workbook.ReadCellAsync("Sheet1", "B2");
RangeResult    rangeAsync  = await workbook.ReadRangeAsync("Sheet1", "A1:D10");
```

### Stream large sheets safely

Read one row at a time without loading the full sheet. `StreamSheet*` and `StreamRange*` return independent row snapshots.

You can retain these rows or use them with LINQ. This is the best default for most consumers:

```csharp
using XLSight;

await using var workbook = await ExcelWorkbook.OpenAsync("large.xlsx");

await foreach (var row in workbook.StreamSheetAsync("Sheet1"))
{
    Console.WriteLine($"Row {row.RowIndex}");
    foreach (var cell in row)              // ExcelRow is IEnumerable<ExcelCellValue>
        Console.Write($"{cell}\t");
    Console.WriteLine();
}

// Stream a typed range — no string parsing
var range = ExcelRange.Parse("A1:C1000");
await foreach (var row in workbook.StreamRangeAsync("Sheet1", range))
{
    var name  = row.GetCell(1);   // 1-based column index
    var value = row.GetCell(3);
}

// Synchronous streaming — rows are independent; safe to buffer or pass to LINQ
foreach (var row in workbook.StreamSheet("Sheet1"))
{
    ReadOnlySpan<ExcelCellValue> cells = row.Cells;   // zero-copy span access
}
```

### Borrowed high-performance reader

Use `GetSheetReader*` or `GetRangeReader*` for the lowest allocation. `ExcelSheetReader.Current` borrows a reused internal buffer.

The current row stays valid until the next successful read. Process each row before you read the next row.

```csharp
await using var reader = await workbook.GetSheetReaderAsync("Sheet1");

while (await reader.ReadAsync())
{
    ExcelRow current = reader.Current;
    ReadOnlySpan<ExcelCellValue> cells = current.Cells;
    runningTotal += Sum(cells);   // process the row before the next ReadAsync()
}
```

If you ever need to keep a borrowed row past the next read, call `current.ToSnapshot()`.
In most application code, using `StreamSheet*` is simpler.

### Address and range types

`ExcelAddress` and `ExcelRange` are value types you can construct once and reuse across calls:

```csharp
// Parse from string (case-insensitive)
ExcelAddress addr = ExcelAddress.Parse("B2");
ExcelRange   rng  = ExcelRange.Parse("A1:D10");

// Try-pattern — returns false on invalid input, never throws
bool okAddress = ExcelAddress.TryParse("b2", out ExcelAddress addr2);
bool okRange   = ExcelRange.TryParse("A1:D10", out ExcelRange rng2);

// Construct directly
var addr3 = new ExcelAddress(column: 2, row: 2);   // B2
var rng3  = new ExcelRange(new ExcelAddress(1, 1), new ExcelAddress(4, 10));  // A1:D10
```

### Read modes

Pass `ReadMode` to control what data is returned:

```csharp
// Values (default) — decoded cached values: dates, numbers, text, booleans, errors
RangeResult valuesRange = workbook.ReadRange("Sheet1", "A1:D10", ReadMode.Values);

// Formulas — return formula text for formula cells; fall back to decoded value otherwise
RangeResult formulasRange = workbook.ReadRange("Sheet1", "A1:D10", ReadMode.Formulas);
```

`ReadMode` applies to `ReadCell`, `ReadRange`, `StreamSheet`, and `StreamRange`.

### Analyze a workbook

`Analyze` and `AnalyzeSheet` return workbook structure. Use `AnalysisLevel` to select the required work.

| Level | What is included |
|---|---|
| `Exact` | Package metadata, including names, tables, charts, merged cells, validation rules, links, and macros |
| `Observed` | `Exact` data plus used ranges, counts, column profiles, and formula dependencies |
| `Full` (default) | `Observed` data plus inferred regions and the inferred header row |

```csharp
using XLSight;
using XLSight.Analysis;

using var workbook = ExcelWorkbook.Open("report.xlsx");

// Analyze all sheets. Full analysis is the default.
WorkbookInfo info = workbook.Analyze();
Console.WriteLine($"Tables: {info.Tables.Count}");
Console.WriteLine($"Has macros: {info.HasMacros}");
Console.WriteLine($"VBA modules: {info.VbaProject?.Modules.Count ?? 0}");

foreach (SheetInfo sheet in info.Sheets)
{
    Console.WriteLine($"{sheet.SheetName}: {sheet.Tables.Count} tables, {sheet.MergedRegions.Count} merged regions");

    if (sheet.RowCount is { } rowCount)
        Console.WriteLine($"  Used range: {sheet.UsedRange}, {rowCount} rows");

    if (sheet.InferredHeaderRowIndex is { } headerRow)
        Console.WriteLine($"  Inferred header row: {headerRow}");
}

// Analyze one sheet at the selected level.
SheetInfo s = workbook.AnalyzeSheet("Sheet1", AnalysisLevel.Observed);
Console.WriteLine($"Used range: {s.UsedRange}");
Console.WriteLine($"Columns with formulas: {string.Join(", ", s.FormulaColumns)}");

// Use the asynchronous APIs.
WorkbookInfo infoAsync  = await workbook.AnalyzeAsync();
SheetInfo    sheetAsync = await workbook.AnalyzeSheetAsync("Sheet1");
```

`Exact` is always available. `Observed` and `Inferred` are `null` when the selected level does not create them.

Related convenience properties also return `null`. Use `TryGetObserved` or `TryGetInferred` to access the complete objects.

### VBA metadata

For macro-enabled `.xlsm` and `.xlsb` workbooks, XLSight can inspect the embedded VBA project
without executing any macros:

```csharp
using XLSight;
using XLSight.Analysis;

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

`GetVbaProject` returns source-free project metadata. `GetVbaModuleSource` and
`GetVbaModuleSourceBytes` decode an individual module on demand.

### Column profiles

`SheetInfo.Columns` gives a per-column profile available at `AnalysisLevel.Observed` and above.
Each `ColumnProfile` captures the dominant cell type, inferred header, non-empty count,
an estimated distinct-value count, the exact distinct values for low-cardinality columns,
and the numeric min/max — everything an agent or pipeline needs to understand a sheet's
schema without reading the data itself.

Low-cardinality columns additionally surface their exact distinct values (capped by
`AnalysisOptions.DistinctValuesCap`, default 32), so a consumer can pick filter values without
an exploratory scan. High-cardinality columns report `DistinctValues == null` — itself a signal
that the column is an ID or free-text column not worth enumerating.

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
```

`ColumnProfile.DistinctValues` is populated when a column's distinct count falls within
`AnalysisOptions.DistinctValuesCap` (default 32). High-cardinality columns leave it `null`
— use `DistinctValueEstimate` instead. Set `DistinctValuesCap = 0` to disable the feature entirely.

```csharp
var options = new AnalysisOptions { DistinctValuesCap = 50 };
SheetInfo sheet = workbook.AnalyzeSheet("Data", options);
```

### Infer worksheet layout (XLSight.Layout)

The optional [`XLSight.Layout`](src/XLSight.Layout/README.md) package finds structure in unknown worksheets.

It identifies labels, data blocks, value profiles, and logical tables. Use the result to select ranges and headers for [XLSight.Query](src/XLSight.Query/README.md).

```bash
dotnet add package XLSight.Layout
```

```csharp
using XLSight.Layout;

SheetLayoutInfo layout = workbook.AnalyzeLayout("Financials");
```

Layout analysis scans the selected worksheet. Core `Analyze` and `AnalyzeSheet` do not run these heuristics.

### Query a range (XLSight.Query)

The optional [`XLSight.Query`](src/XLSight.Query/README.md) package answers
*"sum of X by Y where Z"* in one streaming pass — no sheet materialization, no database.
Filters, a single-column group-by, and Sum/Count/Min/Max/Average aggregates are fused over
borrowed rows, so memory scales with group cardinality rather than row count. Dirty cells
never throw; they are skipped and reported per column with sample row indices.

```bash
dotnet add package XLSight.Query
```

```csharp
using XLSight.Query;
using static XLSight.Query.QueryAggregates;

QueryResult result = workbook
    .QueryRange("Sheet1", "A6:F2410", headerRow: 6)
    .Where("Region", QueryOperator.Equals, "EMEA")
    .GroupBy("Month")
    .Select(Sum("NetSales"), Count())
    .Execute();

// Filter discovery beyond the analysis cap: value → count, frequency-ordered.
var months = workbook.QueryRange("Sheet1", "A6:F2410").DistinctValues("Month");
```

### Data validations

Data validation rules attached to cells are available at `AnalysisLevel.Exact` and above.
Each `DataValidationInfo` carries the validation type, operator, formula constraints, allowed
ranges, and the UI text shown to users:

```csharp
SheetInfo sheet = workbook.AnalyzeSheet("Input");

foreach (DataValidationInfo dv in sheet.DataValidations)
{
    Console.WriteLine($"Type: {dv.Type}, Ranges: {string.Join(" ", dv.Ranges)}");

    if (dv.Formula1 is { } f1) Console.WriteLine($"  Formula1: {f1}");
    if (dv.Formula2 is { } f2) Console.WriteLine($"  Formula2: {f2}");
    if (dv.Operator is { } op) Console.WriteLine($"  Operator: {op}");
}
```

### External workbook links

WorkbookInfo.ExternalLinks lists external workbook references. Each item can include cached sheet names and defined names.

```csharp
WorkbookInfo info = workbook.Analyze();

foreach (ExternalWorkbookLinkInfo link in info.ExternalLinks)
{
    Console.WriteLine($"Target: {link.Target}");
    Console.WriteLine($"  Sheets: {string.Join(", ", link.SheetNames)}");
    Console.WriteLine($"  Defined names: {string.Join(", ", link.DefinedNames)}");
}
```

### Formula dependencies

At `AnalysisLevel.Observed` and above, XLSight tracks which sheets and workbooks each formula
cell references. `SheetInfo.FormulaDependencies` aggregates these into a per-target count,
giving a quick picture of how sheets are connected:

```csharp
WorkbookInfo info = workbook.Analyze();

foreach (SheetInfo sheet in info.Sheets)
{
    foreach (FormulaDependencyInfo dep in sheet.FormulaDependencies)
    {
        string target = dep.TargetWorkbook is { } wb
            ? $"[{wb}]{dep.TargetSheet}"
            : dep.TargetSheet;
        Console.WriteLine($"{sheet.SheetName} → {target}: {dep.FormulaCount} formula(s)");
    }
}
```

### Cell values

`ExcelCellValue` is a 24-byte readonly struct. Use `CellType` to discriminate and typed accessors to read:

```csharp
ExcelCellValue v = row.GetCell(2);

switch (v.CellType)
{
    case CellType.Number:  Console.WriteLine(v.AsNumber()); break;
    case CellType.Text:    Console.WriteLine(v.AsText());   break;
    case CellType.Date:    Console.WriteLine(v.AsDate());   break;
    case CellType.Boolean: Console.WriteLine(v.AsBoolean()); break;
    case CellType.Error:   Console.WriteLine(v.AsError());  break;
    case CellType.Formula: Console.WriteLine(v.AsFormula()); break;
    case CellType.Empty:   break;
}

// Try-pattern accessors never throw
if (v.TryGetNumber(out double d)) { /* ... */ }
if (v.TryGetText(out string? t))  { /* ... */ }

// Shared-string identity — useful for zero-allocation deduplication
if (v.TryGetSharedStringId(out int id)) { /* same id == same string object */ }
```

## File-backed vs stream-backed workbooks

The input type controls concurrency.

| | `Open(filePath)` / `OpenAsync(filePath)` | `Open(stream)` / `OpenAsync(stream)` |
|---|---|---|
| **Backing** | File-backed | Stream-backed |
| **Concurrent operations** | Yes. Each read opens a separate `ZipArchive`. | No. Run one operation at a time. |
| **`Analyze` parallelism** | Scans sheets in parallel by default. | Scans sheets in sequence. |
| **`StreamSheetAsync` iterations** | Supports concurrent enumerations. | Supports one enumeration at a time. |
| **Non-seekable input** | N/A | Buffered into `MemoryStream` automatically |

Use file-backed opening whenever you can. The stream overload is intended for cases where
you already hold an in-memory or network stream.

```csharp
// File-backed — concurrent reads are safe on this instance
using var workbook = ExcelWorkbook.Open("report.xlsx");

// Stream-backed — only one operation at a time; throws InvalidOperationException otherwise
await using var workbook = await ExcelWorkbook.OpenAsync(networkStream);
```

> **Note for ASP.NET Core:** multiple requests can each hold their own `ExcelWorkbook` instance
> opened from a file path and call it concurrently with no coordination needed.
> If you must share a single instance opened from a stream, serialize access yourself.

## Controlling analysis parallelism

XLSight scans file-backed sheets in parallel by default. Set maxDegreeOfParallelism to control this work.

```csharp
// Default: library chooses (one Task per sheet, bounded by processor count)
WorkbookInfo info = workbook.Analyze();

// Sequential — useful in heavily loaded servers to avoid ThreadPool pressure
WorkbookInfo info = workbook.Analyze(maxDegreeOfParallelism: 1);

// Explicit cap
WorkbookInfo info = await workbook.AnalyzeAsync(
    AnalysisLevel.Full,
    maxDegreeOfParallelism: 4);
```

## Exceptions

| Type | Thrown when |
|---|---|
| `SheetNotFoundException` | Named sheet does not exist in the workbook |
| `InvalidAddressException` | Cell address or range string cannot be parsed |
| `RangeTooLargeException` | Requested range exceeds `ExcelLimits.MaxCells` |
| `MalformedWorkbookException` | ZIP package or XML structure is corrupt |

## Limits

`ExcelLimits` exposes the bounds XLSight enforces:

```csharp
Console.WriteLine(ExcelLimits.MaxRows);    // 1,048,576
Console.WriteLine(ExcelLimits.MaxColumns); // 16,384
Console.WriteLine(ExcelLimits.MaxCells);   // 100,000,000
```

## Performance

All benchmarks were run on Linux, .NET 10.0, Intel Core i9-14900K. Every library reads the same
sheet and touches the same rows and cells. XLSight benchmarks use the relevant
public API for each scenario: `GetSheetReader` for forward-only streaming and `ReadRange` for
bounded rectangular reads.

### Real-world benchmark — NYC 311 service requests, 1 M rows × 41 cols

Wall time and peak RSS were measured with a small Python script using `psutil` across 5 runs
(2 warmup).

All four harnesses processed the same workload: **41,000,041 cells**.

| Library | Mean time | Stddev | Peak RSS |
|---|---:|---:|---:|
| **XLSight reader (.NET 10)** | **4.10 s** | **0.004 s** | **157 MB** |
| calamine (Rust) | 8.69 s · 2.1× | 0.109 s | 160 MB |
| ExcelDataReader | 19.27 s · 4.7× | 0.140 s | 310 MB |
| MiniExcel[^1] | 19.11 s · 4.7× | 0.178 s | 395 MB |

### BenchmarkDotNet — public streaming throughput, all rows

Measured with [BenchmarkDotNet](https://benchmarkdotnet.org). The 100 K and 1 M datasets are
synthetic xlsx files with numeric and string columns.

| Library | 100 K rows | 1 M rows | Allocated (100 K) | Allocated (1 M) |
|---|---:|---:|---:|---:|
| **XLSight reader** | **59.3 ms** | **1.51 s** | **343 KB** | **1.46 GB** |
| **XLSight safe stream** | **62.0 ms** | **1.56 s** | **14.1 MB** | **1.66 GB** |
| ExcelDataReader | 268.9 ms · 4.5× | 5.44 s · 3.6× | 165 MB · 492.6× | 3.43 GB · 2.3× |
| MiniExcel[^1] | 387.1 ms · 6.5× | 4.85 s · 3.2× | 885 MB · 2,642.1× | 7.54 GB · 5.2× |

> **Allocated** is total managed heap throughput (BenchmarkDotNet), not peak live RSS.

[^1]: All MiniExcel benchmarks use `EnableSharedStringCache = false` (fully in-memory SST — the same memory model as every other library measured here).

### BenchmarkDotNet — bounded mid-sheet range

This scenario reads `Scenarios!B10:N20` (**11 rows × 13 columns**) from the middle of
`complex_workbook.xlsx`. It models the case where the caller wants one table-like region,
not the whole sheet.

| Library | Time | Allocated |
|---|---:|---:|
| **XLSight `ReadRange`** | **127.0 μs** | **425 KB** |
| MiniExcel[^1] | 596.6 μs · 4.7× | 839 KB · 2.0× |
| ExcelDataReader | 735.5 μs · 5.8× | 614 KB · 1.4× |

> XLSight can use a true bounded range API here; MiniExcel and ExcelDataReader still iterate sheet
> rows and then consume just the requested rectangle.

### BenchmarkDotNet — early exit, first 10 rows

Agents and pipelines often need only a few rows to sample a file or confirm its schema. XLSight
yields control immediately once the row limit is reached.

| Library | First 10 of 100 K | First 10 of 1 M | Allocated (100 K) | Allocated (1 M) |
|---|---:|---:|---:|---:|
| **XLSight reader** | **97.1 μs** | **301.8 μs** | **279 KB** | **1.48 MB** |
| **XLSight safe stream** | **96.4 μs** | **297.6 μs** | **281 KB** | **1.48 MB** |
| ExcelDataReader | 96.7 ms · 995.9× | 2.68 s · 8,880.1× | 44.8 MB · 164.4× | 1.80 GB · 1,245.4× |
| MiniExcel[^1] | 170.2 ms · 1,752.8× | 1.13 s · 3,744.2× | 483 MB · 1,772.7× | 1.51 GB · 1,044.8× |

> **Numeric and text files:** XLSight parses shared strings on demand. It decodes only the entries used by the selected rows.
>
> **ExcelDataReader:** Its `IDataReader` contract needs `FieldCount` and `RowCount` before the first read.
> It scans the full `<sheetData>` section first. It also loads all shared strings and styles when it opens the workbook.
>
> **MiniExcel:** `Query()` creates each row as an `ExpandoObject`. It creates one dictionary entry for every column and boxes each cell value.

### How XLSight reduces work

#### Worksheet data

Most `.xlsx` readers use a general-purpose XML parser for worksheet data. This parser must support
the full XML model and expose data through character and string APIs.

XLSight uses purpose-built scanners for worksheet data and shared strings. The scanners read
decompressed UTF-8 bytes and handle only the required OOXML elements and attributes.

`ReadOnlySpan<byte>.IndexOf` and `SearchValues<byte>` find the boundaries of `<row>`, `<c>`, `<v>`,
`<f>`, and `<t>` elements. `CellAttributeParser` reads the `r`, `t`, and `s` attributes from byte
spans. `Utf8Parser.TryParse` parses integer and floating-point values without temporary strings.

`ScanBuffer` rents one 64 KB buffer from `ArrayPool<byte>` for each open sheet. It reuses this buffer
for the complete scan. The scanner does not allocate more I/O buffers during the scan.

#### Row storage

`ExcelCellValue` is a 24-byte `readonly struct` with no padding. `ExcelSheetReader` reuses one
`ExcelCellValue[]` buffer for all rows. The borrowed reader does not allocate a new cell array for
each row.

`StreamSheet*` and `StreamRange*` copy each row when the caller selects the safe enumerable API.
`RangeResult` keeps cells in one flat buffer and exposes cached `ExcelRow` views. Analysis operations
send cells to generic `struct` sinks and do not create row objects.

#### Shared strings

The shared-string parser stores resolved UTF-8 text in 64 KB arena chunks. It rents one 256 KB
staging buffer and reuses it for each `<si>` element. Each packed `long` records the global offset
and byte length of one entry.

The parser reads more shared-string entries only when a worksheet requests a higher index. A cache
holds at most 131,072 low-index strings, such as headers and category values. High-index entries
remain in the UTF-8 arena. XLSight creates their managed strings on demand, and Gen 0 can collect
them.

## Key design points

- **Zero dependencies** — only the .NET 10 BCL. `ZipArchive` handles the OOXML container; `XmlReader` parses one-time workbook metadata (styles, relationships); the sheet scanner and SST parser are custom byte-level engines that never invoke `XmlReader`.
- **AOT-compatible** — annotated for Native AOT and trimming from day one.
- **Dual streaming API** — `GetSheetReader*` exposes the lowest-allocation borrowed reader; `StreamSheet*` / `StreamRange*` snapshot rows automatically for safe enumeration and LINQ usage.
- **Read-only** — XLSight reads and analyzes `.xlsx`, `.xlsm`, and `.xlsb` files; it does not write them or execute macros.
- **Target framework** — .NET 10 (`net10.0`).

## License

MIT
