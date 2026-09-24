# XLSight.Query

XLSight.Query adds single-pass queries to
[XLSight](https://github.com/MagnusS0/XLSight). It can filter, group, aggregate,
project, and order worksheet data without a database.

## Installation

```bash
dotnet add package XLSight.Query
```

## Quick start

Use the fluent API in .NET code:

```csharp
using XLSight;
using XLSight.Query;
using static XLSight.Query.QueryAggregates;

using var workbook = ExcelWorkbook.Open("sales.xlsx");

QueryResult result = workbook
    .QueryRange("Sheet1", "A6:F2410", headerRow: 6)
    .Where("Region", QueryOperator.Equals, "EMEA")
    .GroupBy("Month")
    .Select(Sum("NetSales"), Count())
    .Execute();

foreach (QueryResultRow row in result.Rows)
{
    Console.WriteLine($"{row.Values[0]}: {row.Values[1]} ({row.Values[2]} rows)");
}
```

Use the Query DSL when a host or agent needs a text format:

```csharp
QueryResult result = workbook.ExecuteQuery("""
    FROM "Sheet1"!A6:F2410 HEADER ROW 6
    SELECT SUM(NetSales), COUNT()
    WHERE Region = "EMEA"
    GROUP BY Month
    """);
```

For grouped results, the group key is the first result column. Selected
aggregates follow in `SELECT` order.

## Features

- The query engine processes each data row once. Filters, grouping, and
  aggregates run during the same scan.
- Grouped queries store one state per group. They do not store every source row.
- Row queries return matching rows. `Take`, or an unordered DSL `LIMIT`, can stop
  the scan after it gets enough rows.
- `Where` supports text, numbers, dates, and Boolean values.
- `Select` supports `Sum`, `Count`, `Min`, `Max`, and `Average`.
- `Project` returns selected source columns without aggregates.
- `DistinctValues` returns common values and their frequencies.
- `WithStats` can reject an impossible numeric filter before the sheet opens.
- The default group and distinct-value limit is 10,000. `WithGroupLimit` changes
  this limit.
- Invalid aggregate inputs do not stop the query. `QueryResult.Unaggregatable`
  reports the affected columns and sample row indices.

## Query DSL

Use the fluent API for simple built-in operations in .NET code. The expression
features below are available through the DSL, including from .NET through
`SheetQuerySpec.Parse` and `ExecuteQuery(spec)`. Parsing once avoids reparsing a
query on repeated execution.

The DSL accepts both the traditional `FROM`-first form and the SQL-shaped
`SELECT`-first form:

```text
FROM ... HEADER ... SELECT ... [WHERE ...] [GROUP BY ...] [HAVING ...] [ORDER BY ...] [LIMIT ...]
SELECT ... FROM ... HEADER ... [WHERE ...] [GROUP BY ...] [HAVING ...] [ORDER BY ...] [LIMIT ...]
```

Supported clauses:

- `FROM <sheet>!<bounded-range>` selects one sheet and range. Sheet names can be
  bare or double-quoted.
- `HEADER AUTO` or `HEADER ROW <number>` selects the source of column names.
- `SELECT *` returns all source columns.
- `SELECT <expression>[, <expression>...]` returns selected source columns or
  computed values. `AS <alias>` names a result column.
- `SELECT COUNT()`, `SUM(column)`, `AVG(column)`, `MIN(column)`, and `MAX(column)`
  return aggregates.
- `WHERE` supports parentheses, `AND`, `OR`, `NOT`, `IN`, `IS EMPTY`, `IS NULL`,
  comparisons, and comparisons between columns. A `FILTER (WHERE ...)` clause
  on an aggregate applies that aggregate's own row predicate.
- Arithmetic expressions support `+`, `-`, `*`, `/`, and `%`, with the usual
  precedence. `YEAR`, `MONTH`, `DAY`, and `DATE_TRUNC` provide date operations.
- Use single quotes for text literals, such as `Region = 'EMEA'`. Use
  `[Column Name]` for an identifier that must be unambiguously read as a column
  reference; double quotes remain supported for sheet names and legacy quoted
  identifiers.
- `GROUP BY` accepts one or more expressions. Grouped execution retains state
  per group. Ordered results with `LIMIT` retain only the best result rows
  during finalization; the limit does not reduce the preceding group state.
  Without a result limit, finalization materializes all groups passing `HAVING`.
- `HAVING` filters completed groups and may refer to selected aggregate aliases.
- `ORDER BY <key> [ASC|DESC]` orders grouped or row results. `ASC` is the default.
- `LIMIT` accepts a nonnegative integer. `LIMIT 0` binds headers and returns no
  data rows; `HEADER AUTO` may still inspect workbook metadata to identify the
  header row.

### Expressions and value semantics

The expression executor uses typed values. A comparison between incompatible
types, or one involving an empty or invalid value, evaluates to unknown. `WHERE`
keeps only true rows, and `NOT` preserves unknown as unknown instead of turning
it into a match. This keeps missing or dirty cells out of both positive and
negated filters.

Invalid arithmetic, division by zero, and non-finite numeric results produce an
empty result value for that row. Empty aggregate inputs are skipped. Nonempty
values rejected by an aggregate's typed-input rules are reported through
`QueryResult.Unaggregatable`; arithmetic failures converted to empty do not
currently have a separate diagnostic counter.

`COALESCE` returns the first nonempty argument. `ABS` accepts numbers. `ROUND`
accepts an optional integer precision from 0 to 15 and rounds midpoint values
away from zero. Arithmetic uses `double`, not exact decimal arithmetic.
`DATE_TRUNC` accepts the literal units `'year'`, `'month'`, and `'day'`.

`IN` uses typed equality. If no value matches and any comparison is unknown
(including an empty or incompatible literal), the result is unknown; `NOT IN`
preserves that result. `IS NULL` and `IS EMPTY` both test missing values, not
zero-length text. Boolean ordering comparisons are unsupported.

```sql
SELECT Region, Units * 1.15 AS WeightedUnits
FROM "Sheet1"!A6:F2410 HEADER ROW 6
WHERE Region IN ('EMEA', 'APAC') AND NOT (Units IS EMPTY)
```

```sql
FROM "Sheet1"!A6:F2410 HEADER ROW 6
SELECT Region, SUM(NetSales) FILTER (WHERE OnPromo = TRUE) AS PromoSales
GROUP BY Region
HAVING PromoSales > 100
```

### Header rows

`FROM` sets the data range. `HEADER ROW n` sets the row that contains column
names.

The header row can be above the top of the data range. In this case, every row
in the `FROM` range is data. The query does not scan or count rows between the
header and the data range.

When the header is inside the range, the query ignores earlier rows in that
range. Data starts on the row after the header.

A header below the range throws `ArgumentOutOfRangeException`. A header with no
cells throws `InvalidOperationException`.

`HEADER AUTO` uses inferred table and crosstab regions. It can use an inferred
header above a bounded range when the region overlaps that range. If no region
matches, it uses the first non-empty row in the range.

`HEADER COLUMN` is reserved for transposed tables. The parser accepts this
syntax, but execution rejects it.

```sql
FROM "Sheet1"!B20:O35 HEADER ROW 4
SELECT *
```

### Projected columns

`SELECT` can contain source column names. Result columns follow `SELECT` order,
not worksheet order. Use double quotes around names that contain spaces or
special characters.

Column matching prefers an exact case match. If none exists, it uses the first
case-insensitive match. Blank header cells use their Excel column labels.

`WHERE` and `ORDER BY` can use a column that `SELECT` does not return. Selecting
the same column twice creates two equal result columns.

A projection lets the scanner skip text and number conversion for unused
columns. The fluent equivalent is `SheetQuery.Project("Region", "NetSales")`.

An aggregate query can explicitly select its grouping expressions alongside
aggregates, for example `SELECT Region, SUM(NetSales) GROUP BY Region`. Ungrouped
source columns are rejected. If the selection contains only aggregate
expressions, grouping keys are prepended for compatibility with existing queries.
The fluent `Project` API remains a row-projection operation.

```sql
FROM "Sheet1"!A6:F2410 HEADER ROW 6
SELECT Region, NetSales
WHERE Units > 100
LIMIT 100
```

### Grouped ordering

Without `ORDER BY`, `LIMIT` returns the first groups in first-seen order.
`ORDER BY` orders groups before `LIMIT` selects the result rows. This returns the
top groups instead.

The key must be one of the `GROUP BY` expressions or a selected aggregate.
Aggregate matching uses the function and source expression. For example,
`ORDER BY AVG(NetSales)` matches `SELECT AVG(NetSales)`.

Grouped queries scan the full data range, even with a positive `LIMIT`. `LIMIT` caps result
rows, not stored group states. The default group limit bounds retained group
state at 10,000 groups; a query that exceeds it throws
`TooManyGroupsException`. `SheetQuerySpec.Parse(text).WithGroupLimit(n)` or the
fluent `WithGroupLimit` method can change that bound. Memory also depends on key
width, aggregate count, and retained strings. Unlimited row queries materialize
every matching result; use `LIMIT` when only a sample is needed.

A global aggregate has no groups to order. `ORDER BY` without `GROUP BY` throws
`QueryDslException` for this type of query.

```sql
FROM "Sheet1"!A6:F2410 HEADER ROW 6
SELECT SUM(NetSales), COUNT()
GROUP BY Region
ORDER BY SUM(NetSales) DESC
LIMIT 10
```

### Row ordering

Row ordering requires `LIMIT`. The limit bounds the memory required to find the
top rows.

The ordering column does not have to be in the result. For example,
`SELECT Region ORDER BY NetSales DESC LIMIT 10` ranks by `NetSales` and returns
`Region`.

The query keeps at most `min(LIMIT, matching rows)` rows. It must scan the full
data range to find the correct result. Therefore, ordered row queries cannot
stop early. `RowsScanned` records all non-empty data rows that the query scans.

```sql
FROM "Sheet1"!A6:F2410 HEADER ROW 6
SELECT *
ORDER BY NetSales DESC
LIMIT 10
```

### Value order

Both ordering modes use these rules:

- Empty values sort last for both `ASC` and `DESC`.
- Types sort in this order: numbers, dates, Boolean values, text, and errors.
- `DESC` reverses values inside each type. It does not reverse the type order.
- Equal groups keep their first-seen order. Equal rows keep their worksheet
  order.

These rules keep text such as `n/a` after all numbers in both directions. They
also keep date values separate from numbers.

### Parse before execution

A host can parse and validate a query before it opens the worksheet:

```csharp
SheetQuerySpec spec = SheetQuerySpec.Parse(queryText);
QueryResult result = workbook.ExecuteQuery(spec);
```

## Use with AI agents

The Query DSL is a small, read-only language. It is suitable as the input to an
agent tool, but the host must control file access.

Check each requested workbook path against an allowlist. Parse the query and cap
its result size before you open the workbook.

The agent also needs sheet and column information before it writes a query.
Provide this information in the prompt, or expose separate discovery tools that
call `Analyze` and `AnalyzeSheet`.

This example shows one query tool. `FormatQueryResult` is an application
function that returns only the data the agent needs:

```csharp
using System.ComponentModel;
using XLSight;
using XLSight.Query;

[Description("Run a read-only XLSight query against the selected workbook.")]
static string QuerySheet(
    [Description("The path of an allowed workbook.")] string workbookPath,
    [Description("An XLSight Query DSL statement.")] string query)
{
    const int maxResultRows = 100;

    string[] allowedPaths = ["/data/sales.xlsx", "/data/inventory.xlsx"];
    string fullPath = Path.GetFullPath(workbookPath);
    if (Array.IndexOf(allowedPaths, fullPath) < 0)
    {
        throw new ArgumentException("The workbook path is not allowed.", nameof(workbookPath));
    }

    SheetQuerySpec spec = SheetQuerySpec.Parse(query);
    bool returnsMultipleRows = spec.Aggregates.Count == 0 || spec.GroupBy is not null;
    if (returnsMultipleRows
        && (spec.Limit is not int limit || limit > maxResultRows))
    {
        throw new ArgumentException(
            $"Row and grouped queries require LIMIT {maxResultRows} or less.",
            nameof(query));
    }

    using var workbook = ExcelWorkbook.Open(fullPath);
    QueryResult result = workbook.ExecuteQuery(spec);
    return FormatQueryResult(result);
}
```

For filter discovery, a separate tool can call `DistinctValues`. If the host
already has column profiles, the fluent API can pass them to `WithStats`.
