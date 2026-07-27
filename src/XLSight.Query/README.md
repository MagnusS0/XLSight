# XLSight.Query

A streaming, single-pass query layer for [XLSight](https://github.com/MagnusS0/XLSight):
answer *"sum of X by Y where Z"* over a region of a sheet without materializing the
sheet, without a database, and without adding any dependency beyond the core reader.

```csharp
using XLSight;
using XLSight.Query;
using static XLSight.Query.QueryAggregates;

using var workbook = ExcelWorkbook.Open("sales.xlsx");

QueryResult result = workbook
    .QueryRange("Sheet1", "A6:F2410", headerRow: 6)    // headers from row 6
    .Where("Region", QueryOperator.Equals, "EMEA")     // AND-combined filters
    .GroupBy("Month")
    .Select(Sum("NetSales"), Count())
    .Execute();

foreach (QueryResultRow row in result.Rows)
{
    Console.WriteLine($"{row.Values[0]}: {row.Values[1]} ({row.Values[2]} rows)");
}
```

The same query can also be executed from the XLSight Query DSL when a host
application or agent needs a portable text contract instead of compiled C#:

```csharp
QueryResult result = workbook.ExecuteQuery("""
    FROM "Sheet1"!A6:F2410 HEADER ROW 6
    SELECT SUM(NetSales), COUNT()
    WHERE Region = "EMEA"
    GROUP BY Month
    """);
```

## What it does

- **Single pass, bounded memory.** Filters, group-by, and aggregates are fused into one
  scan over borrowed rows; memory scales with group cardinality, never row count.
- **Operators:** `Where` (`Equals`/`NotEquals`/`LessThan`/`LessThanOrEqual`/`GreaterThan`/
  `GreaterThanOrEqual` over text, number, date, boolean literals), `GroupBy` (one column),
  `Select` (`Sum`/`Count`/`Min`/`Max`/`Average`), `Project` (raw column list for projected
  row results), `Take`, and a `DistinctValues(column, top)` terminal returning
  frequency-ordered value counts for filter discovery.
- **Row queries.** Without aggregates, `Execute()` returns the matching rows; with a
  `Take`, the scan stops as soon as enough rows matched.
- **Dirty data never throws.** Cells that don't coerce to an aggregate's input type are
  skipped and reported per column in `QueryResult.Unaggregatable`, with sample row indices.
- **Stats pruning.** Pass `AnalyzeSheet` column profiles via `WithStats(...)`: a numeric
  filter no value can satisfy returns an empty result without opening the sheet.
- **Guard rails.** Group/distinct cardinality is capped (default 10,000, configurable via
  `WithGroupLimit`); exceeding it throws `TooManyGroupsException` instead of exhausting memory.
- **Runtime Query DSL.** `ExecuteQuery(...)` parses a fixed-order SQL-like DSL into a
  safe `SheetQuerySpec`, then executes it through the same row-oriented query engine.

## Query DSL

Use the fluent API when writing .NET code directly. Use the DSL when queries need to
cross a process, config, prompt, or tool boundary without compiling C#.

```sql
FROM "Sheet1"!A6:F2410 HEADER ROW 6
SELECT *
WHERE Region = "EMEA" AND Units > 10
LIMIT 100
```

```sql
FROM "Sheet1"!A6:F2410 HEADER ROW 6
SELECT SUM(NetSales), COUNT()
WHERE Region = "EMEA" AND Units > 10
GROUP BY Month
LIMIT 100
```

Supported DSL features:

- `FROM <sheet>!<bounded-range>` with bare or quoted sheet names.
- `HEADER AUTO` or `HEADER ROW <number>`; the header row may sit above the `FROM` range's
  top row, but not below its bottom row.
- `SELECT *` for row results.
- `SELECT <column>[, <column>...]` for projected row results, in `SELECT` order.
- `SELECT COUNT()`, `SUM(column)`, `AVG(column)`, `MIN(column)`, `MAX(column)` for aggregate results.
- `WHERE` predicates joined by `AND`, using `=`, `!=`, `<`, `<=`, `>`, `>=`.
- Text, number, `DATE "yyyy-MM-dd"`, and boolean literals.
- One `GROUP BY` column.
- One `ORDER BY <key> [ASC|DESC]` on grouped results (the `GROUP BY` column or a selected
  aggregate), or on raw-row results (`SELECT *` or a raw column projection) when combined with
  `LIMIT`; `ASC` is the default.
- Optional positive integer `LIMIT`.

`FROM` selects the data window that gets scanned; `HEADER ROW n` only selects where
column names are read from, and the header row does not need to fall inside that window.
When the header sits above the range's top row, every row inside `FROM` is treated as
data — the rows between the header and the range top are never scanned, counted in
`RowsScanned`, or returned. This binds a header that lives above a data block without
widening the range to include it:

```sql
FROM "Sheet1"!B20:O35 HEADER ROW 4
SELECT *
```

When the header row sits inside the range, behavior is unchanged: rows before it are
ignored and data starts on the row after it. A header row below the range's bottom row
is rejected with `ArgumentOutOfRangeException`; a header row with no cells raises
`InvalidOperationException`.

`HEADER AUTO` benefits the same way: header inference now recognizes when a region's
header lives above the queried sub-range and binds that header, instead of falling back
to the first non-empty row inside `FROM`, which previously risked treating a data row as
column names.

`SELECT` also accepts a list of raw column names instead of `*` or aggregates. Result
columns follow `SELECT` order, not the sheet's column order, and column names follow the
same rules as elsewhere in the grammar — bare identifiers, or double-quoted when they
contain spaces or other non-identifier characters:

```sql
FROM "Sheet1"!A6:F2410 HEADER ROW 6
SELECT Region, NetSales
WHERE Units > 100
LIMIT 100
```

`WHERE` may filter on columns that aren't selected — here `Units` is used to filter rows
but isn't returned; the scan reads filter columns without projecting them. Selecting the
same column twice is allowed and yields two identical result columns. Selecting only the
columns you need avoids materializing the other cells in each row (no shared-string
resolution, no number parsing), so it's cheaper than `SELECT *` on wide sheets. The
fluent-API equivalent is `SheetQuery.Project("Region", "NetSales")`.

Raw columns cannot be mixed with aggregates in one `SELECT`; doing so is rejected at
parse time with `QueryDslException` ("Cannot mix raw columns and aggregates in one
SELECT. Select raw columns, SELECT *, or aggregates only."). Raw columns combined with
`GROUP BY` are rejected the same way ("GROUP BY requires aggregate selections. Raw
column projections cannot be grouped.").

`ORDER BY` sorts the materialized groups of a `GROUP BY` query before `LIMIT` truncates,
so the kept rows are the top (or bottom) *N* by the ordering key, not the first *N* seen:

```sql
FROM "Sheet1"!A6:F2410 HEADER ROW 6
SELECT SUM(NetSales), COUNT()
GROUP BY Region
ORDER BY SUM(NetSales) DESC
LIMIT 10
```

The key is either the `GROUP BY` column or one of the selected aggregates — matched by
function and column, so `ORDER BY AVG(NetSales)` resolves against a `SELECT AVG(NetSales)`
even though its result column is labeled `Average(NetSales)`. Empty aggregate results
(a group with no aggregatable cells) always sort last, in both `ASC` and `DESC` — an empty
result is never treated as the largest value. Ties break by first-seen group order, the
same order `LIMIT` uses without `ORDER BY`. `ORDER BY` does not raise the group cap:
a query that would exceed it still throws `TooManyGroupsException`, since a partial
aggregate can't be discarded before its group's last row is seen. `ORDER BY` on a global
aggregate without `GROUP BY` is rejected with `QueryDslException`.

`ORDER BY` also works on raw-row results (`SELECT *` or a raw column projection), but only
together with `LIMIT`:

```sql
FROM "Sheet1"!A6:F2410 HEADER ROW 6
SELECT *
ORDER BY NetSales DESC
LIMIT 10
```

Without `GROUP BY`, `LIMIT` is what bounds the memory a sort would otherwise need, so
`ORDER BY` on a raw-row result with no `LIMIT` is rejected the same way as the grouped case,
with the same message. The ordering column need not be selected — `SELECT Region ORDER BY
NetSales DESC LIMIT 10` is legal, ranking rows by `NetSales` while returning only `Region`.
This is a bounded top-N selection, not a full sort: memory is `O(LIMIT)`, the same cost
`LIMIT` already has without `ORDER BY`. The trade-off is that the early exit an unordered
`LIMIT` uses (stop once enough rows match) does not apply here — every matching row has to
be ranked against the ordering key to find the true top *N*, so `RowsScanned` covers every
matching row rather than stopping at `LIMIT`. Empties-last and the tie-break rule apply the
same as the grouped case, breaking ties by sheet row order instead of first-seen group order.

For lower-level host validation, parse without executing:

```csharp
SheetQuerySpec spec = SheetQuerySpec.Parse(queryText);
QueryResult result = workbook.ExecuteQuery(spec);
```

`HEADER COLUMN` is reserved for transposed tables. The parser recognizes it, but
execution rejects it until the engine has a dedicated transposed scan strategy.

## Using with AI agents

The Query DSL is designed to be the interface between an agent and an Excel file.
The agent receives a bounded, read-only query grammar, no arbitrary code, no writes,
no file system access beyond the single file, which makes it safe to expose as a tool
without a code sandbox. The host validates and executes the DSL; the agent never touches
the file directly.

A minimal tool set covers three operations: workbook discovery, sheet profiling, and
querying. Wire them up with `AIFunctionFactory.Create` and register them with your agent:

```csharp
using System.ComponentModel;
using XLSight;
using XLSight.Query;

// ── 1. workbook overview ───────────────────────────────────────────────────
[Description("List sheets and workbook-level metadata for an Excel file.")]
static string GetWorkbook(
    [Description("Absolute path to the .xlsx, .xlsm, or .xlsb file.")] string path)
{
    using var wb = ExcelWorkbook.Open(path);
    WorkbookInfo info = wb.Analyze(AnalysisLevel.Fast);
    // Reccomended: Don't naivly serialize the WorkbookInfo object to JSON
    // format it into something like a consise markdown with just what is needed
    // {Your own formatting function here}
    return FormatWorkbookInfo(info);
}

// ── 2. sheet profile ────────────────────────────────────────────────────────
[Description(
    "Profile one sheet: column names, dominant types, value ranges, and (for low-cardinality " +
    "columns) the exact distinct values. Call this before querying to discover column names " +
    "and filter values.")]
static string GetSheetOverview(
    [Description("Absolute path to the file.")] string path,
    [Description("Exact sheet name.")] string sheet)
{
    using var wb = ExcelWorkbook.Open(path);
    SheetInfo info = wb.AnalyzeSheet(sheet, AnalysisLevel.Full);
    // {Your own formatting function here}
    return FormatSheetInfo(info);
}

// ── 3. query ────────────────────────────────────────────────────────────────
[Description(
    "Run a read-only query against one sheet using the XLSight Query DSL. " +
    "Returns aggregate or row results. Requires column names — call GetSheetOverview first.")]
static string QuerySheet(
    [Description("Absolute path to the file.")] string path,
    [Description(
        "XLSight Query DSL. Examples:\n" +
        "  FROM Sales!A1:F500 HEADER AUTO SELECT SUM(Revenue), COUNT() WHERE Region = \"EMEA\" GROUP BY Month\n" +
        "  FROM Sheet1!A1:D200 HEADER ROW 1 SELECT * WHERE Status = \"Open\" LIMIT 50\n" +
        "  FROM Sheet1!B20:O35 HEADER ROW 4 SELECT * LIMIT 50\n" +
        "  FROM Sheet1!A1:D200 HEADER ROW 1 SELECT Region, NetSales WHERE Units > 100 LIMIT 50")]
    string query)
{
    using var wb = ExcelWorkbook.Open(path);
    QueryResult result = wb.ExecuteQuery(query);
    // convert result.Rows / result.Unaggregatable to a string the model can read
    // {Your own formatting function here}
    return FormatQueryResult(result);
}
```

Register the tools and run the agent loop with your chosen provider:

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

IList<AITool> tools =
[
    AIFunctionFactory.Create(GetWorkbook),
    AIFunctionFactory.Create(GetSheetOverview),
    AIFunctionFactory.Create(QuerySheet),
];

// pass tools to your IChatClient / AIAgent as usual
```

**Stats pruning (optional optimisation).**  If `GetSheetOverview` has already been
called, pass the column profiles into the query via `WithStats(...)` on the fluent API.
A numeric filter that no value in the profiled min/max range can satisfy returns an empty
result without opening the sheet at all:

```csharp
SheetInfo info = wb.AnalyzeSheet(sheet, AnalysisLevel.Full);

QueryResult result = wb
    .QueryRange(sheet, "A1:F500")
    .Where("Units", QueryOperator.GreaterThan, 1000)
    .Select(Sum("Revenue"))
    .WithStats(info.Columns!)   // skips the scan when no column value can match
    .Execute();
```

For filter discovery beyond what `GetSheetOverview` returns, add a fourth tool that
calls `QueryRange(...).DistinctValues("ColumnName")` and returns the top-N values with
their frequencies.
