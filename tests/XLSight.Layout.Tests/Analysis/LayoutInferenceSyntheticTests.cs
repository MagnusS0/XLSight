using XLSight.Analysis.Layout;
using Xunit;
using static XLSight.Layout.Tests.Analysis.LayoutTestWorkbook;

namespace XLSight.Layout.Tests.Analysis;

/// <summary>Small in-memory workbooks proving layout-inference behaviors that would otherwise
/// only be exercised by the external corpora in <see cref="LayoutInferenceIntegrationTests"/>.</summary>
public sealed class LayoutInferenceSyntheticTests
{
    // Two stacked statements, each with its own reprinted year header, separated by a title row.
    private static readonly RowSpec[] StackedSectionsRows =
    [
        Row(1, Text("A", "Income statement")),
        Row(2, Number("B", 2023), Number("C", 2024), Number("D", 2025)),
        Row(3, Text("A", "Revenue"), Number("B", 100), Number("C", 110), Number("D", 120)),
        Row(4, Text("A", "Costs"), Number("B", 40), Number("C", 45), Number("D", 50)),
        Row(5, Text("A", "EBITDA"), Number("B", 60), Number("C", 65), Number("D", 70)),
        Row(7, Text("A", "Balance sheet")),
        Row(8, Number("B", 2023), Number("C", 2024), Number("D", 2025)),
        Row(9, Text("A", "Assets"), Number("B", 500), Number("C", 520), Number("D", 540)),
        Row(10, Text("A", "Liabilities"), Number("B", 300), Number("C", 310), Number("D", 320)),
        Row(11, Text("A", "Equity"), Number("B", 200), Number("C", 210), Number("D", 220)),
    ];

    // One table sits beside a CAGR/Avg block across an empty spacer; both share row labels.
    private static readonly RowSpec[] SiblingFieldsRows =
    [
        Row(1, Number("B", 2023), Number("C", 2024), Number("D", 2025), Text("F", "CAGR"), Text("G", "Avg")),
        Row(2, Text("A", "Revenue"), Number("B", 100), Number("C", 110), Number("D", 120), Number("F", 0.1), Number("G", 105)),
        Row(3, Text("A", "Costs"), Number("B", 40), Number("C", 45), Number("D", 50), Number("F", 0.05), Number("G", 45)),
        Row(4, Text("A", "EBITDA"), Number("B", 60), Number("C", 65), Number("D", 70), Number("F", 0.08), Number("G", 65)),
        Row(5, Text("A", "NetIncome"), Number("B", 50), Number("C", 55), Number("D", 60), Number("F", 0.1), Number("G", 57)),
    ];

    // A repeated year column between labels and measures must peel off as a context axis.
    private static readonly RowSpec[] LeadingYearColumnRows =
    [
        Row(1, Text("A", "Name"), Text("B", "Year"), Text("C", "Assets"), Text("D", "Deposits")),
        Row(2, Text("A", "Bank A"), Number("B", 2020), Number("C", 100), Number("D", 50)),
        Row(3, Text("A", "Bank A"), Number("B", 2021), Number("C", 110), Number("D", 55)),
        Row(4, Text("A", "Bank A"), Number("B", 2022), Number("C", 120), Number("D", 60)),
        Row(5, Text("A", "Bank B"), Number("B", 2020), Number("C", 200), Number("D", 90)),
        Row(6, Text("A", "Bank B"), Number("B", 2021), Number("C", 210), Number("D", 95)),
        Row(7, Text("A", "Bank B"), Number("B", 2022), Number("C", 220), Number("D", 100)),
    ];

    // A uniform first data row under weekday labels is a dense field, not matrix coordinates.
    private static readonly RowSpec[] CalendarGridRows =
    [
        Row(1, Text("A", "Mon"), Text("B", "Tue"), Text("C", "Wed"), Text("D", "Thu"), Text("E", "Fri"), Text("F", "Sat"), Text("G", "Sun")),
        Row(2, Number("A", 1), Number("B", 2), Number("C", 3), Number("D", 4), Number("E", 5), Number("F", 6), Number("G", 7)),
        Row(3, Number("A", 8), Number("B", 9), Number("C", 10), Number("D", 11), Number("E", 12), Number("F", 13), Number("G", 14)),
        Row(4, Number("A", 15), Number("B", 16), Number("C", 17), Number("D", 18), Number("E", 19), Number("F", 20), Number("G", 21)),
        Row(5, Number("A", 22), Number("B", 23), Number("C", 24), Number("D", 25), Number("E", 26), Number("F", 27), Number("G", 28)),
    ];

