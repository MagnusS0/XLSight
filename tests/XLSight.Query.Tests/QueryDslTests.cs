using System.Globalization;
using XLSight.Query.Tests.Infrastructure;
using Xunit;

namespace XLSight.Query.Tests;

public sealed class QueryDslTests
{
    private const string Range = "A1:F11";

    [Fact]
    public void Parse_ValidAggregateQuery_ReturnsStructuredSpec()
    {
        SheetQuerySpec spec = SheetQuerySpec.Parse("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT SUM(Units), COUNT()
            WHERE Region = "EMEA" AND Units > 1
            GROUP BY Month
            LIMIT 5
            """);

        Assert.Equal(SalesWorkbook.SheetName, spec.Sheet);
        Assert.Equal(Range, spec.RangeAddress);
        Assert.Equal(SheetQueryHeaderKind.Auto, spec.Header.Kind);
        Assert.False(spec.SelectAll);
        Assert.Equal(["Sum(Units)", "Count()"], spec.Aggregates.Select(a => a.Label));
        Assert.Equal(2, spec.Predicates.Count);
        Assert.Equal("Month", spec.GroupBy);
        Assert.Equal(5, spec.Limit);
    }

    [Fact]
    public void Parse_QuotedIdentifiersAndEscapedText_PreservesValues()
    {
        SheetQuerySpec spec = SheetQuerySpec.Parse(
            "FROM \"Sales\"!A1:F11 HEADER AUTO\n" +
            "SELECT COUNT()\n" +
            "WHERE \"Region\" = \"Revenue \"\"Actual\"\"\"");

        SheetQueryPredicate predicate = Assert.Single(spec.Predicates);
        Assert.Equal("Region", predicate.Column);
        Assert.Equal("Revenue \"Actual\"", predicate.Literal.AsText());
    }

    [Fact]
    public void Parse_BareIdentifiersCanStartWithDigits()
    {
        SheetQuerySpec spec = SheetQuerySpec.Parse("""
            FROM 2025_Sales!A1:F11 HEADER AUTO
            SELECT SUM(2025)
            GROUP BY Q1
            """);

        Assert.Equal("2025_Sales", spec.Sheet);
        Assert.Equal("Sum(2025)", Assert.Single(spec.Aggregates).Label);
        Assert.Equal("Q1", spec.GroupBy);
    }

    [Fact]
    public void ExecuteQuery_SelectAll_ReturnsFilteredRows()
    {
        int[] expectedRows = [.. SalesWorkbook.Data
            .Select((d, i) => (d, Row: SalesWorkbook.SheetRowOf(i)))
            .Where(x => string.Equals(x.d.Region, "APAC", StringComparison.Ordinal) && x.d.Units > 3)
            .Select(x => x.Row)];

        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT *
            WHERE Region = "APAC" AND Units > 3
            LIMIT 10
            """);

        Assert.Equal(SalesWorkbook.Headers, result.Columns);
        Assert.Equal(expectedRows, result.Rows.Select(r => r.SourceRowIndex!.Value));
    }

    [Fact]
    public void ExecuteQuery_AggregatesWithGroupBy_MatchesFluentQuery()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult expected = workbook
            .QueryRange(SalesWorkbook.SheetName, Range)
            .Where("Region", QueryOperator.Equals, "EMEA")
            .Where("Units", QueryOperator.GreaterThan, 1)
            .GroupBy("Month")
            .Select(QueryAggregates.Sum("Units"), QueryAggregates.Count())
            .Take(2)
            .Execute();

