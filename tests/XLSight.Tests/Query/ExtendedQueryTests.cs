using Xunit;

namespace XLSight.Query.Tests;

/// <summary>
/// Contract tests for the expression based query surface. These tests deliberately use the
/// deterministic SalesWorkbook fixture so every result can be checked against the records that
/// produced the workbook, without coupling correctness to a particular scanner implementation.
/// </summary>
public sealed class ExtendedQueryTests
{
    private const string Range = "A1:F11";

    [Fact]
    public void WideAggregateSelection_KeepsEveryGroupAndExpressionIndependent()
    {
        const int groups = 65;
        SalesRecord[] records = Enumerable.Range(0, groups * 2).Select(i => new SalesRecord(
            $"G{i % groups}", "Jan", 0, null, i + 1, true, new DateTime(2024, 1, 1))).ToArray();
        using var stream = SalesWorkbook.Build(records: records);
        using var workbook = ExcelWorkbook.Open(stream);
        string aggregates = string.Join(", ", Enumerable.Range(0, 33).Select(i => $"SUM(Units + {i}) AS total{i}"));
        var spec = SheetQuerySpec.Parse($"SELECT {aggregates} FROM Sales!A1:F{records.Length + 1} HEADER ROW 1 GROUP BY Region")
            .WithGroupLimit(groups);
        QueryResult result = workbook.ExecuteQuery(spec);
        Assert.Equal(groups, result.Rows.Count);
        for (int group = 0; group < groups; group++)
        {
            ReadOnlySpan<ExcelCellValue> values = result.Rows[group].Values.Span;
            Assert.Equal($"G{group}", values[0].AsText());
            for (int aggregate = 0; aggregate < 33; aggregate++)
            {
                Assert.Equal(2d * (group + 1 + aggregate) + groups, values[aggregate + 1].AsNumber());
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(129)]
    public void ManyInterleavedGroups_KeepIndependentAggregateState(int groups)
    {
        SalesRecord[] records = Enumerable.Range(0, groups * 3).Select(i => new SalesRecord(
            $"G{i % groups}", "Jan", i / groups == 1 ? null : i % groups + (i / groups * 10),
            i / groups == 1 ? "n/a" : null, i + 1, i / groups == 0, new DateTime(2024, 1, 1).AddDays(i / groups))).ToArray();
        using var stream = SalesWorkbook.Build(records: records);
        using var workbook = ExcelWorkbook.Open(stream);
        var spec = SheetQuerySpec.Parse($"""
            SELECT Region, COUNT(), SUM(Units), AVG(NetSales), MIN(OrderDate), MAX(OrderDate),
                SUM(Units) FILTER (WHERE OnPromo = TRUE)
            FROM Sales!A1:F{records.Length + 1} HEADER ROW 1 GROUP BY Region
            """).WithGroupLimit(groups);
        QueryResult result = workbook.ExecuteQuery(spec);
        Assert.Equal(groups, result.Rows.Count);
        for (int i = 0; i < groups; i++)
        {
            ReadOnlySpan<ExcelCellValue> values = result.Rows[i].Values.Span;
            Assert.Equal($"G{i}", values[0].AsText());
            Assert.Equal(3d, values[1].AsNumber());
            Assert.Equal(3d * (i + 1 + groups), values[2].AsNumber());
            Assert.Equal(i + 10d, values[3].AsNumber());
            Assert.Equal(new DateTime(2024, 1, 1), values[4].AsDate());
            Assert.Equal(new DateTime(2024, 1, 3), values[5].AsDate());
            Assert.Equal(i + 1d, values[6].AsNumber());
        }
        Assert.Equal(groups, Assert.Single(result.Unaggregatable).SkippedCount);
        QueryResult keys = workbook.ExecuteQuery($"SELECT Region FROM Sales!A1:F{records.Length + 1} HEADER ROW 1 GROUP BY Region");
        Assert.Equal(result.Rows.Select(r => r.Values.Span[0]), keys.Rows.Select(r => r.Values.Span[0]));
    }

    [Theory]
    [InlineData("ORDER BY total DESC", "AMER")]
    [InlineData("ORDER BY total ASC", "EMEA")]
    [InlineData("ORDER BY total - total DESC", "EMEA")]
    [InlineData("", "EMEA")]
    public void GroupLimit_AppliesAfterHavingWithStableTies(string ordering, string expected)
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery($"""
            SELECT Region, SUM(Units) AS total, COUNT() AS n
            FROM Sales!A1:F11 HEADER ROW 1 GROUP BY Region
            HAVING total > 15 {ordering} LIMIT 1
            """);
        QueryResultRow row = Assert.Single(result.Rows);
        Assert.Equal(expected, row.Values.Span[0].AsText());
        Assert.Null(row.SourceRowIndex);
        Assert.Equal(10, result.RowsScanned);
    }

    [Theory]
    [InlineData("Units-1", 0d)]
    [InlineData("Units+-1", 0d)]
    [InlineData("Units*-2", -2d)]
    [InlineData("1e-2+Units", 1.01d)]
    public void ArithmeticSigns_DoNotDependOnWhitespace(string expression, double expected)
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery($"SELECT {expression} FROM Sales!{Range} HEADER ROW 1 LIMIT 1");
        Assert.Equal(expected, Assert.Single(result.Rows).Values.Span[0].AsNumber());
    }