    // Uniform quantity and first-price runs must not seed a phantom matrix.
    private static readonly RowSpec[] PricingTableRows =
    [
        Row(1, Text("A", "Quantity"), Text("B", "North"), Text("C", "South"), Text("D", "East")),
        Row(2, Number("A", 10), Number("B", 5.0), Number("C", 5.5), Number("D", 6.0)),
        Row(3, Number("A", 20), Number("B", 4.8), Number("C", 5.9), Number("D", 6.4)),
        Row(4, Number("A", 30), Number("B", 4.1), Number("C", 5.2), Number("D", 6.9)),
        Row(5, Number("A", 40), Number("B", 3.9), Number("C", 4.8), Number("D", 7.7)),
    ];

    // Band labels sit above a reprinted year row, which must stay outside the measure field.
    private static readonly RowSpec[] BandOverYearHeaderRows =
    [
        Row(1, Text("B", "Group A"), Text("C", "Group A"), Text("D", "Group B"), Text("E", "Group B")),
        Row(2, Number("B", 2023), Number("C", 2024), Number("D", 2023), Number("E", 2024)),
        Row(3, Text("A", "Revenue"), Number("B", 10), Number("C", 13), Number("D", 12), Number("E", 18)),
        Row(4, Text("A", "Costs"), Number("B", 20), Number("C", 27), Number("D", 22), Number("E", 31)),
        Row(5, Text("A", "EBITDA"), Number("B", 34), Number("C", 41), Number("D", 39), Number("E", 52)),
    ];

    // Non-monotonic integers in the year range are measures, not a year context axis.
    private static readonly RowSpec[] NonMonotonicUnitsColumnRows =
    [
        Row(1, Text("A", "Product"), Text("B", "Units"), Text("C", "Price"), Text("D", "Total")),
        Row(2, Text("A", "Alpha"), Number("B", 1950), Number("C", 5), Number("D", 9750)),
        Row(3, Text("A", "Beta"), Number("B", 2010), Number("C", 7), Number("D", 14070)),
        Row(4, Text("A", "Gamma"), Number("B", 1980), Number("C", 6), Number("D", 11880)),
    ];

    // A stepped forecast row embedded in ordinary data must not seed a phantom matrix.
    private static readonly RowSpec[] EmbeddedForecastRows =
    [
        Row(1, Text("A", "Name"), Text("B", "Year"), Text("C", "Metric0"), Text("D", "Metric1"), Text("E", "Metric2"), Text("F", "Metric3"), Text("G", "Metric4")),
        Row(2, Text("A", "Foo"), Number("B", 2020), Number("C", 200), Number("D", 10), Number("E", 20), Number("F", 30), Number("G", 40)),
        Row(3, Text("A", "Foo"), Number("B", 2021), Number("C", 210), Number("D", 41), Number("E", 42), Number("F", 43), Number("G", 44)),
        Row(4, Text("A", "Foo"), Number("B", 2022), Number("C", 463), Number("D", 590.4), Number("E", 670.9), Number("F", 752.2), Number("G", 834.9)),
        Row(5, Text("A", "Foo"), Number("B", 2023), Number("C", 463), Number("D", 111), Number("E", 112), Number("F", 113), Number("G", 114)),
        Row(6, Text("A", "Foo"), Number("B", 2024), Number("C", 1620), Number("D", 121), Number("E", 122), Number("F", 123), Number("G", 124)),
        Row(7, Text("A", "Foo"), Number("B", 2025), Number("C", 2786), Number("D", 131), Number("E", 132), Number("F", 133), Number("G", 134)),
    ];

    // A lone caption above a text-kind header row should become its horizontal-axis title.
    private static readonly RowSpec[] CaptionedTextHeaderRows =
    [
        Row(1, Text("B", "Growth (%)")),
        Row(2, Text("A", "Name"), Text("B", "Q1"), Text("C", "Q2"), Text("D", "Q3")),
        Row(3, Text("A", "Foo"), Number("B", 10), Number("C", 11), Number("D", 12)),
        Row(4, Text("A", "Bar"), Number("B", 20), Number("C", 21), Number("D", 22)),
        Row(5, Text("A", "Baz"), Number("B", 30), Number("C", 31), Number("D", 32)),
    ];

