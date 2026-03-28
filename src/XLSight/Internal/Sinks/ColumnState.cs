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

    // Distinct tracking: shared strings use integer SST index (zero alloc);
    // inline strings and other scalar types fall back to string representation.
    internal HashSet<int>?    DistinctSstIds     = new();
    internal HashSet<string>? DistinctOtherValues = new(StringComparer.Ordinal);
    internal int DistinctEstimate;

    /// <summary>Combined distinct count across SST IDs and other values, or the capped estimate.</summary>
    internal int DistinctCount =>
        DistinctSstIds is not null || DistinctOtherValues is not null
            ? (DistinctSstIds?.Count ?? 0) + (DistinctOtherValues?.Count ?? 0)
            : DistinctEstimate;

    private const int DistinctCap = 1000;

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
                DistinctEstimate = DistinctSstIds.Count;
                DistinctSstIds = null;
                DistinctOtherValues = null;
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
        TrackDistinctOther(value);

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
                break;

            case Models.CellType.Date:
                DateCount++;
                break;

            case Models.CellType.Text:
                TextCount++;
                int len = value.AsText().Length;
                if (len > MaxTextLength) { MaxTextLength = len; }
                break;

            case Models.CellType.Boolean:
                BooleanCount++;
                break;

            case Models.CellType.Error:
                ErrorCount++;
                break;

            case Models.CellType.Formula:
                HasFormulas = true;
                break;
        }
    }

    private void TrackDistinctOther(Models.ExcelCellValue value)
    {
        if (DistinctOtherValues is null)
        {
            return;
        }

        DistinctOtherValues.Add(value.ToString());

        if (DistinctOtherValues.Count >= DistinctCap)
        {
            DistinctEstimate = DistinctOtherValues.Count;
            DistinctSstIds = null;
            DistinctOtherValues = null;
        }
    }
}