    [Fact]
    public void DuplicateAliases_ProduceAQueryDiagnosticDuringParsing()
    {
        var error = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse(
            $"SELECT SUM(Units) AS total, COUNT() AS TOTAL FROM Sales!{Range} HEADER ROW 1 GROUP BY Region ORDER BY total"));
        Assert.Contains("Duplicate alias", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstantFilter_CountsRowsWithoutRequiringAnyDataColumns()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery("""
            SELECT COUNT() FILTER (WHERE TRUE) AS count
            FROM Sales!A1:F11 HEADER ROW 1
            """);
        Assert.Equal(10, result.RowsScanned);
        Assert.Equal(10d, Assert.Single(result.Rows).Values.Span[0].AsNumber());
    }

    [Fact]
    public void GroupedOrderBy_CanUseAnAliasTwiceInOneExpression()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery("""
            SELECT Region, SUM(Units) AS total
            FROM Sales!A1:F11 HEADER ROW 1 GROUP BY Region
            ORDER BY total + total DESC
            """);
        Assert.Equal(["AMER", "EMEA", "APAC"], result.Rows.Select(r => r.Values.Span[0].AsText()));
    }

    [Fact]
    public void GroupBinding_UsesHeaderCaseFallbackInSelectionAndOrdering()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery("""
            SELECT region, SUM(units) AS total
            FROM Sales!A1:F11 HEADER ROW 1
            GROUP BY Region ORDER BY region
            """);
        Assert.Equal(["AMER", "APAC", "EMEA"], result.Rows.Select(r => r.Values.Span[0].AsText()));
        Assert.Equal([22d, 15d, 18d], result.Rows.Select(r => r.Values.Span[1].AsNumber()));
    }

    [Theory]
    [InlineData("Units NOT IN ('1')", 0)]
    [InlineData("Units IN (-1, 1, NULL)", 1)]
    [InlineData("Units NOT IN (-1, NULL)", 0)]
    [InlineData("Units = 'wrong' OR TRUE", 10)]
    [InlineData("Units = 'wrong' AND FALSE", 0)]
    public void PredicateUnknownsAndSignedMembership_HaveDefinedResults(string predicate, int count)
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery($"FROM Sales!{Range} HEADER ROW 1 SELECT * WHERE {predicate}");
        Assert.Equal(count, result.Rows.Count);
    }

    [Theory]
    [InlineData("NOT ")]
    [InlineData("- ")]
    public void ExcessiveUnaryDepth_IsRejectedBeforeExecution(string unary)
    {
        string expression = string.Concat(Enumerable.Repeat(unary, 300)) + "Units";
        Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse($"SELECT {expression} FROM Sales!{Range} HEADER ROW 1"));
    }

    [Fact]
    public void RepeatedExpressions_AreRefreshedForEachRowAndGroup()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        QueryResult result = workbook.ExecuteQuery("""
            SELECT Region, SUM(Units * 2) AS doubled, AVG(Units * 2) AS average
            FROM Sales!A1:F11 HEADER ROW 1 GROUP BY Region
            """);
        var expected = SalesWorkbook.Data.GroupBy(r => r.Region, StringComparer.Ordinal).ToArray();
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Sum(r => r.Units * 2d), result.Rows[i].Values.Span[1].AsNumber());
            Assert.Equal(expected[i].Average(r => r.Units * 2d), result.Rows[i].Values.Span[2].AsNumber());
        }
    }

    [Fact]
    public void SelectFirst_ProjectsColumnsInSelectOrder()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult result = workbook.ExecuteQuery("""
            SELECT NetSales, Region
            FROM Sales!A1:F11 HEADER ROW 1
            LIMIT 2
            """);

        Assert.Equal(["NetSales", "Region"], result.Columns);
        Assert.Equal(["100.5|EMEA", "200.25|EMEA"],
            result.Rows.Select(row => string.Join("|", row.Values.ToArray())));
    }

    [Fact]
    public void Arithmetic_UsesNormalPrecedenceAndSupportsAliases()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT Units + 2 * 3 AS Value, Units / 2 AS Half, Units % 2 AS Remainder
            LIMIT 2
            """);

        Assert.Equal(["Value", "Half", "Remainder"], result.Columns);
        Assert.Equal([7d, 0.5d, 1d], result.Rows[0].Values.ToArray().Select(value => value.AsNumber()));
        Assert.Equal([8d, 1d, 0d], result.Rows[1].Values.ToArray().Select(value => value.AsNumber()));
    }

    [Fact]
    public void LogicalOperators_RespectNotAndAndOrPrecedence()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT Region, Month
            WHERE Region IN ('EMEA', 'APAC') AND NOT (OnPromo = TRUE) OR Region = 'AMER'
            """);

        // The AND branch selects EMEA/APAC non-promo rows; OR adds every AMER row.
        SalesRecord[] expected = [.. SalesWorkbook.Data.Where(record =>
            ((record.Region is "EMEA" or "APAC") && !record.OnPromo) || record.Region == "AMER")];
        Assert.Equal(expected.Select(record => record.Month), result.Rows.Select(row => row.Values.Span[1].AsText()));
        Assert.Equal(expected.Select(record => record.Region), result.Rows.Select(row => row.Values.Span[0].AsText()));
    }

    [Fact]
    public void InAndColumnComparison_UseTheRightHandColumnValue()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT Region, Units
            WHERE Region IN ('EMEA', 'APAC') AND NetSales > [Units]
            """);

        // Dirty and missing NetSales cells become unknown and are excluded; numeric comparisons
        // are true only for values whose two operands have compatible types.
        Assert.Equal(["EMEA", "EMEA", "APAC", "EMEA", "APAC"],
            result.Rows.Select(row => row.Values.Span[0].AsText()));
        Assert.Equal([1d, 2d, 3d, 6d, 8d],
            result.Rows.Select(row => row.Values.Span[1].AsNumber()));
    }

    [Fact]
    public void IsEmptyAndIsNull_DistinguishMissingCellsFromDirtyText()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult empty = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT Region, Month
            WHERE NetSales IS EMPTY
            """);
        QueryResult nulls = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT Region, Month
            WHERE NetSales IS NULL
            """);

        Assert.Equal(["EMEA|Mar"], empty.Rows.Select(row => string.Join("|", row.Values.ToArray())));
        Assert.Equal(["EMEA|Mar"], nulls.Rows.Select(row => string.Join("|", row.Values.ToArray())));
    }

    [Fact]
    public void DateFunctions_ReturnTypedCalendarPartsAndTruncatedDate()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT YEAR(OrderDate) AS Year, MONTH(OrderDate) AS MonthNumber,
                   DAY(OrderDate) AS DayOfMonth, DATE_TRUNC('month', OrderDate) AS MonthStart
            LIMIT 1
            """);

        Assert.Equal(["Year", "MonthNumber", "DayOfMonth", "MonthStart"], result.Columns);
        Assert.Equal(2024d, result.Rows[0].Values.Span[0].AsNumber());
        Assert.Equal(1d, result.Rows[0].Values.Span[1].AsNumber());
        Assert.Equal(15d, result.Rows[0].Values.Span[2].AsNumber());
        Assert.Equal(new DateTime(2024, 1, 1), result.Rows[0].Values.Span[3].AsDate());
    }

    [Fact]
    public void FilteredAggregates_UseIndependentWhereClausesAndStrictNumericSkipping()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT SUM(NetSales) FILTER (WHERE Region = 'EMEA') AS EmeaSales,
                   COUNT() FILTER (WHERE OnPromo = TRUE) AS PromoRows
            """);

        var row = Assert.Single(result.Rows);
        Assert.Equal(310.75d, row.Values.Span[0].AsNumber());
        Assert.Equal(5d, row.Values.Span[1].AsNumber());
        Assert.Equal(["EmeaSales", "PromoRows"], result.Columns);
    }

    [Fact]
    public void MultiColumnGroupBy_EmitsKeysBeforeSelectedAggregates()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT COUNT() AS RowsInGroup, SUM(Units) AS TotalUnits
            GROUP BY Region, Month
            """);

        Assert.Equal(["Region", "Month", "RowsInGroup", "TotalUnits"], result.Columns);
        Assert.Equal(SalesWorkbook.Data.Select(record => $"{record.Region}|{record.Month}").Distinct(StringComparer.Ordinal),
            result.Rows.Select(row => $"{row.Values.Span[0].AsText()}|{row.Values.Span[1].AsText()}"));
    }

    [Fact]
    public void Having_CanReferenceAggregateAlias()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT Region, SUM(Units) AS TotalUnits
            GROUP BY Region
            HAVING TotalUnits > 20
            """);

        var row = Assert.Single(result.Rows);
        Assert.Equal("AMER", row.Values.Span[0].AsText());
        Assert.Equal(22d, row.Values.Span[1].AsNumber());
    }

    [Fact]
    public void AggregateOnlyGroupBy_PrependsGroupKeysForCompatibility()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT SUM(Units)
            GROUP BY Region
            """);

        Assert.Equal(["Region", "Sum(Units)"], result.Columns);
        Assert.Equal(
            new[] { ("EMEA", 18d), ("APAC", 15d), ("AMER", 22d) },
            result.Rows.Select(row => (row.Values.Span[0].AsText(), row.Values.Span[1].AsNumber())));
    }

    [Fact]
    public void LimitZero_SkipsRowsAndGlobalAggregateMaterialization()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT COUNT()
            LIMIT 0
            """);

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.RowsScanned);
        Assert.Equal(0, result.RowsMatched);
    }

    [Fact]
    public void LimitZero_BindsComputedHeadersWithoutScanningData()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult result = workbook.ExecuteQuery("""
            SELECT Region, Units * 2 AS Doubled
            FROM Sales!A1:F11 HEADER ROW 1
            LIMIT 0
            """);

        Assert.Equal(["Region", "Doubled"], result.Columns);
        Assert.Empty(result.Rows);
        Assert.Equal(0, result.RowsScanned);
    }

    [Fact]
    public void RowOrderBy_CanUseHiddenExpressionAndKeepsTiesStable()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT Region
            ORDER BY Units % 2 ASC
            LIMIT 4
            """);

        // Even Units all have the same hidden ordering expression. Their source order must be
        // preserved when the bounded top-N cutoff is selected.
        Assert.Equal(["EMEA", "APAC", "EMEA", "APAC"],
            result.Rows.Select(row => row.Values.Span[0].AsText()));
    }

    [Fact]
    public void CompositeGroupBy_RespectsConfiguredGroupStateLimit()
    {
        SheetQuerySpec spec = SheetQuerySpec.Parse("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT COUNT()
            GROUP BY Region, Month
            """).WithGroupLimit(2);

        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        Assert.Throws<TooManyGroupsException>(() => workbook.ExecuteQuery(spec));
    }

    [Theory]
    [InlineData("SELECT Region / 2 AS Broken")]
    [InlineData("SELECT Units / 0 AS Broken")]
    [InlineData("SELECT 0 / 0 AS Broken")]
    public void InvalidArithmetic_ProducesEmptyCells(string select)
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult result = workbook.ExecuteQuery($"""
            FROM Sales!A1:F11 HEADER ROW 1
            {select}
            LIMIT 1
            """);

        Assert.True(result.Rows[0].Values.Span[0].IsEmpty);
    }

    [Fact]
    public void MismatchedComparisonAndNotUnknown_DoNotAdmitRows()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);

        QueryResult mismatch = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT *
            WHERE Units = '1'
            """);
        QueryResult notUnknown = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT *
            WHERE NOT (Units = '1')
            """);

        Assert.Empty(mismatch.Rows);
        Assert.Empty(notUnknown.Rows);
    }

    [Fact]
    public async Task AsyncExpressionProjection_MatchesSyncAndHonorsPreCancellation()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        const string query = """
            FROM Sales!A1:F11 HEADER ROW 1
            SELECT Region, Units * 2 AS Doubled
            WHERE Region = 'EMEA'
            """;

        QueryResult sync = workbook.ExecuteQuery(query);
        QueryResult async = await workbook.ExecuteQueryAsync(query, TestContext.Current.CancellationToken);
        Assert.Equal(sync.Columns, async.Columns);
        Assert.Equal(sync.Rows.Select(row => string.Join("|", row.Values.ToArray())),
            async.Rows.Select(row => string.Join("|", row.Values.ToArray())));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workbook.ExecuteQueryAsync(query, cancellation.Token));
    }
}