    // No-data label rows split one vertical axis into titled sections.
    private static readonly RowSpec[] AxisSectionRows =
    [
        Row(1, Number("B", 2023), Number("C", 2024)),
        Row(2, Text("A", "Funding")),
        Row(3, Text("A", "Deposits"), Number("B", 100), Number("C", 110)),
        Row(4, Text("A", "Savings"), Number("B", 200), Number("C", 210)),
        Row(5, Text("A", "Total Funding"), Number("B", 300), Number("C", 320)),
        Row(6, Text("A", "Loans")),
        Row(7, Text("A", "Mortgages"), Number("B", 150), Number("C", 160)),
        Row(8, Text("A", "Auto"), Number("B", 90), Number("C", 95)),
        Row(9, Text("A", "Total Loans"), Number("B", 240), Number("C", 255)),
    ];

    // A right-side metric block extends beyond the left table's row-span anchor.
    private static readonly RowSpec[] SiblingRowsLeftAnchorRows =
    [
        Row(1, Number("B", 2023), Number("C", 2024), Text("E", "CAGR"), Text("F", "Avg")),
        Row(2, Text("A", "Revenue"), Number("B", 100), Number("C", 110), Number("E", 0.1), Number("F", 105)),
        Row(3, Text("A", "Costs"), Number("B", 40), Number("C", 44), Number("E", 0.1), Number("F", 42)),
        Row(4, Text("A", "EBITDA"), Number("B", 60), Number("C", 66), Number("E", 0.1), Number("F", 63)),
        Row(5, Text("A", "Other income"), Number("E", 5), Number("F", 6)),
        Row(6, Text("A", "Other costs"), Number("E", 7), Number("F", 8)),
    ];

    // A shorter table between two panels must prevent merging the panels across it.
    private static readonly RowSpec[] InterveningTableRows =
    [
        Row(1, Number("B", 2023), Number("C", 2024), Text("F", "Current"), Text("G", "Prior")),
        Row(2, Text("A", "Revenue"), Number("B", 100), Number("C", 110), Number("F", 10), Number("G", 11)),
        Row(3, Text("A", "Costs"), Number("B", 40), Number("C", 44), Text("D", "Short A"), Text("E", "Short B"), Number("F", 20), Number("G", 22)),
        Row(4, Text("A", "EBITDA"), Number("B", 60), Number("C", 66), Number("D", 1), Number("E", 2), Number("F", 30), Number("G", 33)),
        Row(5, Text("A", "Tax"), Number("B", 25), Number("C", 28), Number("D", 3), Number("E", 4), Number("F", 40), Number("G", 44)),
        Row(6, Text("A", "Cash"), Number("B", 35), Number("C", 38), Number("F", 50), Number("G", 55)),
    ];

