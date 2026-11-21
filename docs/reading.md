# Reading workbooks

This page covers opening workbooks, reading cells and ranges, streaming rows, and the
`ExcelCellValue` type. For concurrency rules, see [Workbook backing and concurrency](concurrency.md).

## Open a workbook

```csharp
using XLSight;

// From a file path (preferred; see concurrency.md)
using var fromFile = ExcelWorkbook.Open("report.xlsx");

// From a seekable stream
using var fromStream = ExcelWorkbook.Open(seekableStream);

// Async variants. OpenAsync(stream) also accepts non-seekable streams.
await using var fromFileAsync = await ExcelWorkbook.OpenAsync("report.xlsx");
await using var fromStreamAsync = await ExcelWorkbook.OpenAsync(networkStream);

// Workbook metadata
Console.WriteLine(string.Join(", ", fromFile.SheetNames)); // "Sheet1, Sheet2"
Console.WriteLine(fromFile.Format);      // Xlsx, Xlsm, or Xlsb
Console.WriteLine(fromFile.IsDate1904);
Console.WriteLine(fromFile.HasMacros);
```

- `Open(stream)` requires a seekable stream. It throws `InvalidOperationException` otherwise.
- `OpenAsync(stream)` copies a non-seekable stream into memory before it opens the workbook.
- The caller keeps ownership of a stream it passes in. Keep the stream open until the workbook is disposed.
- Sheet names and cell addresses are case-insensitive.

## Read a cell or range

```csharp
using var workbook = ExcelWorkbook.Open("report.xlsx");

// Single cell
ExcelCellValue cell = workbook.ReadCell("Sheet1", "B2");

// Typed address: no string parsing at the call site
ExcelCellValue sameCell = workbook.ReadCell("Sheet1", new ExcelAddress(2, 2));

// Range: one ExcelRow per row
RangeResult result = workbook.ReadRange("Sheet1", "A1:D10");
foreach (var row in result.Rows)
{
    foreach (var c in row.Cells)
    {
        Console.Write($"{c}\t");
    }

    Console.WriteLine();
}

// Typed range
RangeResult typedResult = workbook.ReadRange("Sheet1", ExcelRange.Parse("A1:D10"));

// Async equivalents
ExcelCellValue cellAsync  = await workbook.ReadCellAsync("Sheet1", "B2");
RangeResult    rangeAsync = await workbook.ReadRangeAsync("Sheet1", "A1:D10");
```

`RangeResult[rowOffset, colOffset]` uses 0-based offsets from the top-left cell of the range.
`ExcelRow.GetCell(column)` uses the 1-based absolute column index.

## Stream large sheets

`StreamSheet*` and `StreamRange*` read one row at a time without loading the full sheet. Each row
is an independent snapshot, so you can keep rows or use them with LINQ. This is the best default
for most code.

```csharp
await using var workbook = await ExcelWorkbook.OpenAsync("large.xlsx");

await foreach (var row in workbook.StreamSheetAsync("Sheet1"))
{
    Console.WriteLine($"Row {row.RowIndex}");
    foreach (var cell in row)              // ExcelRow is IEnumerable<ExcelCellValue>
        Console.Write($"{cell}\t");
    Console.WriteLine();
}

// Stream a typed range
await foreach (var row in workbook.StreamRangeAsync("Sheet1", ExcelRange.Parse("A1:C1000")))
{
    var name  = row.GetCell(1);   // 1-based column index
    var value = row.GetCell(3);
}

// Synchronous streaming
foreach (var row in workbook.StreamSheet("Sheet1"))
{
    ReadOnlySpan<ExcelCellValue> cells = row.Cells;
}
```

The streaming methods check their arguments when enumeration starts, not when you call them.

## Borrowed reader

`GetSheetReader*` and `GetRangeReader*` give the lowest allocation. `ExcelSheetReader.Current`
borrows an internal buffer that the reader reuses for every row.

`Current` stays valid until the next call to `Read`/`ReadAsync`, whatever that call returns, or
until the reader is disposed. Process each row before you read the next one.

