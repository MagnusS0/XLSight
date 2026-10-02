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

    // Distinct tracking — typed per-kind to avoid string allocations.
    // SST: integer index (zero-alloc read); Numbers: double bits; Dates: double bits;
    // Booleans: two-bit flags; Errors: int code; Inline strings: string (unavoidable).
    // Each set is allocated on first use, and all sets are nulled out once the combined
    // count hits DistinctCap and _distinctEstimate is latched (_capped distinguishes
    // "never used" from "capped" so tracking stops permanently after the cap).
    private HashSet<int>? _distinctSstIds;
    private HashSet<long>? _distinctNumbers;  // BitConverter.DoubleToInt64Bits
    private HashSet<long>? _distinctDates;    // BitConverter.DoubleToInt64Bits
    private HashSet<string>? _distinctInlineStrings;
    private byte _booleanSeen;   // bit 0 = false seen, bit 1 = true seen
    private int _distinctEstimate;
    private bool _capped;

    private const int DistinctCap = 1000;

    /// <summary>Combined distinct count across all typed sets, or the capped estimate.</summary>
    internal int DistinctCount
    {
        get
        {
            if (_distinctEstimate > 0)
            {
                return _distinctEstimate;
            }

            int count = 0;
            if (_distinctSstIds is not null) { count += _distinctSstIds.Count; }
            if (_distinctNumbers is not null) { count += _distinctNumbers.Count; }
            if (_distinctDates is not null) { count += _distinctDates.Count; }
            if (_distinctInlineStrings is not null) { count += _distinctInlineStrings.Count; }
            count += BooleanCount > 0 ? System.Numerics.BitOperations.PopCount(_booleanSeen) : 0;
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
            (_distinctSstIds ??= []).Add(sstIndex);
            if (_distinctSstIds.Count >= DistinctCap)
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

                TrackDistinctLong(ref _distinctNumbers, System.Runtime.CompilerServices.Unsafe.BitCast<double, long>(num));
                break;

            case CellType.Date:
                DateCount++;
                TrackDistinctLong(ref _distinctDates, value.AsDate().Ticks);
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
                _booleanSeen |= value.AsBoolean() ? (byte)2 : (byte)1;
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
        (_distinctInlineStrings ??= new(StringComparer.Ordinal)).Add(value);
        if (_distinctInlineStrings.Count >= DistinctCap)
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
            if ((_booleanSeen & 1) != 0) { values.Add("FALSE"); }
            if ((_booleanSeen & 2) != 0) { values.Add("TRUE"); }
        }

        return [.. values];
    }

    private void AddDistinctTexts(List<string> values, ISharedStringSource sst)
    {
        if (_distinctSstIds is null && _distinctInlineStrings is null)
        {
            return;
        }

        // SST-resolved and inline copies of the same text must not appear twice.
        var texts = new SortedSet<string>(StringComparer.Ordinal);
        if (_distinctSstIds is not null)
        {
            foreach (int id in _distinctSstIds) { texts.Add(sst.GetString(id)); }
        }

        if (_distinctInlineStrings is not null)
        {
            foreach (string text in _distinctInlineStrings) { texts.Add(text); }
        }

        values.AddRange(texts);
    }

    private void AddDistinctNumbers(List<string> values)
    {
        if (_distinctNumbers is null)
        {
            return;
        }

        var numbers = new double[_distinctNumbers.Count];
        int i = 0;
        foreach (long bits in _distinctNumbers) { numbers[i++] = BitConverter.Int64BitsToDouble(bits); }
        Array.Sort(numbers);
        foreach (double number in numbers)
        {
            values.Add(number.ToString("G", CultureInfo.InvariantCulture));
        }
    }

    private void AddDistinctDates(List<string> values)
    {
        if (_distinctDates is null)
        {
            return;
        }

        long[] ticks = [.. _distinctDates];
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
        _distinctEstimate = DistinctCount;
        _capped = true;
        _distinctSstIds = null;
        _distinctNumbers = null;
        _distinctDates = null;
        _distinctInlineStrings = null;
    }
}
