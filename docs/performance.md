# Performance

Measured on 2026-10-31 on an Intel Core i9-14900K running Linux, using Release builds with
.NET 10.0.14 (SDK 10.0.114). XLSight uses `GetSheetReader` for streaming and `ReadRange` for
bounded reads. Ratios use the XLSight reader as 1× (`ReadRange` for bounded reads). XLSight
revisions: `662fe2f` for NYC; `d9846f9` for BenchmarkDotNet.

## Real-world benchmark: NYC 311 service requests, 1 M rows × 41 columns

Wall time and peak RSS were measured with a small Python script using `psutil`: 2 warmups,
5 measured runs, 10 ms sampling. **41,000,041 cells**.

> Calamine 0.37.0 (`zip` 9.2.0): Rust 1.98.0, `opt-level=3`, LTO, `codegen-units=1`, `target-cpu=native`. ExcelDataReader 3.9.0; MiniExcel 1.46.0.

| Library | Mean time | Stddev | Peak RSS |
|---|---:|---:|---:|
| **XLSight reader (.NET 10)** | **2.92 s** | **0.015 s** | 164 MiB |
| calamine (Rust) | 7.91 s (2.71×) | 0.036 s | **160 MiB** |
| ExcelDataReader | 17.28 s (5.92×) | 0.288 s | 291 MiB |
| MiniExcel[^1] | 20.67 s (7.09×) | 0.306 s | 396 MiB |

## BenchmarkDotNet: streaming throughput, all rows

Measured with [BenchmarkDotNet](https://benchmarkdotnet.org) 0.15.8: 3 warmups, 5 measured
iterations. The 100 K dataset is `tests/XLSight.Tests/TestData/large.xlsx`. The 1 M dataset is
the optional `xl_large.xlsx` fixture (about 985 K rows), which is not in the repository.

| Library | 100 K rows | 1 M rows | Allocated (100 K) | Allocated (1 M) |
|---|---:|---:|---:|---:|
| **XLSight reader** | **43.3 ms** | **1.30 s** | **87.6 KiB** | **1.46 GiB** |
| **XLSight safe stream** | **45.8 ms** | **1.32 s** | **13.8 MiB** | **1.66 GiB** |
| ExcelDataReader | 241.3 ms (5.6×) | 4.94 s (3.8×) | 118.9 MiB (1,389.2×) | 2.71 GiB (1.8×) |
| MiniExcel[^1] | 402.4 ms (9.3×) | 5.17 s (4.0×) | 1.00 GiB (11,925.5×) | 9.53 GiB (6.5×) |

> **Allocated** is total managed heap throughput (BenchmarkDotNet), not peak live RSS.

[^1]: MiniExcel uses `EnableSharedStringCache = false` to keep shared strings in memory.

## BenchmarkDotNet: bounded mid-sheet range

`complex_workbook.xlsx`, `Scenarios!B10:N20`: **11 rows × 13 columns**.

| Library | Time | Allocated |
|---|---:|---:|
| **XLSight `ReadRange`** | **121.8 μs** | **235.8 KiB** |
| ExcelDataReader | 689.1 μs (5.7×) | 466.8 KiB (2.0×) |
| MiniExcel[^1] | 653.5 μs (5.4×) | 945.7 KiB (4.0×) |

XLSight uses a bounded range API here. MiniExcel and ExcelDataReader iterate the sheet rows and
keep only the requested rectangle.

## BenchmarkDotNet: early exit, first 10 rows

The same datasets, stopping after 10 rows.

| Library | First 10 of 100 K | First 10 of 1 M | Allocated (100 K) | Allocated (1 M) |
|---|---:|---:|---:|---:|
| **XLSight reader** | **120.9 μs** | **218.9 μs** | **87.7 KiB** | **318.6 KiB** |
| **XLSight safe stream** | **122.7 μs** | **220.9 μs** | **89.2 KiB** | **320.9 KiB** |
| ExcelDataReader | 83.6 ms (691.9×) | 2.48 s (11,340.8×) | 21.9 MiB (256.3×) | 1.40 GiB (4,620.6×) |
| MiniExcel[^1] | 158.0 ms (1,306.8×) | 1.11 s (5,054.0×) | 482.6 MiB (5,637.5×) | 1.51 GiB (4,974.5×) |

## Reproduce the BenchmarkDotNet results

```bash
dotnet build benchmarks/XLSight.Benchmarks -c Release
dotnet benchmarks/XLSight.Benchmarks/bin/Release/net10.0/XLSight.Benchmarks.dll \
  --filter "*CompetitorStreamBenchmarks*" \
  --warmupCount 3 --iterationCount 5 --launchCount 1
```

Without arguments, the benchmark program runs every benchmark except the `XlLarge` ones. Pass
`--filter "*XlLarge*"` to run those; they need the `xl_large.xlsx` fixture.

## Why the gap

**XLSight** reads worksheet XML as UTF-8 bytes, reuses a typed row buffer, and creates managed
strings only when the caller asks for them. It stops after the requested rows.

**ExcelDataReader** scans worksheet data first to find the row and column counts. It also loads
shared strings and styles up front, so even a short read has a large setup cost.

**MiniExcel** loads all shared strings up front. `Query()` builds each row as an `ExpandoObject`
and then copies its values into another dictionary. It allocates a formula dictionary for every
row, even when there are no formulas, and it boxes numeric and other value-type cells.

## How XLSight reduces work

### Worksheet data

Most `.xlsx` readers parse worksheet data with a general-purpose XML parser. That parser must
support the full XML model and returns data through character and string APIs.

XLSight uses purpose-built scanners for worksheet data and shared strings. The scanners read
decompressed UTF-8 bytes and handle only the OOXML elements and attributes that XLSight needs.
`XmlReader` parses only the one-time workbook metadata: the workbook part, relationships, and styles.

`ReadOnlySpan<byte>.IndexOf` and `SearchValues<byte>` find the boundaries of `<row>`, `<c>`, `<v>`,
`<f>`, and `<t>` elements. `CellAttributeParser` reads the `r`, `t`, and `s` attributes from byte
spans. `Utf8Parser.TryParse` parses integer and floating-point values without temporary strings.

`ScanBuffer` rents a buffer from `ArrayPool<byte>` for each open sheet. It grows the buffer only
when a single token or row does not fit.

### Row storage

`ExcelCellValue` is a 24-byte `readonly struct`. `ExcelSheetReader` reuses one `ExcelCellValue[]`
buffer for all rows, so the borrowed reader does not allocate a cell array per row.

`StreamSheet*` and `StreamRange*` copy each row so the caller can keep it. `RangeResult` keeps
cells in one flat buffer and exposes cached `ExcelRow` views. Analysis sends cells to generic
`struct` sinks and does not create row objects.

### Shared strings

The shared-string parser stores decoded UTF-8 text in 64 KB arena chunks, below the 85 KB
large-object-heap threshold. It reuses one pooled staging buffer for every `<si>` element. Each
entry is one packed `long` that records the entry's offset and byte length.

The parser reads more shared-string entries only when a worksheet asks for a higher index. A
cache holds the managed strings for the first 131,072 indexes, which usually include headers and
category values. Higher indexes stay in the UTF-8 arena, and XLSight creates their strings on
each lookup. Those short-lived strings are collected in generation 0.
