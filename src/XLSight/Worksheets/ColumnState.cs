namespace XLSight.Worksheets;

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
    internal HashSet<string>? DistinctValues = new(StringComparer.Ordinal);
    internal int DistinctEstimate;

    private const int DistinctCap = 1000;

    internal void RecordValue(Models.ExcelCellValue value)
    {
        if (value.IsEmpty)
        {
            return;
        }

        NonEmptyCount++;
        TrackDistinct(value);

        switch (value.CellType)
        {
            case Models.ExcelCellType.Number:
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
                    if (num < MinNumeric)
                    {
                        MinNumeric = num;
                    }

                    if (num > MaxNumeric)
                    {
                        MaxNumeric = num;
                    }
                }
                break;

            case Models.ExcelCellType.Date:
                DateCount++;
                break;

            case Models.ExcelCellType.Text:
                TextCount++;
                int len = value.AsText().Length;
                if (len > MaxTextLength)
                {
                    MaxTextLength = len;
                }
                break;

            case Models.ExcelCellType.Boolean:
                BooleanCount++;
                break;

            case Models.ExcelCellType.Error:
                ErrorCount++;
                break;

            case Models.ExcelCellType.Formula:
                HasFormulas = true;
                break;
        }
    }

    private void TrackDistinct(Models.ExcelCellValue value)
    {
        if (DistinctValues is null)
        {
            return;
        }

        string key = value.ToString();
        DistinctValues.Add(key);

        if (DistinctValues.Count >= DistinctCap)
        {
            DistinctEstimate = DistinctValues.Count;
            DistinctValues = null;
        }
    }
}
