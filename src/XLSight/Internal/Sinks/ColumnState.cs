using System.Globalization;

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

    /// <summary>
    /// Materializes the distinct values as display strings when tracking is still exact and the
    /// combined count is within <paramref name="cap"/>. Returns null when the column was capped,
    /// is empty, or exceeds the cap. Values are grouped by kind (text, number, date, boolean)
    /// and sorted within each kind for deterministic output.
    /// </summary>
    internal string[]? BuildDistinctValues(int cap, ISharedStringSource sst)
    {
        if (_capped || cap <= 0)
        {
            return null;
        }

        int count = DistinctCount;
        if (count == 0 || count > cap)
        {
            return null;
        }

        var values = new List<string>(count);
        AddDistinctTexts(values, sst);
        AddDistinctNumbers(values);
        AddDistinctDates(values);

        if (BooleanCount > 0)
        {
            if ((BooleanSeen & 1) != 0) { values.Add("FALSE"); }
            if ((BooleanSeen & 2) != 0) { values.Add("TRUE"); }
        }

        return [.. values];
    }

    private void AddDistinctTexts(List<string> values, ISharedStringSource sst)
    {
        if (DistinctSstIds is null && DistinctInlineStrings is null)
        {
            return;
        }

        // SST-resolved and inline copies of the same text must not appear twice.
        var texts = new SortedSet<string>(StringComparer.Ordinal);
        if (DistinctSstIds is not null)
        {
            foreach (int id in DistinctSstIds) { texts.Add(sst.GetString(id)); }
        }

        if (DistinctInlineStrings is not null)
        {
            foreach (string text in DistinctInlineStrings) { texts.Add(text); }
        }

        values.AddRange(texts);
    }

    private void AddDistinctNumbers(List<string> values)
    {
        if (DistinctNumbers is null)
        {
            return;
        }

        var numbers = new double[DistinctNumbers.Count];
        int i = 0;
        foreach (long bits in DistinctNumbers) { numbers[i++] = BitConverter.Int64BitsToDouble(bits); }
        Array.Sort(numbers);
        foreach (double number in numbers)
        {
            values.Add(number.ToString("G", CultureInfo.InvariantCulture));
        }
    }

    private void AddDistinctDates(List<string> values)
    {
        if (DistinctDates is null)
        {
            return;
        }

        long[] ticks = [.. DistinctDates];
        Array.Sort(ticks);
        foreach (long t in ticks)
        {
            var date = new DateTime(t);
            values.Add(date.ToString(
                date.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-ddTHH:mm:ss",
                CultureInfo.InvariantCulture));
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
