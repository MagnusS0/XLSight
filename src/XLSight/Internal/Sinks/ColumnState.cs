namespace XLSight.Internal.Sinks;

/// <summary>Per-column accumulator used by <see cref="AnalysisSink"/>.</summary>
internal sealed class ColumnState
{
    internal int NonEmptyCount;
    internal int NumberCount;
    internal int TextCount;
    internal int DateCount;
    internal int BooleanCount;
    internal int ErrorCount;
    internal double MinNumeric = double.MaxValue;
    internal double MaxNumeric = double.MinValue;
    internal bool HasNumeric;
    internal int MaxTextLength;
    internal bool HasFormulas;

    // Distinct tracking — typed per-kind to avoid string allocations.
    // SST: integer index (zero-alloc read); Numbers: double bits; Dates: double bits;
    // Booleans: two-bit flags; Errors: int code; Inline strings: string (unavoidable).
    // Each set is nulled out once it hits DistinctCap and DistinctEstimate is latched.
    internal HashSet<int>? DistinctSstIds = new();
    internal HashSet<long>? DistinctNumbers = new();  // BitConverter.DoubleToInt64Bits
    internal HashSet<long>? DistinctDates = new();    // BitConverter.DoubleToInt64Bits
    internal HashSet<string>? DistinctInlineStrings = new(StringComparer.Ordinal);
    internal byte BooleanSeen;   // bit 0 = false seen, bit 1 = true seen
    internal int DistinctEstimate;

    private const int DistinctCap = 1000;

    /// <summary>Combined distinct count across all typed sets, or the capped estimate.</summary>
    internal int DistinctCount
    {
        get
        {
            if (DistinctEstimate > 0)
            {
                return DistinctEstimate;
            }

            int count = 0;
            if (DistinctSstIds is not null) { count += DistinctSstIds.Count; }
            if (DistinctNumbers is not null) { count += DistinctNumbers.Count; }
            if (DistinctDates is not null) { count += DistinctDates.Count; }
            if (DistinctInlineStrings is not null) { count += DistinctInlineStrings.Count; }
            count += BooleanCount > 0 ? System.Numerics.BitOperations.PopCount(BooleanSeen) : 0;
            return count;
        }
    }

    /// <summary>
    /// Fast path for <see cref="CellDataKind.SharedString"/> cells.
    /// Uses the raw SST index for distinct tracking and <see cref="Metadata.SharedStringTable.GetCharCount"/>
    /// for text length — both zero allocation.
    /// </summary>
    internal void RecordSharedString(int sstIndex, Metadata.SharedStringTable sst)
    {
        NonEmptyCount++;
        TextCount++;

        int len = sst.GetCharCount(sstIndex);
        if (len > MaxTextLength)
        {
            MaxTextLength = len;
        }

        if (DistinctSstIds is not null)
        {
            DistinctSstIds.Add(sstIndex);
            if (DistinctSstIds.Count >= DistinctCap)
            {
                DistinctEstimate = DistinctCount;
                NullAllSets();
            }
        }
    }

    /// <summary>
    /// General path for non-shared-string cells (numbers, dates, booleans, inline strings, errors).
    /// </summary>
    internal void RecordValue(Models.ExcelCellValue value)
    {
        if (value.IsEmpty)
        {
            return;
        }

        NonEmptyCount++;

        switch (value.CellType)
        {
            case Models.CellType.Number:
                NumberCount++;
                double num = value.AsNumber();
                if (!HasNumeric)
                {
                    MinNumeric = num;
                    MaxNumeric = num;
                    HasNumeric = true;
                }
                else
                {
                    if (num < MinNumeric) { MinNumeric = num; }
                    if (num > MaxNumeric) { MaxNumeric = num; }
                }

                TrackDistinctLong(ref DistinctNumbers, System.Runtime.CompilerServices.Unsafe.BitCast<double, long>(num));
                break;

            case Models.CellType.Date:
                DateCount++;
                TrackDistinctLong(ref DistinctDates, value.AsDate().Ticks);
                break;

            case Models.CellType.Text:
                TextCount++;
                string text = value.AsText();
                int len = text.Length;
                if (len > MaxTextLength) { MaxTextLength = len; }
                TrackDistinctString(text);
                break;

            case Models.CellType.Boolean:
                BooleanCount++;
                BooleanSeen |= value.AsBoolean() ? (byte)2 : (byte)1;
                break;

            case Models.CellType.Error:
                ErrorCount++;
                break;
        }
    }

    private void TrackDistinctLong(ref HashSet<long>? set, long key)
    {
        if (set is null) { return; }
        set.Add(key);
        if (set.Count >= DistinctCap)
        {
            DistinctEstimate = DistinctCount;
            NullAllSets();
        }
    }

    private void TrackDistinctString(string value)
    {
        if (DistinctInlineStrings is null) { return; }
        DistinctInlineStrings.Add(value);
        if (DistinctInlineStrings.Count >= DistinctCap)
        {
            DistinctEstimate = DistinctCount;
            NullAllSets();
        }
    }

    private void NullAllSets()
    {
        DistinctSstIds = null;
        DistinctNumbers = null;
        DistinctDates = null;
        DistinctInlineStrings = null;
    }
}
