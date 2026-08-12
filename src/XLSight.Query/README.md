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

Use the fluent API for compiled .NET code. Use the DSL when a query must cross a
configuration, process, prompt, or tool boundary.

The DSL uses this clause order:

```text
FROM ... HEADER ... SELECT ... [WHERE ...] [GROUP BY ...] [ORDER BY ...] [LIMIT ...]
```

Supported clauses:

- `FROM <sheet>!<bounded-range>` selects one sheet and range. Sheet names can be
  bare or double-quoted.
- `HEADER AUTO` or `HEADER ROW <number>` selects the source of column names.
- `SELECT *` returns all source columns.
- `SELECT <column>[, <column>...]` returns selected source columns.
- `SELECT COUNT()`, `SUM(column)`, `AVG(column)`, `MIN(column)`, and `MAX(column)`
  return aggregates.
- `WHERE` joins predicates with `AND`. It supports `=`, `!=`, `<`, `<=`, `>`, and
  `>=`.
- Literals can contain text, numbers, `DATE "yyyy-MM-dd"` values, and Boolean
  values.
- Boolean predicates support only `=` and `!=`.
- `GROUP BY` accepts one column.
- `ORDER BY <key> [ASC|DESC]` orders grouped or row results. `ASC` is the default.
- `LIMIT` accepts a positive integer.

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

One `SELECT` clause cannot mix source columns and aggregates. `GROUP BY` cannot
be used with a source-column projection. The parser throws `QueryDslException`
for both cases.

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

The key must be the `GROUP BY` column or a selected aggregate. Aggregate matching
uses the function and source column. For example, `ORDER BY AVG(NetSales)`
matches `SELECT AVG(NetSales)`.

Grouped queries scan the full data range, even with `LIMIT`. `LIMIT` caps result
rows, not stored group states. The group limit bounds those states, and a query
that exceeds it throws `TooManyGroupsException`.

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