```csharp
await using var reader = await workbook.GetSheetReaderAsync("Sheet1");

while (await reader.ReadAsync())
{
    ExcelRow current = reader.Current;
    ReadOnlySpan<ExcelCellValue> cells = current.Cells;
    runningTotal += Sum(cells);   // process the row before the next ReadAsync()
}
```

To keep a borrowed row after the next read, call `current.ToSnapshot()`.

## Address and range types

`ExcelAddress` and `ExcelRange` are value types. Construct them once and reuse them.

```csharp
ExcelAddress addr = ExcelAddress.Parse("B2");
ExcelRange   rng  = ExcelRange.Parse("A1:D10");

// Try-pattern: returns false on invalid input and does not throw
bool okAddress = ExcelAddress.TryParse("b2", out ExcelAddress addr2);
bool okRange   = ExcelRange.TryParse("A1:D10", out ExcelRange rng2);

// Construct directly
var addr3 = new ExcelAddress(column: 2, row: 2);                              // B2
var rng3  = new ExcelRange(new ExcelAddress(1, 1), new ExcelAddress(4, 10));  // A1:D10
```

## Read modes

`ReadMode` selects what a read returns. It applies to `ReadCell`, `ReadRange`, `StreamSheet`, and
`StreamRange`.

```csharp
// Values (default): decoded cached values
RangeResult values = workbook.ReadRange("Sheet1", "A1:D10", ReadMode.Values);

// Formulas: formula text for formula cells, decoded value for other cells
RangeResult formulas = workbook.ReadRange("Sheet1", "A1:D10", ReadMode.Formulas);
```

Formula text has no leading `=`. `ExcelCellValue.ToString()` adds it for display.

## Cell values

`ExcelCellValue` is a 24-byte readonly struct. Check `CellType`, then use the typed accessor.

```csharp
ExcelCellValue v = row.GetCell(2);

switch (v.CellType)
{
    case CellType.Number:  Console.WriteLine(v.AsNumber());  break;
    case CellType.Text:    Console.WriteLine(v.AsText());    break;
    case CellType.Date:    Console.WriteLine(v.AsDate());    break;
    case CellType.Boolean: Console.WriteLine(v.AsBoolean()); break;
    case CellType.Error:   Console.WriteLine(v.AsError());   break;
    case CellType.Formula: Console.WriteLine(v.AsFormula()); break;
    case CellType.Empty:   break;
}

// Try-pattern accessors do not throw
if (v.TryGetNumber(out double d)) { /* ... */ }
if (v.TryGetText(out string? t))  { /* ... */ }

// Shared-string id: within one workbook, equal ids mean equal text
if (v.TryGetSharedStringId(out int id)) { /* deduplicate by id */ }
```

## Exceptions

All XLSight exceptions derive from `ExcelException`.

| Type | Thrown when |
|---|---|
| `SheetNotFoundException` | The named sheet does not exist in the workbook. |
| `InvalidAddressException` | A cell address or range string cannot be parsed. |
| `RangeTooLargeException` | The requested range exceeds `ExcelLimits.MaxCells`. |
| `MalformedWorkbookException` | Workbook XML, relationship XML, or binary records are malformed. |

XLSight also lets these .NET exceptions through:

| Type | Thrown when |
|---|---|
| `InvalidDataException` | The file is not a ZIP package, or the ZIP package is corrupt. |
| `InvalidOperationException` | `Open(stream)` gets a non-seekable stream, or a stream-backed workbook gets a second concurrent operation. |
| `ObjectDisposedException` | The workbook is used after it is disposed. |

## Limits

`ExcelLimits` exposes the Excel grid size and the range size XLSight accepts:

```csharp
Console.WriteLine(ExcelLimits.MaxRows);    // 1,048,576
Console.WriteLine(ExcelLimits.MaxColumns); // 16,384
Console.WriteLine(ExcelLimits.MaxCells);   // 100,000,000
```
