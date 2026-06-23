using System.Runtime.InteropServices;

namespace XLSight.Query.Internal;

/// <summary>
/// Mutable per-group accumulator for one <see cref="AggregateSpec"/>.
/// Sum/Average accept numeric cells; Min/Max accept numeric or date cells but latch the
/// first-seen kind and reject the other afterwards.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct AggregateAccumulator
{
    public double Sum;
    public long Count;
    public double Min;     // numeric value, or DateTime ticks when ValueKind is Date
    public double Max;
    public bool HasValue;
    public CellType ValueKind;

    /// <summary>Folds a non-empty cell into the accumulator. Returns false when the cell is unaggregatable.</summary>
    public bool TryAccumulate(AggregateKind kind, in ExcelCellValue cell)
    {
        if (kind is AggregateKind.Sum or AggregateKind.Average)
        {
            if (!cell.TryGetNumber(out double number))
            {
                return false;
            }

            Sum += number;
            Count++;
            HasValue = true;
            return true;
        }

        // Min / Max
        double value;
        CellType valueKind;
        if (cell.TryGetNumber(out double n))
        {
            value = n;
            valueKind = CellType.Number;
        }
        else if (cell.TryGetDate(out DateTime date))
        {
            value = date.Ticks;
            valueKind = CellType.Date;
        }
        else
        {
            return false;
        }

        if (!HasValue)
        {
            Min = value;
            Max = value;
            ValueKind = valueKind;
            HasValue = true;
            return true;
        }

        if (valueKind != ValueKind)
        {
            return false;
        }

        if (value < Min) { Min = value; }
        if (value > Max) { Max = value; }
        return true;
    }

    /// <summary>Materializes the final value. Empty when no cell was accepted (except Count, which is 0).</summary>
    public readonly ExcelCellValue Result(AggregateKind kind) => kind switch
    {
        AggregateKind.Count => ExcelCellValue.FromNumber(Count),
        _ when !HasValue => ExcelCellValue.Empty,
        AggregateKind.Sum => ExcelCellValue.FromNumber(Sum),
        AggregateKind.Average => ExcelCellValue.FromNumber(Sum / Count),
        AggregateKind.Min => ToCell(Min),
        AggregateKind.Max => ToCell(Max),
        _ => ExcelCellValue.Empty,
    };

    private readonly ExcelCellValue ToCell(double value) =>
        ValueKind == CellType.Date
            ? ExcelCellValue.FromDate(new DateTime((long)value))
            : ExcelCellValue.FromNumber(value);
}
