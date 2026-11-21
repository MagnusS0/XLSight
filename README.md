# XLSight

[![NuGet](https://img.shields.io/badge/nuget-v0.8.6-blue)](https://www.nuget.org/packages/XLSight/)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://github.com/MagnusS0/XLSight/blob/master/LICENSE)

XLSight is a fast, dependency-free Excel reader and analyzer for .NET 10. It supports `.xlsx`,
`.xlsm`, and `.xlsb` files.

- Reads the NYC 311 workbook with one million rows in **2.92 s** with **164 MiB** peak RSS: **2.71×** faster than [`calamine`](https://github.com/tafia/calamine), **5.92×** faster than [`ExcelDataReader`](https://github.com/ExcelDataReader/ExcelDataReader), and **7.09×** faster than [`MiniExcel`](https://github.com/mini-software/MiniExcel). See [Performance](https://github.com/MagnusS0/XLSight/blob/master/docs/performance.md).
- Stops as soon as the caller has read the rows it needs, so sample reads are fast and allocate little.
- Analyzes workbook structure (tables, used ranges, column profiles, formula dependencies, VBA metadata) without loading cell data.
- Uses only the .NET 10 base class library, and supports Native AOT and trimming.

> **Scope:** XLSight reads Open XML and binary workbooks. It can inspect VBA metadata without
> running macros. It does not write workbooks, and it does not support legacy `.xls` or `.csv` files.

## Installation

```bash
dotnet add package XLSight
```

## Quick start

```csharp
using XLSight;

using var workbook = ExcelWorkbook.Open("report.xlsx");
Console.WriteLine(string.Join(", ", workbook.SheetNames));

// Read one cell
ExcelCellValue cell = workbook.ReadCell("Sheet1", "B2");

// Read a range
RangeResult range = workbook.ReadRange("Sheet1", "A1:D10");
foreach (ExcelRow row in range.Rows)
{
    Console.WriteLine(string.Join("\t", row));
}

// Stream a large sheet one row at a time
foreach (ExcelRow row in workbook.StreamSheet("Sheet1"))
{
    if (row.GetCell(1).TryGetText(out string? name))
        Console.WriteLine($"{row.RowIndex}: {name}");
}

// Analyze structure without reading cell data
WorkbookInfo info = workbook.Analyze();
foreach (var sheet in info.Sheets)
    Console.WriteLine($"{sheet.SheetName}: {sheet.UsedRange}");
```

The open, read, stream, and analysis methods have async variants, such as `OpenAsync`,
`ReadRangeAsync`, `StreamSheetAsync`, and `AnalyzeAsync`.

## Packages

| Package | Purpose |
|---|---|
| [XLSight](https://www.nuget.org/packages/XLSight/) | Read cells, ranges, and rows; analyze workbook structure. |
| [XLSight.Layout](https://github.com/MagnusS0/XLSight/tree/master/src/XLSight.Layout) | Infer label axes, data blocks, and table groups in unknown worksheets. |
| [XLSight.Query](https://github.com/MagnusS0/XLSight/tree/master/src/XLSight.Query) | Filter, group, aggregate, and order worksheet data in one streaming pass, from .NET code or a text query language. |

## Documentation

| Topic | Page |
|---|---|
| Opening workbooks, reading cells and ranges, streaming, cell values, exceptions | [Reading workbooks](https://github.com/MagnusS0/XLSight/blob/master/docs/reading.md) |
| Analysis levels, column profiles, VBA, validations, links, formula dependencies | [Analyzing workbooks](https://github.com/MagnusS0/XLSight/blob/master/docs/analysis.md) |
| File-backed vs stream-backed workbooks and concurrency | [Workbook backing and concurrency](https://github.com/MagnusS0/XLSight/blob/master/docs/concurrency.md) |
| Benchmarks, methodology, and how XLSight reduces work | [Performance](https://github.com/MagnusS0/XLSight/blob/master/docs/performance.md) |

## License

[MIT](https://github.com/MagnusS0/XLSight/blob/master/LICENSE)
