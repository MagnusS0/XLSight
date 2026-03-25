# XLSight

XLSight is a high-performance, zero-dependency, streaming Excel (.xlsx) reader and analyzer for .NET 10.

## Installation

```
dotnet add package XLSight
```

## Quick start

### ExcelWorkbook — full API

Open a workbook and read individual cells or ranges:

```csharp
using XLSight;

// Open from file path
using var workbook = ExcelWorkbook.Open("report.xlsx");

// Read a single cell
var cell = workbook.ReadCell("Sheet1", "B2");
Console.WriteLine(cell.Value);

// Read a range
var range = workbook.ReadRange("Sheet1", "A1:D10");
foreach (var row in range.Rows)
{
    foreach (var c in row.Cells)
        Console.Write($"{c.Value}\t");
    Console.WriteLine();
}

// Analyze a sheet (dimensions, column types, header inference)
var info = workbook.AnalyzeSheet("Sheet1");
Console.WriteLine($"Used range: {info.UsedRange}");
```

Async equivalents are available for all operations:

```csharp
await using var workbook = await ExcelWorkbook.OpenAsync("report.xlsx");
var range = await workbook.ReadRangeAsync("Sheet1", "A1:D10");
var info  = await workbook.AnalyzeSheetAsync("Sheet1");
```

### ExcelReader — convenience API

One-liner reads without managing workbook lifetime:

```csharp
using XLSight;

var cell  = ExcelReader.ReadCell("report.xlsx", "Sheet1", "B2");
var range = ExcelReader.ReadRange("report.xlsx", "Sheet1", "A1:D10");

// Async
var cell  = await ExcelReader.ReadCellAsync("report.xlsx", "Sheet1", "B2");
var range = await ExcelReader.ReadRangeAsync("report.xlsx", "Sheet1", "A1:D10");
```

### Streaming large sheets

Stream rows one at a time without loading the entire sheet into memory:

```csharp
using XLSight;

await using var workbook = await ExcelWorkbook.OpenAsync("large.xlsx");

await foreach (var row in workbook.StreamSheetAsync("Sheet1"))
{
    foreach (var cell in row.Cells)
        Console.Write($"{cell.Value}\t");
    Console.WriteLine();
}

// Stream a specific range
await foreach (var row in workbook.StreamRangeAsync("Sheet1", "A1:C1000"))
{
    // process row
}
```

### Read modes

Pass `ExcelReadMode` to control what data is returned:

```csharp
// Values only (default) — decoded cell values
var range = workbook.ReadRange("Sheet1", "A1:D10", ExcelReadMode.Values);

// Raw — unformatted strings, no type decoding
var range = workbook.ReadRange("Sheet1", "A1:D10", ExcelReadMode.Raw);
```

## Key design points

- **Zero dependencies** — only the .NET 10 BCL (ZipArchive + XmlReader).
- **AOT-compatible** — annotated for Native AOT and trimming from day one.
- **Streaming first** — rows are yielded as they are parsed; the full sheet is never held in memory.
- **Read-only** — XLSight reads and analyzes .xlsx files; it does not write them.
- **Target framework** — .NET 10 (`net10.0`).

## License

MIT