        QueryResult actual = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT SUM(Units), COUNT()
            WHERE Region = "EMEA" AND Units > 1
            GROUP BY Month
            LIMIT 2
            """);

        Assert.Equal(expected.Columns, actual.Columns);
        Assert.Equal(
            expected.Rows.Select(r => string.Join("|", r.Values.ToArray())),
            actual.Rows.Select(r => string.Join("|", r.Values.ToArray())));
        Assert.Equal(expected.RowsScanned, actual.RowsScanned);
        Assert.Equal(expected.RowsMatched, actual.RowsMatched);
    }

    [Fact]
    public void ExecuteQuery_HeaderRow_UsesExplicitHeader()
    {
        using var ms = SalesWorkbook.Build(titleRow: true);
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F12 HEADER ROW 2
            SELECT COUNT()
            WHERE Region = "EMEA"
            """);

        Assert.Equal(
            SalesWorkbook.Data.Count(d => string.Equals(d.Region, "EMEA", StringComparison.Ordinal)),
            result.Rows[0].Values.Span[0].AsNumber());
    }

    [Fact]
    public void ExecuteQuery_DateAndBooleanPredicates_MatchReference()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        QueryResult result = workbook.ExecuteQuery("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT COUNT()
            WHERE OrderDate >= DATE "2024-03-01" AND OnPromo = TRUE
            """);

        Assert.Equal(
            SalesWorkbook.Data.Count(d => d.OrderDate >= new DateTime(2024, 3, 1) && d.OnPromo),
            result.Rows[0].Values.Span[0].AsNumber());
    }

    [Fact]
    public async Task ExecuteQueryAsync_ProducesSameResultAsSync()
    {
        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        const string query = """
            FROM Sales!A1:F11 HEADER AUTO
            SELECT AVG(Units), MIN(OrderDate), MAX(OrderDate)
            WHERE Region != "APAC"
            """;

        QueryResult sync = workbook.ExecuteQuery(query);
        QueryResult async = await workbook.ExecuteQueryAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(sync.Columns, async.Columns);
        Assert.Equal(
            sync.Rows.Select(r => string.Join("|", r.Values.ToArray())),
            async.Rows.Select(r => string.Join("|", r.Values.ToArray())));
    }

    [Fact]
    public void ExecuteQuery_HeaderColumn_ParsesButDoesNotExecute()
    {
        SheetQuerySpec spec = SheetQuerySpec.Parse("""
            FROM Sales!A1:F11 HEADER COLUMN A
            SELECT COUNT()
            """);

        using var ms = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(ms);

        var ex = Assert.Throws<NotSupportedException>(() => workbook.ExecuteQuery(spec));
        Assert.Contains("HEADER COLUMN", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT Region", "Projected row columns are not supported")]
    [InlineData("SELECT TOTAL(Units)", "Unknown aggregate 'TOTAL'")]
    public void Parse_UnsupportedSelectSyntax_ThrowsRepairableDiagnostic(string selectClause, string expectedMessage)
    {
        string query = string.Create(CultureInfo.InvariantCulture, $"""
            FROM Sales!A1:F11 HEADER AUTO
            {selectClause}
            """);

        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse(query));
        Assert.Contains(expectedMessage, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_GroupByWithSelectAll_ThrowsRepairableDiagnostic()
    {
        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT *
            GROUP BY Region
            """));

        Assert.Contains("GROUP BY is not valid with SELECT *", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_OrPredicate_ThrowsRepairableDiagnostic()
    {
        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT COUNT()
            WHERE Region = "EMEA" OR Region = "APAC"
            """));

        Assert.Contains("OR is not supported", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_BooleanOrderingPredicate_ThrowsRepairableDiagnostic()
    {
        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse("""
            FROM Sales!A1:F11 HEADER AUTO
            SELECT COUNT()
            WHERE OnPromo > true
            """));

        Assert.Contains("Boolean predicates support '=' and '!=' only", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UnboundedRange_ThrowsRepairableDiagnostic()
    {
        var ex = Assert.Throws<QueryDslException>(() => SheetQuerySpec.Parse("""
            FROM Sales!A:F HEADER AUTO
            SELECT COUNT()
            """));

        Assert.Contains("FROM range must be a bounded A1 range", ex.Message, StringComparison.Ordinal);
    }
}
