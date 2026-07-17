# XLSight.Layout

Optional worksheet layout inference for [XLSight](https://github.com/MagnusS0/XLSight).
It identifies axes, measure fields, and related layout groups through XLSight's
format-neutral worksheet scan path.

```csharp
using XLSight;
using XLSight.Layout;

using var workbook = ExcelWorkbook.Open("model.xlsx");
SheetLayoutInfo layout = workbook.AnalyzeLayout("Financials");
```

The asynchronous equivalent accepts cancellation:

```csharp
SheetLayoutInfo layout = await workbook.AnalyzeLayoutAsync("Financials", cancellationToken);
```

Layout analysis is an explicit worksheet scan. Core `Analyze` and `AnalyzeSheet`
do not collect layout facts or run layout heuristics.
