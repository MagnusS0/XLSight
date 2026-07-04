namespace XLSight.Internal.Sinks;

internal partial struct AnalysisSink
{
    private void AddLayoutCell(int column, ExcelCellValue value, bool isFormula)
    {
        LayoutKindMask mask = LayoutKindMask.None;
        double numericValue = 0;
        bool hasNumericValue = false;
        bool isHeaderLike = false;
        string? sampleText = null;

        switch (value.CellType)
        {
            case CellType.Text:
                string text = value.AsText().Trim();
                mask |= LayoutKindMask.Text;
                isHeaderLike = IsHeaderLikeText(text);
                sampleText = CapSampleText(text.Length <= 64 ? text : text[..64]);
                break;

            case CellType.Number:
                numericValue = value.AsNumber();
                hasNumericValue = true;
                mask |= LayoutKindMask.Number;
                // Only year-like numbers anchor headers; a ratio (|v|<1) is data, not a header.
                if (IsYearLikeNumber(numericValue))
                {
                    mask |= LayoutKindMask.YearLikeNumber;
                    isHeaderLike = true;
                }
                break;

            case CellType.Date:
                numericValue = value.AsDate().ToOADate();
                hasNumericValue = true;
                mask |= LayoutKindMask.Date;
                isHeaderLike = true;
                break;

            case CellType.Boolean:
                mask |= LayoutKindMask.Boolean;
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

        _layoutCells.Add(new LayoutCellFact(
            _currentRow,
            column,
            mask,
            numericValue,
            hasNumericValue,
            isHeaderLike,
            sampleText));
    }

    // Sample text is diagnostics-only; the budget keeps a high-cardinality text sheet from
    // pinning millions of distinct strings for the fact store's lifetime. Axes discovered
    // past the budget lose text samples but keep range, kind, and coverage.
    private string? CapSampleText(string sampleText)
    {
        if (_layoutTextSamplesRemaining == 0)
        {
            return null;
        }

        _layoutTextSamplesRemaining--;
        return sampleText;
    }

    // Deliberately permissive: any short text can label a row or column, so length is the only
    // filter. The claimed-field pruning in SheetLayoutInference keeps this cheap.
    private static bool IsHeaderLikeText(string text) => text.Length is > 0 and <= 40;

    private static bool IsYearLikeNumber(double value) => value % 1 == 0 && value is >= 1900 and <= 2100;
}
