using Xunit;

namespace XLSight.Query.Tests;

public sealed class ExtendedQueryRegressionTests
{
    [Theory]
    [InlineData("FROM Sales!A20:F25 HEADER AUTO SELECT COUNT() LIMIT 0")]
    [InlineData("SELECT COUNT() FROM Sales!A20:F25 HEADER AUTO LIMIT 0")]
    public void LimitZero_WithoutAnyHeaderOrDataNeverMaterializesAnAggregate(string query)
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery(query);
        Assert.Empty(result.Rows);
        Assert.Equal(0, result.RowsScanned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrderedRows_PreserveEarliestTiesWhenABetterRowEvictsASurvivor(bool selectFirst)
    {
        string[] regions = ["A", "B", "C", "D"];
        SalesRecord[] records = [.. regions.Select((region, i) =>
            new SalesRecord(region, "Jan", 0, null, i == 3 ? 4 : 5, false, new DateTime(2024, 1, 1)))];
        using var stream = SalesWorkbook.Build(records: records);
        using var workbook = ExcelWorkbook.Open(stream);
        string query = selectFirst
            ? "SELECT Region FROM Sales!A1:F5 HEADER ROW 1 ORDER BY Units LIMIT 3"
            : "FROM Sales!A1:F5 HEADER ROW 1 SELECT Region ORDER BY Units LIMIT 3";
        QueryResult result = workbook.ExecuteQuery(query);
        Assert.Equal(["D", "A", "B"], result.Rows.Select(r => r.Values.Span[0].AsText()));
        Assert.Equal([5, 2, 3], result.Rows.Select(r => r.SourceRowIndex!.Value));
    }

    [Theory]
    [InlineData("2025_Sales")]
    [InlineData("2025Expenses")]
    [InlineData("2025E2")]
    public void ExpressionParser_AcceptsDigitPrefixedSheetNames(string name)
    {
        SheetQuerySpec spec = SheetQuerySpec.Parse($"SELECT Units * 2 FROM {name}!A1:F11 HEADER ROW 1");
        Assert.Equal(name, spec.Sheet);
    }

    [Fact]
    public void ExpressionParser_PreservesLegacyPositiveIntegerSigns()
    {
        SheetQuerySpec spec = SheetQuerySpec.Parse("SELECT Units * 2 FROM Sales!A1:F11 HEADER ROW +1 LIMIT +0");
        Assert.Equal(1, spec.Header.Row);
        Assert.Equal(0, spec.Limit);
    }

    [Theory]
    [InlineData("HEADER AUTO")]
    [InlineData("HEADER ROW 2")]
    public async Task LimitZero_WithExternalHeaderReturnsComputedColumns(string header)
    {
        using var stream = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(stream);
        string query = $"SELECT Units * 2 AS doubled FROM Sales!A6:F12 {header} LIMIT 0";
        QueryResult result = await workbook.ExecuteQueryAsync(query, TestContext.Current.CancellationToken);
        Assert.Equal(["doubled"], result.Columns);
        Assert.Empty(result.Rows);
        Assert.Equal(0, result.RowsScanned);
        Assert.Equal(0, result.RowsMatched);
    }

    [Fact]
    public void NonFiniteCells_DoNotMatchNegatedComparisonsOrThrowInRound()
    {
        SalesRecord[] records = [
            new("A", "Jan", 0, null, double.NaN, true, new DateTime(2024, 1, 1)),
            new("A", "Jan", 0, null, double.PositiveInfinity, true, new DateTime(2024, 1, 1)),
        ];
        using var stream = SalesWorkbook.Build(records: records);
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult rounded = workbook.ExecuteQuery("SELECT ROUND(1, Units) FROM Sales!A1:F3 HEADER ROW 1");
        Assert.Equal(2, rounded.Rows.Count);
        Assert.All(rounded.Rows, row => Assert.True(row.Values.Span[0].IsEmpty));
        QueryResult filtered = workbook.ExecuteQuery("SELECT * FROM Sales!A1:F3 HEADER ROW 1 WHERE NOT (Units < 0)");
        Assert.Empty(filtered.Rows);
        QueryResult legacy = workbook.ExecuteQuery("FROM Sales!A1:F3 HEADER ROW 1 SELECT * WHERE Units != 0");
        Assert.Empty(legacy.Rows);
        QueryResult aggregate = workbook.ExecuteQuery("SELECT SUM(Units) AS total FROM Sales!A1:F3 HEADER ROW 1");
        Assert.True(Assert.Single(aggregate.Rows).Values.Span[0].IsEmpty);
        Assert.Equal(2, Assert.Single(aggregate.Unaggregatable).SkippedCount);
    }

    [Theory]
    [InlineData("FROM Sales!A1:F11 HEADER ROW 1 SELECT SUM(NetSales), AVG(NetSales)")]
    [InlineData("FROM Sales!A1:F11 HEADER ROW 1 SELECT SUM(NetSales), AVG(netsales)")]
    [InlineData("SELECT SUM(NetSales) AS total, AVG(NetSales) AS average FROM Sales!A1:F11 HEADER ROW 1")]
    [InlineData("SELECT SUM(NetSales) AS total, AVG(netsales) AS average FROM Sales!A1:F11 HEADER ROW 1")]
    public void DirtyDiagnostics_CountEachRejectedCellOnceAcrossAggregates(string query)
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        UnaggregatableColumn dirty = Assert.Single(workbook.ExecuteQuery(query).Unaggregatable);
        Assert.Equal(1, dirty.SkippedCount);
        Assert.Equal("NetSales", dirty.Column);
        Assert.Equal([5], dirty.SampleRowIndices);
    }

