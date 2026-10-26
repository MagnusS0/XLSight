using System.Runtime.CompilerServices;

namespace XLSight.FastBench;

/// <summary>
/// Order-sensitive 64-bit digest of the useful work an operation produced. Timed operations use
/// the cheap form (types, counts, lengths, numeric bits); the untimed validation pass also folds
/// in full text content so decoding changes cannot hide behind equal lengths.
/// </summary>
internal struct Digest
{
    private const ulong Prime = 0x100000001B3UL;
    private ulong _hash;

    public Digest() => _hash = 0xCBF29CE484222325UL;

    public readonly ulong Value => _hash;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(ulong value) => _hash = (_hash ^ value) * Prime;

    public void Add(long value) => Add(unchecked((ulong)value));

    public void Add(int value) => Add(unchecked((ulong)(uint)value));

    public void Add(double value) => Add(BitConverter.DoubleToUInt64Bits(value));

    public void Add(bool value) => Add(value ? 1UL : 0UL);

    public void AddText(string? value, bool deep)
    {
        if (value is null)
        {
            Add(ulong.MaxValue);
            return;
        }

        Add(value.Length);
        if (deep)
        {
            foreach (char c in value)
            {
                Add((ulong)c);
            }
        }
    }

    public void AddCell(in ExcelCellValue cell, bool deep)
    {
        Add((ulong)cell.CellType);
        switch (cell.CellType)
        {
            case CellType.Number:
                cell.TryGetNumber(out double number);
                Add(number);
                break;
            case CellType.Date:
                cell.TryGetDate(out DateTime date);
                Add(date.Ticks);
                break;
            case CellType.Boolean:
                cell.TryGetBoolean(out bool boolean);
                Add(boolean);
                break;
            case CellType.Text:
                cell.TryGetText(out string? text);
                AddText(text, deep);
                break;
            case CellType.Error:
                AddText(cell.AsError(), deep);
                break;
            case CellType.Formula:
                AddText(cell.AsFormula(), deep);
                break;
        }
    }

    public static string Hex(ulong value) => value.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
}
