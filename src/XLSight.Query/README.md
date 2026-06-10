# XLSight.Query

A streaming, single-pass query layer for [XLSight](https://github.com/MagnusS0/XLSight):
answer *"sum of X by Y where Z"* over a region of a sheet without materializing the
sheet, without a database, and without adding any dependency beyond the core reader.

```csharp
using XLSight;
using XLSight.Query;

using var workbook = ExcelWorkbook.Open("sales.xlsx");

QueryResult result = workbook
    .QueryRange("Sheet1", "A6:F2410", headerRow: 6)    // headers from row 6
    .Where("Region", QueryOp.Equals, "EMEA")           // AND-combined filters
    .GroupBy("Month")
    .Aggregate(Agg.Sum("NetSales"), Agg.Count())
    .Execute();

foreach (QueryResultRow row in result.Rows)
{
    Console.WriteLine($"{row.Values[0]}: {row.Values[1]} ({row.Values[2]} rows)");
}
```

## What it does

- **Single pass, bounded memory.** Filters, group-by, and aggregates are fused into one
  scan over borrowed rows; memory scales with group cardinality, never row count.
- **Operators:** `Where` (`Equals`/`NotEquals`/`LessThan`/`LessThanOrEqual`/`GreaterThan`/
  `GreaterThanOrEqual` over text, number, date, boolean literals), `GroupBy` (one column),
  `Aggregate` (`Sum`/`Count`/`Min`/`Max`/`Avg`), `Limit`, and a `DistinctValues(column, top)`
  terminal returning frequency-ordered value counts for filter discovery.
- **Row queries.** Without aggregates, `Execute()` returns the matching rows; with a
  `Limit`, the scan stops as soon as enough rows matched.
- **Dirty data never throws.** Cells that don't coerce to an aggregate's input type are
  skipped and reported per column in `QueryResult.Unaggregatable`, with sample row indices.
- **Stats pruning.** Pass `AnalyzeSheet` column profiles via `WithStats(...)`: a numeric
  filter no value can satisfy returns an empty result without opening the sheet.
- **Guard rails.** Group/distinct cardinality is capped (default 10,000, configurable via
  `WithGroupLimit`); exceeding it throws `TooManyGroupsException` instead of exhausting memory.

## What it deliberately does not do

No SQL, no expressions, no computed columns, no joins, no ORDER BY, no writes. Filters
are `column op literal`; aggregates take a single column. Anything richer should escalate
to `ReadRange`/`StreamRange` or an external engine such as DuckDB.

## Agent recipe

1. `Analyze()` / `AnalyzeSheet()` — discover regions, headers, types, and (for
   low-cardinality columns) exact distinct values.
2. `ReadRange` — peek at a region when unsure.
3. `QueryRange(...).DistinctValues("Region")` — discover filter values beyond the
   analysis cap.
4. `QueryRange(...).Where(...).GroupBy(...).Aggregate(...)` — compute the answer in one pass.