    [Theory]
    [InlineData("SELECT SUM(Units) AS total, SUM(Units) FILTER (WHERE OnPromo) AS promo", "GROUP BY YEAR(OrderDate), MONTH(OrderDate) HAVING total > 0 ORDER BY total DESC LIMIT 2")]
    [InlineData("SELECT YEAR(OrderDate) AS year, MONTH(OrderDate) AS month, SUM(Units) AS total, SUM(Units) FILTER (WHERE OnPromo) AS promo", "GROUP BY YEAR(OrderDate), MONTH(OrderDate) HAVING total > 0 ORDER BY total DESC LIMIT 2")]
    public void DateGroups_KeepIndependentFilteredAggregatesAndStableOrdering(string select, string clauses)
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery($"FROM Sales!A1:F11 HEADER ROW 1 {select} {clauses}");
        var expected = SalesWorkbook.Data.GroupBy(r => (r.OrderDate.Year, r.OrderDate.Month))
            .OrderByDescending(g => g.Sum(r => r.Units)).Take(2).ToArray();
        Assert.Equal(2, result.Rows.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            SalesRecord[] promo = expected[i].Where(r => r.OnPromo).ToArray();
            Assert.Equal([
                ExcelCellValue.FromNumber(expected[i].Key.Year), ExcelCellValue.FromNumber(expected[i].Key.Month),
                ExcelCellValue.FromNumber(expected[i].Sum(r => r.Units)),
                promo.Length == 0 ? ExcelCellValue.Empty : ExcelCellValue.FromNumber(promo.Sum(r => r.Units)),
            ], result.Rows[i].Values.ToArray());
        }
    }

    [Fact]
    public void ParserResourceCaps_RejectOversizedInputTokensAndNesting()
    {
        Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse(new string(' ', 1_048_577)));
        string columns = string.Join(",", Enumerable.Repeat("Units", 4_100));
        Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse($"SELECT {columns} FROM Sales!A1:F11 HEADER ROW 1"));
        string expression = new string('(', 65) + "Units" + new string(')', 65);
        Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse($"SELECT {expression} FROM Sales!A1:F11 HEADER ROW 1"));
    }

    [Fact]
    public void OverflowedNumericLiteral_IsRejectedInBothQueryForms()
    {
        string literal = new string('9', 400);
        Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse($"FROM Sales!A1:F11 HEADER ROW 1 SELECT * WHERE Units != {literal}"));
        Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse($"SELECT * FROM Sales!A1:F11 HEADER ROW 1 WHERE Units != {literal}"));
    }

    [Fact]
    public void Ordering_CanUseAggregateInsideAComputedSelection()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery("""
            SELECT Region, ROUND(SUM(Units), 0) AS total
            FROM Sales!A1:F11 HEADER ROW 1 GROUP BY Region ORDER BY SUM(Units) DESC
            """);
        Assert.Equal(["AMER", "EMEA", "APAC"], result.Rows.Select(r => r.Values.Span[0].AsText()));
    }

    [Theory]
    [InlineData("TRUE AND NULL", "Empty")]
    [InlineData("FALSE AND NULL", "FALSE")]
    [InlineData("TRUE OR NULL", "TRUE")]
    [InlineData("FALSE OR NULL", "Empty")]
    [InlineData("NOT NULL", "Empty")]
    [InlineData("1 IN (0, 2, 3, 4, 5, 6, 7, 8, NULL)", "Empty")]
    [InlineData("1 IN (0, 1, 2, 3, 4, 5, 6, 7, 8, NULL)", "TRUE")]
    [InlineData("1 NOT IN (0, 2, 3, 4, 5, 6, 7, 8, 'x')", "Empty")]
    [InlineData("ROUND(1.25, 1)", "1.3")]
    [InlineData("ROUND(-1.25, 1)", "-1.3")]
    [InlineData("ROUND(1.25, 1.5)", "Empty")]
    [InlineData("ROUND(1.25, 16)", "Empty")]
    [InlineData("COALESCE(NULL, '', 1)", "")]
    [InlineData("'' IS EMPTY", "FALSE")]
    [InlineData("1e308 * 1e308", "Empty")]
    public void ConstantExpressions_RespectTypedAndUnknownSemantics(string expression, string expected)
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery($"SELECT {expression} FROM Sales!A1:F11 HEADER ROW 1 LIMIT 1");
        Assert.Equal(expected, Assert.Single(result.Rows).Values.Span[0].ToString());
        Assert.Equal(1, result.RowsScanned);
    }

    [Theory]
    [InlineData("complex_workbook.xlsx")]
    [InlineData("complex_workbook.xlsb")]
    public async Task RealWorkbook_GroupingAndFilteredCachedFormulasMatchDirectReader(string fileName)
    {
        using var workbook = ExcelWorkbook.Open(Path.Combine(AppContext.BaseDirectory, "TestData", fileName));
        const string query = """
            SELECT Age % 5 AS bucket,
                SUM([Ending Balance] * 1.15) FILTER (WHERE Age >= 40) AS total
            FROM Calculator!E6:K40 HEADER ROW 5 GROUP BY Age % 5
            HAVING total > 0 ORDER BY total DESC LIMIT 3
            """;
        var totals = new Dictionary<double, double>();
        using (var reader = workbook.GetRangeReader("Calculator", "E6:K40"))
        {
            while (reader.Read())
            {
                double age = reader.Current.GetCell(6).AsNumber();
                if (age < 40) { continue; }
                double bucket = age % 5;
                totals[bucket] = totals.GetValueOrDefault(bucket) + reader.Current.GetCell(11).AsNumber() * 1.15;
            }
        }
        var expected = totals.OrderByDescending(t => t.Value).Take(3).ToArray();
        SheetQuerySpec spec = SheetQuerySpec.Parse(query);
        QueryResult sync = workbook.ExecuteQuery(spec);
        QueryResult async = await workbook.ExecuteQueryAsync(spec, TestContext.Current.CancellationToken);
        Assert.Equal(3, sync.Rows.Count);
        Assert.Equal(35, sync.RowsScanned);
        Assert.Equal(["bucket", "total"], sync.Columns);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Key, sync.Rows[i].Values.Span[0].AsNumber());
            Assert.Equal(expected[i].Value, sync.Rows[i].Values.Span[1].AsNumber());
            Assert.Equal(sync.Rows[i].Values.ToArray(), async.Rows[i].Values.ToArray());
        }
    }

    [Theory]
    [InlineData("TRUE", "TRUE")]
    [InlineData("FALSE", "FALSE")]
    [InlineData("NULL", "Empty")]
    [InlineData("EMPTY", "Empty")]
    [InlineData("1.0", "1")]
    [InlineData("SUM(1.0)", "10")]
    [InlineData("COUNT() GROUP BY TRUE", "TRUE|10")]
    public void FromFirst_LiteralExpressionsMatchSelectFirst(string selection, string expected)
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery($"FROM Sales!A1:F11 HEADER ROW 1 SELECT {selection} LIMIT 1");
        Assert.Equal(expected, string.Join("|", Assert.Single(result.Rows).Values.ToArray()));
    }

    [Theory]
    [InlineData("SELECT *")]
    [InlineData("SELECT COUNT()")]
    [InlineData("SELECT Units * 2")]
    public async Task ExplicitMissingHeader_ThrowsForSyncAndAsync(string selection)
    {
        using var stream = SalesWorkbook.Build(records: []);
        using var workbook = ExcelWorkbook.Open(stream);
        string query = $"FROM Sales!A1:F2 HEADER ROW 2 {selection}";
        Assert.Throws<InvalidOperationException>(() => workbook.ExecuteQuery(query));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workbook.ExecuteQueryAsync(query, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AggregateAliasMatchingSourceColumn_DoesNotChangeGrouping()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery("""
            SELECT SUM(Units) AS Region FROM Sales!A1:F11 HEADER ROW 1
            GROUP BY Region ORDER BY Region DESC
            """);
        Assert.Equal(["Region", "Region"], result.Columns);
        Assert.Equal(["AMER", "EMEA", "APAC"], result.Rows.Select(r => r.Values.Span[0].AsText()));
        Assert.Equal([22d, 18d, 15d], result.Rows.Select(r => r.Values.Span[1].AsNumber()));
    }

    [Fact]
    public void AliasMatchingAnotherSelectedColumn_DoesNotRewriteSelections()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery("""
            SELECT Region AS Month, Month, SUM(Units)
            FROM Sales!A1:F11 HEADER ROW 1 GROUP BY Region, Month
            """);
        Assert.Equal(9, result.Rows.Count);
        Assert.Equal(["EMEA", "Jan", "7"], result.Rows[0].Values.ToArray().Select(v => v.ToString()));
    }

    [Theory]
    [InlineData("ABS(SUM(Units))", 55d)]
    [InlineData("ROUND(AVG(Units), 0)", 6d)]
    [InlineData("COALESCE(SUM(NetSales) FILTER (WHERE FALSE), 0)", 0d)]
    public void ScalarFunctions_CanConsumeCompletedAggregates(string expression, double expected)
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery($"SELECT {expression} FROM Sales!A1:F11 HEADER ROW 1");
        Assert.Equal(expected, Assert.Single(result.Rows).Values.Span[0].AsNumber());
    }

    [Theory]
    [InlineData("SELECT Units + SUM(Units)")]
    [InlineData("SELECT SUM(Units) HAVING Units > 0")]
    [InlineData("SELECT * HAVING TRUE")]
    [InlineData("SELECT SUM(SUM(Units))")]
    [InlineData("SELECT SUM(ABS(SUM(Units)))")]
    [InlineData("SELECT DATE_TRUNC('week', OrderDate)")]
    public void InvalidExpressions_AreRejectedDuringParsing(string clauses)
    {
        Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse($"FROM Sales!A1:F11 HEADER ROW 1 {clauses}"));
    }

    [Fact]
    public void BlankHeaders_UseExcelColumnLabelsInExpressionQueries()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        // G is outside the populated header, but inside the requested range.
        QueryResult result = workbook.ExecuteQuery("SELECT * FROM Sales!A1:G11 HEADER ROW 1 LIMIT 1");
        Assert.Equal("G", result.Columns[6]);
        Assert.True(result.Rows[0].Values.Span[6].IsEmpty);
        QueryResult selected = workbook.ExecuteQuery("SELECT [G] FROM Sales!A1:G11 HEADER ROW 1 LIMIT 1");
        Assert.True(Assert.Single(selected.Rows).Values.Span[0].IsEmpty);
    }

    [Theory]
    [InlineData("SUM(Units)")]
    [InlineData("AVG(Units)")]
    public void OverflowedAggregate_ReturnsEmptyInsteadOfInfinity(string aggregate)
    {
        SalesRecord[] records = [
            new("A", "Jan", 0, null, 1e308, true, new DateTime(2024, 1, 1)),
            new("A", "Jan", 0, null, 1e308, true, new DateTime(2024, 1, 1)),
        ];
        using var stream = SalesWorkbook.Build(records: records);
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery($"SELECT {aggregate} AS total FROM Sales!A1:F3 HEADER ROW 1");
        Assert.True(Assert.Single(result.Rows).Values.Span[0].IsEmpty);
    }

    [Fact]
    public void GlobalAggregateWithConstantSelection_ReturnsOneRowEvenWithoutMatches()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery("""
            SELECT 'total', COUNT(), COALESCE(SUM(Units), 0)
            FROM Sales!A1:F11 HEADER ROW 1 WHERE FALSE
            """);
        Assert.Equal(["total", "0", "0"], Assert.Single(result.Rows).Values.ToArray().Select(v => v.ToString()));
    }

    [Fact]
    public void HavingAliasWithSameNameAsSource_UsesTheSelectedExpressionOnce()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery("""
            SELECT Region, SUM(Units) + 1 AS Units
            FROM Sales!A1:F11 HEADER ROW 1 GROUP BY Region HAVING Units > 20
            """);
        Assert.Equal(["AMER", "23"], Assert.Single(result.Rows).Values.ToArray().Select(v => v.ToString()));
    }
}
