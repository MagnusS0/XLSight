# XLSight.Layout

Optional, best-effort worksheet layout inference for [XLSight](https://github.com/MagnusS0/XLSight).
It runs heuristics over a worksheet's cells — through XLSight's format-neutral
scan path — and turns an unknown sheet into a structural map: where the tables
are, which rows and columns label them, and what kind of data each block holds.

`AnalyzeLayout` returns a `SheetLayoutInfo` with three collections:

- **Axes** — the label rows and columns that give data meaning: a year header
  row, a row-label column, a peeled-off context column. Each axis carries its
  orientation, role (primary or context), value kind (text, numeric, or date),
  range, a few sample values, a probed title, and any detected sections — e.g.
  "Funding" / "Loans" bands within one label column.
- **Measure fields** — the rectangular data blocks those axes describe, from
  ordinary header-run tables to numeric sensitivity matrices and header-less
  vectors. Each field lists the axis ids it answers to and a value profile
  (cell, numeric, text, and formula counts plus numeric min/max).
- **Groups** — fields clustered by shared axes into logical tables, each with
  a bounding range and, when one is found nearby, a title from the sheet.

Because this is heuristic inference over arbitrary spreadsheets, treat the
result as a best-effort map rather than ground truth. It is aimed at
programmatic consumers that need to orient themselves in a workbook they have
never seen — for example discovering which ranges are worth reading, or picking
the range and headers to hand to
[XLSight.Query](https://github.com/MagnusS0/XLSight/tree/master/src/XLSight.Query)
for aggregation.

```csharp
using XLSight;
using XLSight.Layout;

using var workbook = ExcelWorkbook.Open("model.xlsx");
SheetLayoutInfo layout = workbook.AnalyzeLayout("Financials");

foreach (LayoutGroupInfo group in layout.Groups)
{
    Console.WriteLine($"{group.Title ?? group.Id}: {group.Range}");
}
```

The asynchronous equivalent accepts cancellation:

```csharp
SheetLayoutInfo layout = await workbook.AnalyzeLayoutAsync("Financials", cancellationToken);
```

Layout analysis is an explicit worksheet scan. Core `Analyze` and `AnalyzeSheet`
do not collect layout facts or run layout heuristics.
