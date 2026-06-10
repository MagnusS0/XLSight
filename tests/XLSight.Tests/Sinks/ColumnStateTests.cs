using XLSight.Internal.Sinks;
using XLSight.Tests.Infrastructure;
using Xunit;

namespace XLSight.Tests.Sinks;

public sealed class ColumnStateTests
{
    // ── RecordValue — Empty cell ──────────────────────────────────────────────

    [Fact]
    public void RecordValue_EmptyCell_DoesNotIncrementNonEmptyCount()
    {
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.Empty);
        Assert.Equal(0, col.NonEmptyCount);
    }

    // ── RecordValue — Error ───────────────────────────────────────────────────

    [Fact]
    public void RecordValue_ErrorCell_IncrementsErrorCount()
    {
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.FromError("#REF!"));
        Assert.Equal(1, col.ErrorCount);
        Assert.Equal(1, col.NonEmptyCount);
    }

    [Fact]
    public void RecordValue_MultipleErrors_AccumulatesCount()
    {
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.FromError("#REF!"));
        col.RecordValue(ExcelCellValue.FromError("#VALUE!"));
        Assert.Equal(2, col.ErrorCount);
    }

    // ── RecordValue — Boolean ─────────────────────────────────────────────────

    [Fact]
    public void RecordValue_BoolTrue_SetsBit1InBooleanSeen()
    {
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.FromBoolean(true));
        Assert.Equal(1, col.BooleanCount);
        Assert.Equal((byte)2, col.BooleanSeen);
    }

    [Fact]
    public void RecordValue_BoolFalse_SetsBit0InBooleanSeen()
    {
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.FromBoolean(false));
        Assert.Equal(1, col.BooleanCount);
        Assert.Equal((byte)1, col.BooleanSeen);
    }

    [Fact]
    public void RecordValue_BothBooleans_SetsBothBits()
    {
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.FromBoolean(true));
        col.RecordValue(ExcelCellValue.FromBoolean(false));
        Assert.Equal((byte)3, col.BooleanSeen);
    }

    // ── RecordValue — Number ──────────────────────────────────────────────────

    [Fact]
    public void RecordValue_Numbers_TrackMinMax()
    {
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.FromNumber(10.0));
        col.RecordValue(ExcelCellValue.FromNumber(3.0));
        col.RecordValue(ExcelCellValue.FromNumber(7.0));
        Assert.True(col.HasNumeric);
        Assert.Equal(3.0, col.MinNumeric);
        Assert.Equal(10.0, col.MaxNumeric);
    }

    [Fact]
    public void RecordValue_FirstNumber_SetsMinAndMax()
    {
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.FromNumber(42.0));
        Assert.Equal(42.0, col.MinNumeric);
        Assert.Equal(42.0, col.MaxNumeric);
    }

    // ── RecordValue — Date ────────────────────────────────────────────────────

    [Fact]
    public void RecordValue_DateCell_IncrementsDateCount()
    {
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.FromDate(new DateTime(2024, 1, 1)));
        Assert.Equal(1, col.DateCount);
        Assert.Equal(1, col.NonEmptyCount);
    }

    // ── RecordValue — Text (inline string) ───────────────────────────────────

    [Fact]
    public void RecordValue_TextCell_TracksMaxLength()
    {
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.FromText("short"));
        col.RecordValue(ExcelCellValue.FromText("a much longer string"));
        Assert.Equal(20, col.MaxTextLength);
        Assert.Equal(2, col.TextCount);
    }

    // ── RecordSharedString ────────────────────────────────────────────────────

    [Fact]
    public void RecordSharedString_IncrementsTextAndNonEmpty()
    {
        var sst = SstBuilder.Make("hello", "world");
        var col = new ColumnState();
        col.RecordSharedString(0, sst);
        Assert.Equal(1, col.TextCount);
        Assert.Equal(1, col.NonEmptyCount);
        Assert.Equal(5, col.MaxTextLength);
    }

    [Fact]
    public void RecordSharedString_LongerString_UpdatesMaxTextLength()
    {
        var sst = SstBuilder.Make("hi", "longer string here");
        var col = new ColumnState();
        col.RecordSharedString(0, sst); // "hi" = 2 chars
        col.RecordSharedString(1, sst); // "longer string here" = 18 chars
        Assert.Equal(18, col.MaxTextLength);
    }

    // ── DistinctCount — with DistinctEstimate ─────────────────────────────────

    [Fact]
    public void DistinctCount_WhenEstimateIsSet_ReturnsEstimate()
    {
        var col = new ColumnState();
        col.DistinctEstimate = 5000;
        Assert.Equal(5000, col.DistinctCount);
    }

    [Fact]
    public void DistinctCount_BeforeEstimate_SumsAllSets()
    {
        var sst = SstBuilder.Make("a", "b", "c");
        var col = new ColumnState();
        col.RecordSharedString(0, sst);
        col.RecordSharedString(1, sst);
        col.RecordValue(ExcelCellValue.FromNumber(1.0));
        // 2 SST + 1 number
        Assert.Equal(3, col.DistinctCount);
    }

    // ── DistinctCap overflow — numbers ────────────────────────────────────────

    [Fact]
    public void RecordValue_Numbers_WhenDistinctCapReached_NullsAllSets()
    {
        var col = new ColumnState();
        for (int i = 0; i < 1000; i++)
        {
            col.RecordValue(ExcelCellValue.FromNumber(i));
        }
        // After cap: sets are nulled and estimate is latched
        Assert.Null(col.DistinctNumbers);
        Assert.True(col.DistinctEstimate > 0);
    }

    // ── DistinctCap overflow — shared strings ─────────────────────────────────

    [Fact]
    public void RecordSharedString_WhenDistinctCapReached_NullsAllSets()
    {
        var strings = Enumerable.Range(0, 1001).Select(i => $"str{i}").ToArray();
        var sst = SstBuilder.Make(strings);
        var col = new ColumnState();
        for (int i = 0; i < 1000; i++)
        {
            col.RecordSharedString(i, sst);
        }
        Assert.Null(col.DistinctSstIds);
        Assert.True(col.DistinctEstimate > 0);
    }

    // ── DistinctCap overflow — inline strings ────────────────────────────────

    [Fact]
    public void RecordValue_InlineStrings_WhenDistinctCapReached_NullsAllSets()
    {
        var col = new ColumnState();
        for (int i = 0; i < 1000; i++)
        {
            col.RecordValue(ExcelCellValue.FromText($"inline{i}"));
        }
        Assert.Null(col.DistinctInlineStrings);
        Assert.True(col.DistinctEstimate > 0);
    }

    // ── DistinctCap overflow — dates ─────────────────────────────────────────

    [Fact]
    public void RecordValue_Dates_WhenDistinctCapReached_NullsAllSets()
    {
        var col = new ColumnState();
        var baseDate = new DateTime(2000, 1, 1);
        for (int i = 0; i < 1000; i++)
        {
            col.RecordValue(ExcelCellValue.FromDate(baseDate.AddDays(i)));
        }
        Assert.Null(col.DistinctDates);
        Assert.True(col.DistinctEstimate > 0);
    }

    // ── DistinctCount after NullAllSets ──────────────────────────────────────

    [Fact]
    public void DistinctCount_AfterNullAllSets_ReturnsEstimate()
    {
        var col = new ColumnState();
        for (int i = 0; i < 1000; i++)
        {
            col.RecordValue(ExcelCellValue.FromNumber(i));
        }
        int distinct = col.DistinctCount;
        Assert.True(distinct >= 1000);
    }

    // ── TrackDistinctLong with null set ──────────────────────────────────────

    [Fact]
    public void RecordValue_NumberAfterNullAllSets_DoesNotThrow()
    {
        var col = new ColumnState();
        // Force NullAllSets by hitting the cap with numbers
        for (int i = 0; i < 1000; i++)
        {
            col.RecordValue(ExcelCellValue.FromNumber(i));
        }
        // Now DistinctNumbers is null — adding more should not throw
        col.RecordValue(ExcelCellValue.FromNumber(9999.0));
        Assert.Equal(1001, col.NumberCount);
    }

    // ── BooleanCount in DistinctCount ─────────────────────────────────────────

    [Fact]
    public void DistinctCount_IncludesBooleanBits()
    {
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.FromBoolean(true));
        col.RecordValue(ExcelCellValue.FromBoolean(false));
        // BooleanCount > 0 with both bits set → 2 distinct booleans
        Assert.Equal(2, col.DistinctCount);
    }

    [Fact]
    public void DistinctCount_OnlyOneBooleanSeen_CountsAsOne()
    {
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.FromBoolean(true));
        col.RecordValue(ExcelCellValue.FromBoolean(true));
        // Only bit 1 set → 1 distinct boolean
        Assert.Equal(1, col.DistinctCount);
    }

    // ── BuildDistinctValues ───────────────────────────────────────────────────

    [Fact]
    public void BuildDistinctValues_SharedStrings_ResolvesAndSortsOrdinal()
    {
        var sst = SstBuilder.Make("EMEA", "APAC", "AMER");
        var col = new ColumnState();
        col.RecordSharedString(0, sst);
        col.RecordSharedString(1, sst);
        col.RecordSharedString(2, sst);
        col.RecordSharedString(1, sst);

        string[]? values = col.BuildDistinctValues(32, sst);

        Assert.NotNull(values);
        Assert.Equal(["AMER", "APAC", "EMEA"], values);
    }

    [Fact]
    public void BuildDistinctValues_SstAndInlineCopiesOfSameText_Deduplicated()
    {
        var sst = SstBuilder.Make("EMEA");
        var col = new ColumnState();
        col.RecordSharedString(0, sst);
        col.RecordValue(ExcelCellValue.FromText("EMEA"));
        col.RecordValue(ExcelCellValue.FromText("APAC"));

        string[]? values = col.BuildDistinctValues(32, sst);

        Assert.NotNull(values);
        Assert.Equal(["APAC", "EMEA"], values);
    }

    [Fact]
    public void BuildDistinctValues_MixedKinds_GroupsTextNumberDateBoolean()
    {
        var sst = SstBuilder.Make("label");
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.FromBoolean(true));
        col.RecordValue(ExcelCellValue.FromNumber(7.5));
        col.RecordValue(ExcelCellValue.FromNumber(-2.0));
        col.RecordValue(ExcelCellValue.FromDate(new DateTime(2024, 3, 1)));
        col.RecordSharedString(0, sst);

        string[]? values = col.BuildDistinctValues(32, sst);

        Assert.NotNull(values);
        Assert.Equal(["label", "-2", "7.5", "2024-03-01", "TRUE"], values);
    }

    [Fact]
    public void BuildDistinctValues_DateWithTimeComponent_KeepsTimeInFormat()
    {
        var sst = SstBuilder.Make();
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.FromDate(new DateTime(2024, 3, 1, 14, 30, 0)));

        string[]? values = col.BuildDistinctValues(32, sst);

        Assert.NotNull(values);
        Assert.Equal(["2024-03-01T14:30:00"], values);
    }

    [Fact]
    public void BuildDistinctValues_CountAboveCap_ReturnsNull()
    {
        var sst = SstBuilder.Make();
        var col = new ColumnState();
        for (int i = 0; i < 33; i++)
        {
            col.RecordValue(ExcelCellValue.FromNumber(i));
        }

        Assert.Null(col.BuildDistinctValues(32, sst));
    }

    [Fact]
    public void BuildDistinctValues_AfterTrackingCapLatched_ReturnsNull()
    {
        var sst = SstBuilder.Make();
        var col = new ColumnState();
        for (int i = 0; i < 1000; i++)
        {
            col.RecordValue(ExcelCellValue.FromNumber(i));
        }

        // Even a huge cap cannot recover values once tracking stopped.
        Assert.Null(col.BuildDistinctValues(int.MaxValue, sst));
    }

    [Fact]
    public void BuildDistinctValues_ZeroCap_ReturnsNull()
    {
        var sst = SstBuilder.Make();
        var col = new ColumnState();
        col.RecordValue(ExcelCellValue.FromNumber(1.0));

        Assert.Null(col.BuildDistinctValues(0, sst));
    }

    [Fact]
    public void BuildDistinctValues_EmptyColumn_ReturnsNull()
    {
        var sst = SstBuilder.Make();
        var col = new ColumnState();

        Assert.Null(col.BuildDistinctValues(32, sst));
    }
}
