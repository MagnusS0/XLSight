# XLSight.Layout

XLSight.Layout is an optional package for best-effort worksheet structure analysis.
It uses the format-neutral scan path in [XLSight](https://github.com/MagnusS0/XLSight).
It can find tables, labels, and data blocks in an unknown worksheet.

## Result

`AnalyzeLayout` returns a `SheetLayoutInfo` with three collections:

- **Axes** are label rows and columns, such as a year header or a row-label
  column. Each axis has an orientation, role, value kind, range, samples, and
  an optional title. It can also contain sections such as `Funding` and `Loans`.
- **Measure fields** are rectangular data blocks. They include standard tables,
  numeric matrices, and vectors without headers. Each field lists its axes and
  value profile. The profile contains cell, numeric, text, and formula counts.
  It also contains the numeric minimum and maximum.
- **Groups** combine fields that share axes. Each group has a bounding range and
  an optional title from the worksheet.

## Limits

Layout analysis uses heuristics, so the result can differ from the intended
worksheet structure. Use the result to find useful ranges or select a range and
headers for
[XLSight.Query](https://github.com/MagnusS0/XLSight/tree/master/src/XLSight.Query).

## Example

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

The asynchronous API accepts a cancellation token:

```csharp
SheetLayoutInfo layout = await workbook.AnalyzeLayoutAsync("Financials", cancellationToken);
```

Each layout analysis scans the selected worksheet. Core `Analyze` and
`AnalyzeSheet` do not run layout heuristics.
