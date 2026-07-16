using System.Runtime.InteropServices;
using XLSight.Internal.Scanning;

namespace XLSight.Analysis.Layout.Internal;

[StructLayout(LayoutKind.Auto)]
internal readonly struct LayoutScanSink : IWorksheetScanSink
{
    private readonly LayoutCellStore _cells = new();

    public LayoutScanSink() { }

    public LayoutCellStore Cells => _cells;

    public void OnCell(int row, int column, in ExcelCellValue value, bool isFormula)
    {
        if (value.IsEmpty)
        {
            return;
        }

        LayoutKindMask mask = LayoutKindMask.None;
        double numericValue = 0;
        bool hasNumericValue = false;
        bool isHeaderLike = false;
        string? text = null;

        switch (value.CellType)
        {
            case CellType.Text:
                text = CapText(value.AsText().Trim());
                mask = LayoutKindMask.Text;
                isHeaderLike = IsHeaderLikeText(text);
                break;

            case CellType.Number:
                numericValue = value.AsNumber();
                hasNumericValue = true;
                mask = LayoutKindMask.Number;
                if (IsYearLikeNumber(numericValue))
                {
                    mask |= LayoutKindMask.YearLikeNumber;
                    isHeaderLike = true;
                }
                break;

            case CellType.Date:
                numericValue = value.AsDate().ToOADate();
                hasNumericValue = true;
                mask = LayoutKindMask.Date;
                isHeaderLike = true;
                break;

            case CellType.Boolean:
                mask = LayoutKindMask.Boolean;
                break;
        }

        if (isFormula)
        {
            mask |= LayoutKindMask.Formula;
        }

        if (mask == LayoutKindMask.None)
        {
            return;
        }

        AddFact(row, column, mask, numericValue, hasNumericValue, isHeaderLike, text);
    }

    private void AddFact(
        int row,
        int column,
        LayoutKindMask mask,
        double numericValue,
        bool hasNumericValue,
        bool isHeaderLike,
        string? text) =>
        _cells.Add(new LayoutCellFact(
            row,
            column,
            mask,
            numericValue,
            hasNumericValue,
            isHeaderLike,
            text));

    private static string CapText(string text)
    {
        if (text.Length <= 64)
        {
            return text;
        }

        int cutoff = char.IsHighSurrogate(text[63]) ? 63 : 64;
        return text[..cutoff];
    }

    private static bool IsHeaderLikeText(string text) => text.Length is > 0 and <= 40;

    private static bool IsYearLikeNumber(double value) => value % 1 == 0 && value is >= 1900 and <= 2100;
}
