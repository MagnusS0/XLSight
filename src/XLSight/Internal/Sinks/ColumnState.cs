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
    // Each set is allocated on first use, and all sets are nulled out once the combined
    // count hits DistinctCap and DistinctEstimate is latched (_capped distinguishes
    // "never used" from "capped" so tracking stops permanently after the cap).
    internal HashSet<int>? DistinctSstIds;
    internal HashSet<long>? DistinctNumbers;  // BitConverter.DoubleToInt64Bits
    internal HashSet<long>? DistinctDates;    // BitConverter.DoubleToInt64Bits
    internal HashSet<string>? DistinctInlineStrings;
    internal byte BooleanSeen;   // bit 0 = false seen, bit 1 = true seen
    internal int DistinctEstimate;
    private bool _capped;

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
    /// Uses the raw SST index for distinct tracking and <see cref="ISharedStringSource.GetCharCount"/>
    /// for text length — both zero allocation.
    /// </summary>
    internal void RecordSharedString(int sstIndex, ISharedStringSource sst)
    {
        NonEmptyCount++;
        TextCount++;

        int len = sst.GetCharCount(sstIndex);
        if (len > MaxTextLength)
        {
            MaxTextLength = len;
        }

        if (!_capped)
        {
            (DistinctSstIds ??= []).Add(sstIndex);
            if (DistinctSstIds.Count >= DistinctCap)
            {
                LatchEstimateAndStopTracking();
            }
        }
    }

    /// <summary>
    /// General path for non-shared-string cells (numbers, dates, booleans, inline strings, errors).
    /// </summary>
    internal void RecordValue(ExcelCellValue value)
    {
        if (value.IsEmpty)
        {
            return;
        }

        NonEmptyCount++;

        switch (value.CellType)
        {
            case CellType.Number:
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

            case CellType.Date:
                DateCount++;
                TrackDistinctLong(ref DistinctDates, value.AsDate().Ticks);
                break;

            case CellType.Text:
                TextCount++;
                string text = value.AsText();
                int len = text.Length;
                if (len > MaxTextLength) { MaxTextLength = len; }
                TrackDistinctString(text);
                break;

            case CellType.Boolean:
                BooleanCount++;
                BooleanSeen |= value.AsBoolean() ? (byte)2 : (byte)1;
                break;

            case CellType.Error:
                ErrorCount++;
                break;
        }
    }

    private void TrackDistinctLong(ref HashSet<long>? set, long key)
    {
        if (_capped) { return; }
        (set ??= []).Add(key);
        if (set.Count >= DistinctCap)
        {
            LatchEstimateAndStopTracking();
        }
    }

    private void TrackDistinctString(string value)
    {
        if (_capped) { return; }
        (DistinctInlineStrings ??= new(StringComparer.Ordinal)).Add(value);
        if (DistinctInlineStrings.Count >= DistinctCap)
        {
            LatchEstimateAndStopTracking();
        }
    }

    private void LatchEstimateAndStopTracking()
    {
        DistinctEstimate = DistinctCount;
        _capped = true;
        DistinctSstIds = null;
        DistinctNumbers = null;
        DistinctDates = null;
        DistinctInlineStrings = null;
    }
}
