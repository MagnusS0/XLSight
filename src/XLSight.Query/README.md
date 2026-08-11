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

Run the same query from the XLSight Query DSL when a host or agent needs a portable
text contract instead of compiled C#:

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
  `Take`, the scan stops as soon as enough rows match.
- **Dirty data never throws.** Cells that do not coerce to an aggregate's input type are
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
- `HEADER AUTO` or `HEADER ROW <number>`. The header row may be above the `FROM` range's
  top row, but not below its bottom row.
- `SELECT *` for row results.
- `SELECT <column>[, <column>...]` for projected row results, in `SELECT` order.
- `SELECT COUNT()`, `SUM(column)`, `AVG(column)`, `MIN(column)`, `MAX(column)` for aggregate results.
- `WHERE` predicates joined by `AND`, using `=`, `!=`, `<`, `<=`, `>`, `>=`.
- Text, number, `DATE "yyyy-MM-dd"`, and boolean literals.
- One `GROUP BY` column.
- One `ORDER BY <key> [ASC|DESC]`, on grouped results or on raw-row results with a `LIMIT`.
  `ASC` is the default.
- Optional positive integer `LIMIT`.

`FROM` sets the data window to scan. `HEADER ROW n` sets where column names come from,
and that row does not have to be inside the window.

When the header is above the range's top row, every row inside `FROM` is data. The rows
between the header and the range top are never scanned, never counted in `RowsScanned`,
and never returned. This binds a header above a data block without widening the range to
include it:

```sql
FROM "Sheet1"!B20:O35 HEADER ROW 4
SELECT *
```

When the header row is inside the range, rows before it are ignored and data starts on
the next row. A header row below the range's bottom row throws
`ArgumentOutOfRangeException`. A header row with no cells throws
`InvalidOperationException`.

`HEADER AUTO` follows the same rule. It binds a region's header when that header is above
the queried sub-range, instead of the first non-empty row inside `FROM`.

`SELECT` also takes a list of raw column names instead of `*` or aggregates. Result
columns follow `SELECT` order, not sheet order. Column names are bare identifiers, or
double-quoted when they contain spaces or other non-identifier characters:

```sql
FROM "Sheet1"!A6:F2410 HEADER ROW 6
SELECT Region, NetSales
WHERE Units > 100
LIMIT 100
```

`WHERE` can filter on columns that `SELECT` does not return. Above, the scan reads `Units`
to filter rows but never projects it. The same column selected twice gives two identical
result columns. Selecting only the columns you need skips shared-string resolution and
number parsing on the rest of the row. That beats `SELECT *` on wide sheets. The fluent
equivalent is `SheetQuery.Project("Region", "NetSales")`.

One `SELECT` cannot mix raw columns with aggregates, and `GROUP BY` cannot take raw column
projections. Both throw `QueryDslException` at parse time.

`ORDER BY` sorts the groups of a `GROUP BY` query before `LIMIT` truncates. You keep the
top *N* by the ordering key, not the first *N* seen:

```sql
FROM "Sheet1"!A6:F2410 HEADER ROW 6
SELECT SUM(NetSales), COUNT()
GROUP BY Region
ORDER BY SUM(NetSales) DESC
LIMIT 10
```

The key is the `GROUP BY` column or one of the selected aggregates. Matching is on function
and column, so `ORDER BY AVG(NetSales)` finds `SELECT AVG(NetSales)` even though the result
column is named `Average(NetSales)`. `ORDER BY` does not raise the group cap, because the
scan cannot discard a partial aggregate before it reads that group's last row. A query over
the cap still throws `TooManyGroupsException`. `ORDER BY` on a global aggregate, with no
`GROUP BY`, throws `QueryDslException`.

`ORDER BY` also works on raw-row results, but only with `LIMIT`. Without `GROUP BY`,
`LIMIT` is what bounds the memory a sort would otherwise need:

```sql
FROM "Sheet1"!A6:F2410 HEADER ROW 6
SELECT *
ORDER BY NetSales DESC
LIMIT 10
```

The ordering column does not have to be selected. `SELECT Region ORDER BY NetSales DESC
LIMIT 10` ranks on `NetSales` and returns `Region`. This is a bounded top-N selection, not
a full sort, so memory stays `O(min(LIMIT, matching rows))` — an oversized `LIMIT` costs
only what the rows that actually matched need. The trade-off is that the row-mode early exit
no longer applies. The scan ranks every matching row to find the true top *N*, so
`RowsScanned` covers all of them.

Both forms use the same total order over cell values:

- Empty sorts last under `ASC` and `DESC`. It is never the largest value, unlike SQL `NULL`.
- Types rank numbers first, then dates, then booleans, then text, then errors. The rank is
  fixed, so a stray `n/a` in a numeric column sorts behind every number in both directions,
  and a column mixing serial numbers with dates groups all numbers ahead of all dates rather
  than interleaving them. `DESC` reverses magnitude within a type, so it is not an exact
  mirror of `ASC` on a mixed-type column.
- Ties break by first-seen group order, or by sheet row order for raw rows. This matches
  the order `LIMIT` uses on its own.

For lower-level host validation, parse without executing:

```csharp
SheetQuerySpec spec = SheetQuerySpec.Parse(queryText);
QueryResult result = workbook.ExecuteQuery(spec);
```

`HEADER COLUMN` is reserved for transposed tables. The parser recognizes it, but
execution rejects it until the engine has a dedicated transposed scan strategy.

## Using with AI agents

The Query DSL is the interface between an agent and an Excel file. The agent gets a
bounded, read-only grammar: no arbitrary code, no writes, and no file system access beyond
the one file. That makes it safe to expose as a tool without a code sandbox. The host
validates and executes the DSL, and the agent never touches the file.

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

**Stats pruning (optional optimisation).** If you already called `GetSheetOverview`, pass
the column profiles into the query with `WithStats(...)` on the fluent API. When no value
in the profiled min/max range can satisfy a numeric filter, the query returns an empty
result without opening the sheet:

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
