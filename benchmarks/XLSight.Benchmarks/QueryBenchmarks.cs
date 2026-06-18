using BenchmarkDotNet.Attributes;
using XLSight;
using XLSight.Query;

/// <summary>
/// Verifies the M2 "done when": a fused filter/group-by/aggregate pass runs at
/// ≈ raw scan speed with O(groups) allocation. Each query benchmark is paired
/// with a hand-written reader loop computing the same answer.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class QueryBenchmarks
{
    private string _mediumPath = null!;
    private string _largePath = null!;
    private const string MediumGroupBySumQuery = """
        FROM Data!A1:J1001 HEADER AUTO
        SELECT SUM(Value1)
        GROUP BY Category
        """;

    [GlobalSetup]
    public void Setup()
    {
        _mediumPath = Path.Combine(AppContext.BaseDirectory, "TestData", "medium.xlsx");
        _largePath = Path.Combine(AppContext.BaseDirectory, "TestData", "large.xlsx");
    }

    // ── medium.xlsx: 1000 rows × 10 cols, group by Category (5 groups) ────────

    [Benchmark]
    public QueryResult Query_GroupBySum_Medium()
    {
        using var wb = ExcelWorkbook.Open(_mediumPath);
        return wb.QueryRange("Data", "A1:J1001")
            .GroupBy("Category")
            .Aggregate(Agg.Sum("Value1"))
            .Execute();
    }

    [Benchmark]
    public QueryResult QueryDsl_GroupBySum_Medium()
    {
        using var wb = ExcelWorkbook.Open(_mediumPath);
        return wb.ExecuteQuery(MediumGroupBySumQuery);
    }

    [Benchmark]
    public double RawScan_GroupBySum_Medium()
    {
        using var wb = ExcelWorkbook.Open(_mediumPath);
        using var reader = wb.GetRangeReader("Data", "A1:J1001");
        var groups = new Dictionary<string, double>(StringComparer.Ordinal);
        bool headerSeen = false;
        while (reader.Read())
        {
            if (!headerSeen) { headerSeen = true; continue; }
            var row = reader.Current;
            if (!row.GetCell(2).TryGetText(out string? category)) { continue; }
            if (!row.GetCell(4).TryGetNumber(out double value)) { continue; }
            groups[category] = groups.GetValueOrDefault(category) + value;
        }

        double total = 0;
        foreach (double v in groups.Values) { total += v; }
        return total;
    }

    // ── large.xlsx: 100K rows × 5 numeric cols, filtered global sum ───────────

    [Benchmark]
    public QueryResult Query_FilteredSum_Large()
    {
        using var wb = ExcelWorkbook.Open(_largePath);
        return wb.QueryRange("Numbers", "A1:E100001")
            .Where("A", QueryOp.GreaterThan, 0.5)
            .Aggregate(Agg.Sum("C"), Agg.Count())
            .Execute();
    }

    [Benchmark]
    public double RawScan_FilteredSum_Large()
    {
        using var wb = ExcelWorkbook.Open(_largePath);
        using var reader = wb.GetRangeReader("Numbers", "A1:E100001");
        bool headerSeen = false;
        double sum = 0;
        long count = 0;
        while (reader.Read())
        {
            if (!headerSeen) { headerSeen = true; continue; }
            var row = reader.Current;
            if (!row.GetCell(1).TryGetNumber(out double a) || a <= 0.5) { continue; }
            count++;
            if (row.GetCell(3).TryGetNumber(out double c)) { sum += c; }
        }

        return sum + count;
    }
}
