# Workbook backing and concurrency

The stream behind a workbook controls which operations can run at the same time.

A workbook is **file-backed** when its stream is a `FileStream`. This is true for
`Open(filePath)`/`OpenAsync(filePath)`, and also for `Open(stream)`/`OpenAsync(stream)` when you
pass a `FileStream`. Any other stream makes the workbook **stream-backed**.

| | File-backed | Stream-backed |
|---|---|---|
| **Concurrent operations** | Yes. Each concurrent operation uses its own `ZipArchive`; idle archives are pooled. | No. A second concurrent operation throws `InvalidOperationException`. |
| **`Analyze` parallelism** | Scans sheets in parallel by default. | Scans sheets one at a time. |
| **Concurrent `StreamSheet*` enumerations** | Yes. | One at a time. |
| **Non-seekable input** | Not applicable. | `OpenAsync(stream)` copies the stream into memory. `Open(stream)` throws. |

Prefer opening from a file path. Use the stream overloads when you already hold an in-memory or
network stream.

```csharp
// File-backed: concurrent reads on this instance are safe
using var fileBacked = ExcelWorkbook.Open("report.xlsx");

// Stream-backed: one operation at a time
await using var streamBacked = await ExcelWorkbook.OpenAsync(networkStream);
```

On a stream-backed workbook, an open `ExcelSheetReader` or an unfinished `StreamSheet*`/`StreamRange*`
enumeration counts as a running operation until it is disposed or completed.

Dispose readers and finish enumerations before you dispose the workbook.

> **ASP.NET Core:** each request can open its own file-backed `ExcelWorkbook` and use it without
> coordination. If several requests must share one stream-backed instance, serialize access to it.

To control `Analyze` parallelism, see [Analyzing workbooks](analysis.md#parallelism).
