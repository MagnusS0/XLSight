# XLSight

[![NuGet](https://img.shields.io/badge/nuget-v0.4.0-blue)](https://www.nuget.org/packages/XLSight/)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

XLSight is a high-performance, zero-dependency Excel (`.xlsx`, `.xlsm`, `.xlsb`) reader and analyzer for .NET 10.

XLSight bypasses `XmlReader` on the hot path. It scans raw UTF-8 byte streams with SIMD-accelerated `IndexOf`/`SearchValues<byte>` operations and stores shared strings in a chunked, LOH-free arena to minimize per-cell allocations and heap fragmentation.

- Processes the NYC 311 1M-row workbook in **4.10 s** with **157 MB** peak RSS using the public reader API, about **2.1x faster** than Rust's [`calamine`](https://github.com/tafia/calamine) and **4.7x faster** than both [`ExcelDataReader`](https://github.com/ExcelDataReader/ExcelDataReader) and [`MiniExcel`](https://github.com/mini-software/MiniExcel/tree/master).
- Reads the first 10 rows of a 1M-row sheet in about **300 μs** on both public streaming APIs. Use the borrowed reader for the lowest allocations, or the safe stream when you want independent row snapshots.

> **Scope note:** XLSight supports Open XML workbooks (`.xlsx`, `.xlsm`) and binary workbooks
> (`.xlsb`), including source-free VBA project metadata. It does not execute VBA macros and
> does not support legacy `.xls` or `.csv` files. Some comparison libraries cover those broader
> formats, so the benchmark tables compare equivalent `.xlsx` reads unless noted otherwise.


## Installation

```
dotnet add package XLSight
```

## Quick start

### Open a workbook

```csharp
using XLSight;

// Open from file path
using var workbookFromFile = ExcelWorkbook.Open("report.xlsx"); // .xlsx, .xlsm, and .xlsb are supported

// Open from a stream
using var workbookFromStream = ExcelWorkbook.Open(stream);

// Async variants
await using var workbookFromFileAsync = await ExcelWorkbook.OpenAsync("report.xlsx");
await using var workbookFromStreamAsync = await ExcelWorkbook.OpenAsync(stream);

// Workbook-level metadata
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
        Console.Write($"{c}\t");
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

Stream rows one at a time without loading the entire sheet into memory.
The `StreamSheet*` / `StreamRange*` APIs yield **independent row snapshots**, so they are safe
to buffer, materialize, and use with LINQ. This is the best default for most consumers:

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

If you want the absolute lowest-allocation path, use `GetSheetReader*` / `GetRangeReader*`.
`ExcelSheetReader.Current` is a **borrowed** row view over a reused internal buffer, so the
current row is only valid until the next successful call to `Read()` or `ReadAsync()`.
Use this when you process each row immediately in a hot loop; if you need to retain rows,
prefer `StreamSheet*` / `StreamRange*`:

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

`Analyze` / `AnalyzeSheet` returns structural metadata without requiring you to iterate cells yourself.
Use `AnalysisLevel` to control how much work is performed:

| Level | What is included |
|---|---|
| `Exact` | Metadata parsed from package XML: named ranges, tables, pivot tables, charts, merged regions, data validations, external workbook links, macros |
| `Observed` | Everything in `Exact` plus a streaming scan: used range, row/column counts, per-column type profiles, formula dependency aggregation |
| `Full` (default) | Everything in `Observed` plus inferred regions and header row index |

```csharp
using XLSight;
using XLSight.Analysis;

using var workbook = ExcelWorkbook.Open("report.xlsx");

// Analyze all sheets at once
WorkbookInfo info = workbook.Analyze();           // AnalysisLevel.Full by default
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

// Analyze a single sheet — with explicit level
SheetInfo s = workbook.AnalyzeSheet("Sheet1", AnalysisLevel.Observed);
Console.WriteLine($"Used range: {s.UsedRange}");
Console.WriteLine($"Columns with formulas: {string.Join(", ", s.FormulaColumns)}");

// Async variants
WorkbookInfo infoAsync  = await workbook.AnalyzeAsync();
SheetInfo    sheetAsync = await workbook.AnalyzeSheetAsync("Sheet1");
```

`Exact` is always populated. `Observed` and `Inferred` are `null` when that analysis work was
not requested, and the convenience properties (`RowCount`, `Columns`, `UsedRange`,
`FormulaColumns`, `InferredHeaderRowIndex`, and so on) return `null` instead of throwing.
Use `TryGetObserved` / `TryGetInferred` when you want the full sub-objects explicitly.

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
an estimated distinct-value count, and the numeric min/max — everything an agent or pipeline
needs to understand a sheet's schema without reading the data itself.

```csharp
SheetInfo sheet = workbook.AnalyzeSheet("Data");

if (sheet.Columns is { } columns)
{
    foreach (ColumnProfile col in columns)
    {
        string header = col.InferredHeader ?? $"Col {col.ColumnIndex}";
        Console.WriteLine($"{header}: {col.DominantType}, {col.NonEmptyCount} rows, ~{col.DistinctValueEstimate} distinct");

        if (col.MinNumericValue.HasValue)
            Console.WriteLine($"  range [{col.MinNumericValue} – {col.MaxNumericValue}]");
    }
}
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

`WorkbookInfo.ExternalLinks` lists all external workbook references discovered in the file,
including the cached sheet names and defined names stored inside each link part:

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

How you open a workbook determines its concurrency characteristics:

| | `Open(filePath)` / `OpenAsync(filePath)` | `Open(stream)` / `OpenAsync(stream)` |
|---|---|---|
| **Backing** | File-backed | Stream-backed |
| **Concurrent operations** | ✅ Safe — each read opens its own `ZipArchive` | ❌ One operation at a time |
| **`Analyze` parallelism** | ✅ Sheets scanned in parallel by default | ❌ Sequential only |
| **`StreamSheetAsync` iterations** | ✅ Multiple concurrent enumerations allowed | ❌ One enumeration at a time |
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

When analyzing file-backed workbooks, XLSight scans sheets in parallel by default.
Use `maxDegreeOfParallelism` to tune or disable this:

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

> **Numeric vs string-heavy files**: the SST is parsed lazily — only the entries referenced by the
> rows actually consumed are decoded. For numeric sheets the SST is tiny and contributes nothing;
> for string-heavy sheets only the handful of unique string indices in those 10 rows are resolved,
> keeping both time and allocation near the numeric baseline regardless of total file size.
>
> **ExcelDataReader** implements `IDataReader`, whose contract requires `FieldCount` and `RowCount`
> to be known before the first `Read()` call. To satisfy this, the worksheet constructor performs a
> mandatory pre-scan that reads through the entire `<sheetData>` section. All shared strings and
> styles are also loaded into memory at workbook-open time. There is no mechanism to exit earlier,
> so the full sheet is always processed even when only the first row is consumed.
>
> **MiniExcel** is a streaming XML reader, but `Query()` materialises each row as an `ExpandoObject`
> (`IDictionary<string, object>`). Every column slot — occupied or not — is pre-populated with a
> null entry before any cell data is written, so per-row cost scales with the sheet's column width
> rather than the number of non-empty cells. Every cell value is then boxed as `object?`.

### How XLSight achieves high performance

Most xlsx readers sit on top of `XmlReader` or a SAX event stream that fires a callback per XML
element, allocating a string for every attribute value it encounters. XLSight's sheet scanner and
shared-string parser bypass `XmlReader` entirely for the hot path. Instead,
`ReadOnlySpan<byte>.IndexOf` and `SearchValues<byte>` — backed by SIMD intrinsics in the .NET
runtime — locate `<row>`, `<c>`, `<v>`, `<f>`, and `<t>` tag boundaries directly in the
decompressed UTF-8 byte stream. A single 64 KB `ArrayPool<byte>`-backed sliding window (`ScanBuffer`)
is rented once per sheet open and reused for the full stream; no additional I/O buffers are allocated
during parsing.

Cell attributes (`r=`, `t=`, `s=`) are extracted in-place from byte spans by `CellAttributeParser`,
using `Utf8Parser.TryParse` to decode column references, integers, and floats without ever
constructing a managed string. Numbers, booleans, and shared-string indices all take this
zero-allocation path. Inline text and formula-result strings are the only cell types that produce a
heap string during decoding.

`ExcelCellValue` is a 24-byte `readonly struct` field-ordered to eliminate padding (8-byte `double`,
8-byte `string` reference, 4-byte `CellType`, 4-byte `int`). The borrowed `ExcelSheetReader`
reuses one `ExcelCellValue[]` row buffer for the full scan, keeping the hot path allocation-free
aside from decoded strings. `StreamSheet*` / `StreamRange*` build on top of that reader and
snapshot rows only when you choose the safe enumerable surface. `RangeResult` stores one flat
read-only cell buffer and projects cached `ExcelRow` views over slices of that memory instead of
copying per row. For analysis operations the scanner drives a push-based `struct` sink via a
generic struct constraint, bypassing the row-yield path entirely and reducing per-row heap
allocation to zero.

The shared-string table is built as a lazy UTF-8 arena: 64 KB byte-array chunks (below the 85 KB
LOH threshold) hold pre-decoded, entity-resolved UTF-8. A 256 KB `ArrayPool`-rented staging buffer
assembles each `<si>` entry inline and commits it atomically to the arena — this parser is also
byte-level, with no `XmlReader`. Entries are indexed via a packed `long[]` table (global offset +
byte length, 8 bytes per entry). The SST is parsed incrementally and on demand: a consumer that reads
only 10 rows causes only the handful of SST indices those rows reference to ever be decoded. A
low-index `string?[]` cache sized to `min(uniqueCount, 131,072)` retains repeated headers and
categorical values without over-allocating on small workbooks; high-index entries are materialised
directly from the arena on lookup and collected by Gen 0.

## Key design points

- **Zero dependencies** — only the .NET 10 BCL. `ZipArchive` handles the OOXML container; `XmlReader` parses one-time workbook metadata (styles, relationships); the sheet scanner and SST parser are custom byte-level engines that never invoke `XmlReader`.
- **AOT-compatible** — annotated for Native AOT and trimming from day one.
- **Dual streaming API** — `GetSheetReader*` exposes the lowest-allocation borrowed reader; `StreamSheet*` / `StreamRange*` snapshot rows automatically for safe enumeration and LINQ usage.
- **Read-only** — XLSight reads and analyzes `.xlsx`, `.xlsm`, and `.xlsb` files; it does not write them or execute macros.
- **Target framework** — .NET 10 (`net10.0`).

## License

MIT
