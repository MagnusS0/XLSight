using BenchmarkDotNet.Attributes;
using XLSight.Analysis;
using XLSight.Analysis.Layout;

namespace XLSight.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class LayoutBenchmarks
{
    private string _complexPath = null!;

    [GlobalSetup]
    public void Setup()
    {
        _complexPath = Path.Combine(AppContext.BaseDirectory, "TestData", "complex_workbook.xlsx");
    }

    /// <summary>
    /// Full core analysis plus explicit layout analysis for every worksheet. This is the
    /// post-extraction semantic equivalent of the former AnalyzeWorkbook_Complex benchmark,
    /// which collected layout facts during the core analysis scan.
    /// </summary>
    [Benchmark]
    public int AnalyzeWorkbookAndLayoutComplex()
    {
        using var workbook = ExcelWorkbook.Open(_complexPath);
        WorkbookInfo analysis = workbook.Analyze();
        int resultCount = analysis.Sheets.Count;
        foreach (string sheet in workbook.SheetNames)
        {
            SheetLayoutInfo layout = workbook.AnalyzeLayout(sheet);
            resultCount += layout.Axes.Count + layout.MeasureFields.Count + layout.Groups.Count;
        }

        return resultCount;
    }

    [Benchmark]
    public WorkbookInfo AnalyzeWorkbookCoreComplex()
    {
        using var workbook = ExcelWorkbook.Open(_complexPath);
        return workbook.Analyze();
    }

    [Benchmark]
    public SheetLayoutInfo AnalyzeLayoutComplexCalculator()
    {
        using var workbook = ExcelWorkbook.Open(_complexPath);
        return workbook.AnalyzeLayout("Calculator");
    }
}