    [Fact]
    public void StackedSections_SplitAtReprintedHeaders_WithGroupTitles()
    {
        SheetLayoutInfo layout = Infer(StackedSectionsRows);

        AssertField(layout, "B3:D5", 2);
        AssertField(layout, "B9:D11", 2);
        AssertAxis(layout, "A3:A5", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        AssertAxis(layout, "A9:A11", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        AssertAxis(layout, "B2:D2", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        AssertAxis(layout, "B8:D8", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        Assert.Equal(["Income statement", "Balance sheet"], layout.Groups.Select(static group => group.Title));
    }

    [Fact]
    public void SiblingFields_ShareRowLabelAxis_AcrossEmptySpacer()
    {
        SheetLayoutInfo layout = Infer(SiblingFieldsRows);

        MeasureFieldInfo left = AssertField(layout, "B2:D5", 2);
        MeasureFieldInfo right = AssertField(layout, "F2:G5", 2);
        LayoutAxis labelAxis = AssertAxis(layout, "A2:A5", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        Assert.Contains(labelAxis.Id, left.AxisIds);
        Assert.Contains(labelAxis.Id, right.AxisIds);
    }

    [Fact]
    public void NumericCoordinateMatrix_GetsOwnAxes()
    {
        SheetLayoutInfo layout = Infer(NumericMatrixRows());

        MeasureFieldInfo matrix = AssertField(layout, "B3:F7", 2);
        LayoutAxis waccAxis = AssertAxis(layout, "A3:A7", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        LayoutAxis growthAxis = AssertAxis(layout, "B2:F2", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        Assert.Equal(LayoutAxisValueKind.Numeric, waccAxis.ValueKind);
        Assert.Equal(LayoutAxisValueKind.Numeric, growthAxis.ValueKind);
        Assert.Contains(waccAxis.Id, matrix.AxisIds);
        Assert.Contains(growthAxis.Id, matrix.AxisIds);
    }

    [Fact]
    public void ValuelessFormulaCell_DoesNotMarkNextCellAsFormula()
    {
        SheetLayoutInfo layout = Infer(NumericMatrixRows(valueLessFormula: true));

        MeasureFieldInfo field = AssertField(layout, "B3:F7", 2);
        Assert.Equal(0, field.Profile.FormulaCount);
    }

    [Fact]
    public void LeadingYearColumn_PeelsIntoContextAxis()
    {
        SheetLayoutInfo layout = Infer(LeadingYearColumnRows);

        MeasureFieldInfo field = AssertField(layout, "C2:D7", 3);
        LayoutAxis primary = AssertAxis(layout, "A2:A7", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        LayoutAxis context = AssertAxis(layout, "B2:B7", LayoutAxisOrientation.Vertical, LayoutAxisRole.Context);
        Assert.Contains(primary.Id, field.AxisIds);
        Assert.Contains(context.Id, field.AxisIds);
    }

    [Fact]
    public void CalendarGrid_DoesNotBecomePhantomMatrix()
    {
        SheetLayoutInfo layout = Infer(CalendarGridRows);

        MeasureFieldInfo field = AssertField(layout, "A2:G5", 1);
        LayoutAxis dayNames = AssertAxis(layout, "A1:G1", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        Assert.Equal(LayoutAxisValueKind.Text, dayNames.ValueKind);
        Assert.Contains(dayNames.Id, field.AxisIds);
        Assert.DoesNotContain(layout.MeasureFields, static field => field.Range == ExcelRange.Parse("B3:G5"));
    }

    [Fact]
    public void PricingTable_UniformFirstRowDoesNotEatTextHeader()
    {
        SheetLayoutInfo layout = Infer(PricingTableRows);

        MeasureFieldInfo field = AssertField(layout, "A2:D5", 1);
        LayoutAxis header = AssertAxis(layout, "A1:D1", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        Assert.Equal(LayoutAxisValueKind.Text, header.ValueKind);
        Assert.Contains(header.Id, field.AxisIds);
        Assert.DoesNotContain(layout.MeasureFields, static field => field.Range == ExcelRange.Parse("B3:D5"));
    }

    [Fact]
    public void BandOverYearHeader_YearRowStaysOutOfMeasureField()
    {
        SheetLayoutInfo layout = Infer(BandOverYearHeaderRows);

        MeasureFieldInfo field = AssertField(layout, "B3:E5", 2);
        LayoutAxis years = AssertAxis(layout, "B2:E2", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        LayoutAxis labels = AssertAxis(layout, "A3:A5", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        Assert.Equal(LayoutAxisValueKind.Numeric, years.ValueKind);
        Assert.Contains(years.Id, field.AxisIds);
        Assert.Contains(labels.Id, field.AxisIds);
    }

    [Fact]
    public void NonMonotonicIntegerColumn_StaysInMeasureField()
    {
        SheetLayoutInfo layout = Infer(NonMonotonicUnitsColumnRows);

        AssertField(layout, "B2:D4", 2);
        Assert.DoesNotContain(
            layout.Axes,
            static axis => axis.Range == ExcelRange.Parse("B2:B4") &&
                (axis.Orientation == LayoutAxisOrientation.Vertical || axis.Role == LayoutAxisRole.Context));
    }

    [Fact]
    public void EmbeddedForecastRow_DoesNotSeedPhantomMatrix()
    {
        SheetLayoutInfo layout = Infer(EmbeddedForecastRows);

        AssertField(layout, "C2:G7", 3);
        Assert.DoesNotContain(layout.MeasureFields, static field => field.Range == ExcelRange.Parse("D5:G7"));
        Assert.Single(layout.MeasureFields);
    }

    [Fact]
    public void HorizontalTextAxis_PicksUpLoneCaptionCellAbove()
    {
        SheetLayoutInfo layout = Infer(CaptionedTextHeaderRows);

        LayoutAxis header = AssertAxis(layout, "B2:D2", LayoutAxisOrientation.Horizontal, LayoutAxisRole.Primary);
        Assert.Equal(LayoutAxisValueKind.Text, header.ValueKind);
        Assert.Equal("Growth (%)", header.Title);
    }

    [Fact]
    public void AxisSections_FromNoDataHeaderRows()
    {
        SheetLayoutInfo layout = Infer(AxisSectionRows);

        LayoutAxis labelAxis = AssertAxis(layout, "A3:A9", LayoutAxisOrientation.Vertical, LayoutAxisRole.Primary);
        Assert.Contains(labelAxis.Sections, static section =>
            string.Equals(section.Title, "Funding", StringComparison.Ordinal) && section.Range == ExcelRange.Parse("A2:A5"));
        Assert.Contains(labelAxis.Sections, static section =>
            string.Equals(section.Title, "Loans", StringComparison.Ordinal) && section.Range == ExcelRange.Parse("A6:A9"));
    }

    [Fact]
    public void SiblingRows_UseLeftmostFieldAsRowSpanAnchor()
    {
        SheetLayoutInfo layout = Infer(SiblingRowsLeftAnchorRows);

        AssertField(layout, "B2:C4", 2);
        AssertField(layout, "E2:F4", 2);
        Assert.DoesNotContain(layout.MeasureFields, static field => field.Range == ExcelRange.Parse("E2:F6"));
    }

    [Fact]
    public void MergeColumnAdjacentFields_DoesNotMergeAcrossDifferentSpanTable()
    {
        SheetLayoutInfo layout = Infer(InterveningTableRows);

        AssertFieldRange(layout, "B2:C6");
        AssertFieldRange(layout, "D4:E5");
        AssertFieldRange(layout, "F2:G6");
        AssertNoOverlappingFields(layout);
    }

    private static RowSpec[] NumericMatrixRows(bool valueLessFormula = false) =>
    [
        Row(2, Number("B", 0.01), Number("C", 0.015), Number("D", 0.02), Number("E", 0.025), Number("F", 0.03)),
        Row(3, Number("A", 0.04), Number("B", 950), valueLessFormula ? Formula("C", "B3*2") : Number("C", 53), Number("D", 956), Number("E", 59), Number("F", 962)),
        Row(4, Number("A", 0.05), Number("B", 60), Number("C", 963), Number("D", 66), Number("E", 969), Number("F", 72)),
        Row(5, Number("A", 0.06), Number("B", 970), Number("C", 73), Number("D", 976), Number("E", 79), Number("F", 982)),
        Row(6, Number("A", 0.07), Number("B", 80), Number("C", 983), Number("D", 86), Number("E", 989), Number("F", 92)),
        Row(7, Number("A", 0.08), Number("B", 990), Number("C", 93), Number("D", 996), Number("E", 99), Number("F", 1002)),
    ];

    private static SheetLayoutInfo Infer(RowSpec[] rows)
    {
        using var ms = LayoutTestWorkbook.Build(rows);
        using var workbook = ExcelWorkbook.Open(ms);
        return workbook.AnalyzeLayout("Data");
    }

    private static MeasureFieldInfo AssertField(SheetLayoutInfo layout, string range, int rank)
    {
        MeasureFieldInfo field = AssertFieldRange(layout, range);
        Assert.Equal(rank, field.Rank);
        return field;
    }

    private static MeasureFieldInfo AssertFieldRange(SheetLayoutInfo layout, string range)
    {
        var expectedRange = ExcelRange.Parse(range);
        MeasureFieldInfo? field = layout.MeasureFields.FirstOrDefault(field => field.Range == expectedRange);
        Assert.NotNull(field);
        return field;
    }

    private static LayoutAxis AssertAxis(
        SheetLayoutInfo layout,
        string range,
        LayoutAxisOrientation orientation,
        LayoutAxisRole role)
    {
        var expectedRange = ExcelRange.Parse(range);
        LayoutAxis? axis = layout.Axes.FirstOrDefault(axis =>
            axis.Range == expectedRange &&
            axis.Orientation == orientation &&
            axis.Role == role);
        Assert.NotNull(axis);
        return axis;
    }

    private static void AssertNoOverlappingFields(SheetLayoutInfo layout)
    {
        for (int i = 0; i < layout.MeasureFields.Count; i++)
        {
            for (int j = i + 1; j < layout.MeasureFields.Count; j++)
            {
                Assert.False(
                    Overlaps(layout.MeasureFields[i].Range, layout.MeasureFields[j].Range),
                    $"{layout.MeasureFields[i].Range} overlaps {layout.MeasureFields[j].Range}");
            }
        }
    }

    private static bool Overlaps(ExcelRange left, ExcelRange right) =>
        Math.Min(left.BottomRight.Column, right.BottomRight.Column) >= Math.Max(left.TopLeft.Column, right.TopLeft.Column) &&
        Math.Min(left.BottomRight.Row, right.BottomRight.Row) >= Math.Max(left.TopLeft.Row, right.TopLeft.Row);
}
