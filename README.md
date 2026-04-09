# XLSight

[![NuGet](https://img.shields.io/badge/nuget-v0.1.0-blue)](https://www.nuget.org/packages/XLSight/)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

XLSight is a high-performance, zero-dependency, streaming Excel (.xlsx) reader and analyzer for .NET 10.

## Installation

```
dotnet add package XLSight
```

## Quick start

### Open a workbook

```csharp
using XLSight;

// Open from file path
using var workbook = ExcelWorkbook.Open("report.xlsx");

// Open from a stream
using var workbook = ExcelWorkbook.Open(stream);

// Async variants
await using var workbook = await ExcelWorkbook.OpenAsync("report.xlsx");
await using var workbook = await ExcelWorkbook.OpenAsync(stream);

// Workbook-level metadata
Console.WriteLine(string.Join(", ", workbook.SheetNames)); // ["Sheet1", "Sheet2"]
Console.WriteLine(workbook.IsDate1904);
Console.WriteLine(workbook.HasMacros);
```

### Read a cell or range

```csharp
using XLSight;

using var workbook = ExcelWorkbook.Open("report.xlsx");

// Single cell
var cell = workbook.ReadCell("Sheet1", "B2");
Console.WriteLine(cell.Value);

// Range
var result = workbook.ReadRange("Sheet1", "A1:D10");
foreach (var row in result.Rows)
{
    foreach (var cell in row.Cells)
        Console.Write($"{cell}\t");
    Console.WriteLine();
}

// Async equivalents
var cell  = await workbook.ReadCellAsync("Sheet1", "B2");
var range = await workbook.ReadRangeAsync("Sheet1", "A1:D10");
```

### Stream large sheets

Stream rows one at a time without loading the entire sheet into memory:

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

// Stream a specific range
await foreach (var row in workbook.StreamRangeAsync("Sheet1", "A1:C1000"))
{
    var name  = row.GetCell(1);   // column 1 by 1-based index
    var value = row.GetCell(3);
}

// Synchronous streaming is also available
foreach (var row in workbook.StreamSheet("Sheet1"))
{
    ReadOnlySpan<ExcelCellValue> cells = row.Cells;   // zero-copy span access
}
```

### Read modes

Pass `ReadMode` to control what data is returned:

```csharp
// Values (default) — decoded cached values: dates, numbers, text, booleans, errors
var range = workbook.ReadRange("Sheet1", "A1:D10", ReadMode.Values);

// Formulas — return formula text for formula cells; fall back to decoded value otherwise
var range = workbook.ReadRange("Sheet1", "A1:D10", ReadMode.Formulas);
```

`ReadMode` applies to `ReadCell`, `ReadRange`, `StreamSheet`, and `StreamRange`.

### Analyze a workbook

`Analyze` / `AnalyzeSheet` returns structural metadata without requiring you to iterate cells yourself.
Use `AnalysisLevel` to control how much work is performed:

| Level | What is included |
|---|---|
| `Exact` | Metadata parsed from package XML: named ranges, tables, pivot tables, charts, merged regions, macros |
| `Observed` | Everything in `Exact` plus a streaming scan: used range, row/column counts, per-column type profiles |
| `Full` (default) | Everything in `Observed` plus inferred header row index |

```csharp
using XLSight;
using XLSight.Models.Analysis;

using var workbook = ExcelWorkbook.Open("report.xlsx");

// Analyze all sheets at once
WorkbookInfo info = workbook.Analyze();           // AnalysisLevel.Full by default
Console.WriteLine($"Tables: {info.Tables.Count}");
Console.WriteLine($"Has macros: {info.HasMacros}");

foreach (SheetInfo sheet in info.Sheets)
{
    Console.WriteLine($"{sheet.SheetName}: {sheet.UsedRange}, {sheet.RowCount} rows");
    Console.WriteLine($"  Inferred header row: {sheet.InferredHeaderRowIndex}");
    Console.WriteLine($"  Merged regions: {sheet.MergedRegions.Count}");
}

// Analyze a single sheet — with explicit level
SheetInfo s = workbook.AnalyzeSheet("Sheet1", AnalysisLevel.Observed);
Console.WriteLine($"Used range: {s.UsedRange}");
Console.WriteLine($"Columns with formulas: {string.Join(", ", s.FormulaColumns)}");

// Async variants
WorkbookInfo info  = await workbook.AnalyzeAsync();
SheetInfo    sheet = await workbook.AnalyzeSheetAsync("Sheet1");
```

### Column profiles

`SheetInfo.Columns` gives a per-column profile available at `AnalysisLevel.Observed` and above.
Each `ColumnProfile` captures the dominant cell type, inferred header, non-empty count,
an estimated distinct-value count, and the numeric min/max — everything an agent or pipeline
needs to understand a sheet's schema without reading the data itself.

```csharp
SheetInfo sheet = workbook.AnalyzeSheet("Data");

foreach (ColumnProfile col in sheet.Columns)
{
    string header = col.InferredHeader ?? $"Col {col.ColumnIndex}";
    Console.WriteLine($"{header}: {col.DominantType}, {col.NonEmptyCount} rows, ~{col.DistinctValueEstimate} distinct");

    if (col.MinNumericValue.HasValue)
        Console.WriteLine($"  range [{col.MinNumericValue} – {col.MaxNumericValue}]");
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

## Using XLSight with AI agents

A common agent pattern: receive an uploaded file, understand its structure, then stream only
the data that is relevant to the task — without loading the entire sheet into memory.

```csharp
// Each request creates its own ExcelWorkbook instance.
// File-backed open means concurrent requests never block each other.
app.MapPost("/analyze-sheet", async (IFormFile upload) =>
{
    string tmp = Path.GetTempFileName() + ".xlsx";
    await using (var fs = File.Create(tmp))
        await upload.CopyToAsync(fs);

    await using var workbook = await ExcelWorkbook.OpenAsync(tmp);

    // Step 1 — understand the file without reading all data
    WorkbookInfo info = await workbook.AnalyzeAsync();
    SheetInfo sheet   = info.Sheets[0];

    // Step 2 — build a schema description for the LLM
    var schema = sheet.Columns.Select(c => new
    {
        header   = c.InferredHeader ?? $"Col{c.ColumnIndex}",
        type     = c.DominantType.ToString(),
        nonEmpty = c.NonEmptyCount,
        distinct = c.DistinctValueEstimate,
    });

    // Step 3 — stream only the rows the agent needs
    await foreach (var row in workbook.StreamSheetAsync(sheet.SheetName))
    {
        // row is yielded one at a time; the sheet is never held in memory
    }

    return Results.Ok(schema);
});
```

**Why this matters on a shared server:**

- Each `ExcelWorkbook` instance is independent. Ten concurrent users each get their own instance;
  file-backed workbooks open a separate `ZipArchive` per read so there is no coordination needed.
- Streaming allocation is flat — a 100 K-row sheet allocates roughly 343 KB regardless of row count,
  so ten concurrent reads cost ~3.4 MB, not 3.4 GB.
- `Analyze()` returns a complete schema in a single pass. Give the result to your LLM as context;
  only stream rows when the agent has decided what data it actually needs.

## Exceptions

| Type | Thrown when |
|---|---|
| `SheetNotFoundException` | Named sheet does not exist in the workbook |
| `InvalidAddressException` | Cell address or range string cannot be parsed |
| `RangeTooLargeException` | Requested range exceeds `ExcelLimits` |
| `MalformedWorkbookException` | ZIP package or XML structure is corrupt |

All exception types inherit from `ExcelException`.

## Performance

All benchmarks run on Linux .NET 10.0, Intel Core i9-14900K. Every library reads the same sheet and decodes all cell values.

### Real-world benchmark — NYC 311 service requests, 1 M rows × 41 cols

Wall time and peak RSS measured with psutil across 5 runs (2 warmup). MiniExcel measured with
`EnableSharedStringCache = false` (fully in-memory SST — the same memory model as the other libraries).

| Library | Mean time | Peak RSS |
|---|---:|---:|
| **XLSight (.NET 10)** | **4.59 s** | **169 MB** |
| calamine (Rust) | 8.37 s · 1.8× | 160 MB |
| ExcelDataReader | 18.71 s · 4.1× | 310 MB |
| MiniExcel | 18.63 s · 4.1× | 395 MB |

### BenchmarkDotNet — streaming throughput, all rows

Measured with [BenchmarkDotNet](https://benchmarkdotnet.org). The 100 K and 1 M datasets are
synthetic xlsx files with numeric and string columns. All libraries decode every cell value.

| Library | 100 K rows | 1 M rows | Allocated (100 K) | Allocated (1 M) |
|---|---:|---:|---:|---:|
| **XLSight** | **60 ms** | **1.49 s** | **343 KB** | **1.46 GB** |
| ExcelDataReader | 270 ms · 4.5× | 5.57 s · 3.7× | 165 MB · 491× | 3.44 GB · 2.3× |
| MiniExcel | 391 ms · 6.6× | 8.36 s · 5.6× | 877 MB · 2,615× | 10.56 GB · 7.2× |

> **Allocated** is total managed heap throughput (BenchmarkDotNet). XLSight's 1.46 GB for 1 M rows reflects
> strings materialised from the shared-string table; peak live memory (RSS) stays at 169 MB because
> short-lived strings are collected in Gen 0/1 before the process can grow further.

### BenchmarkDotNet — early exit, first 10 rows

Agents and pipelines often need only a few rows to sample a file or confirm its schema.
Because XLSight is a true streaming reader, it yields control immediately once the row limit is reached.

| Library | First 10 of 100 K | First 10 of 1 M | Allocated (100 K) | Allocated (1 M) |
|---|---:|---:|---:|---:|
| **XLSight** | **97 μs** | **294 μs** | **279 KB** | **1.5 MB** |
| ExcelDataReader | 98 ms · 1,012× | 2.67 s · 9,082× | 44.8 MB · 164× | 1.80 GB · 1,254× |
| MiniExcel | 169 ms · 1,740× | 935 ms · 3,180× | 483 MB · 1,770× | 1.68 GB · 1,173× |

> **Numeric vs string-heavy files**: the SST is parsed lazily — only the entries referenced by the
> rows actually consumed are decoded. For numeric sheets the SST is tiny and contributes nothing;
> for string-heavy sheets only the handful of unique string indices in those 10 rows are resolved,
> keeping both time and allocation near the numeric baseline regardless of total file size.
>
> **ExcelDataReader** processes a full SAX event stream under the hood; there is no mechanism to stop
> mid-stream, so it reads the entire sheet even when only the first row is consumed.
>
> **MiniExcel** is a streaming XML reader, but `Query()` materializes each row as an
> `IDictionary<string, object>` and boxes every cell value. Per-cell allocation scales linearly
> with the number of rows processed, which explains the large allocation figures.

### How XLSight achieves flat allocation

Most xlsx readers sit on top of an XML parser (`XmlReader` / SAX) that fires an event per element,
allocating a string or object for every attribute value encountered.
XLSight's inner loop works at the byte level: it uses `IndexOf` to locate `<row>` and `<c>` tag
boundaries directly in the raw UTF-8 byte stream, then decodes cell attributes into pooled stack
buffers — producing near-zero per-cell heap allocation regardless of sheet size.

The shared-string table is stored in a chunked UTF-8 arena (64 KB slabs, below the LOH threshold).
Strings are decoded on demand via a 131 K-entry index cache: low-index SST entries (headers, categorical
values) are cached with zero eviction; high-index entries bypass the cache and are collected by Gen 0.

## Key design points

- **Zero dependencies** — only the .NET 10 BCL (ZipArchive + XmlReader).
- **AOT-compatible** — annotated for Native AOT and trimming from day one.
- **Streaming first** — rows are yielded as they are parsed; the full sheet is never held in memory.
- **Read-only** — XLSight reads and analyzes .xlsx files; it does not write them.
- **Target framework** — .NET 10 (`net10.0`).

## License

MIT
