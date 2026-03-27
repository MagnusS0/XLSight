using XLSight.SharedStrings;
using System.Globalization;
using XLSight.Models;
using XLSight.Styles;

namespace XLSight.Worksheets;

internal static class CellValueDecoder
{
    internal static ExcelCellValue Decode(
        in ParsedCell cell,
        SharedStringTable sharedStrings,
        StyleTable styles,
        bool isDate1904,
        ExcelReadMode mode)
    {
        if (mode == ExcelReadMode.Formulas && cell.FormulaText is not null)
        {
            return ExcelCellValue.FromFormula(cell.FormulaText);
        }

        return cell.DataKind switch
        {
            CellDataKind.SharedString  => DecodeSharedString(in cell, sharedStrings),
            CellDataKind.Boolean       => DecodeBoolean(in cell),
            CellDataKind.InlineString  => cell.InlineString is not null
                                            ? ExcelCellValue.FromText(cell.InlineString)
                                            : ExcelCellValue.Empty,
            CellDataKind.Error         => !string.IsNullOrEmpty(cell.RawValue)
                                            ? ExcelCellValue.FromError(cell.RawValue)
                                            : ExcelCellValue.Empty,
            CellDataKind.FormulaString => !string.IsNullOrEmpty(cell.RawValue)
                                            ? ExcelCellValue.FromText(cell.RawValue)
                                            : ExcelCellValue.Empty,
            _                          => DecodeNumber(in cell, styles, isDate1904),
        };
    }

    private static ExcelCellValue DecodeSharedString(in ParsedCell cell, SharedStringTable sharedStrings)
    {
        // Empty <v> with t="s" MUST return Empty, NOT sharedStrings[0] (calamine #607)
        if (string.IsNullOrEmpty(cell.RawValue))
        {
            return ExcelCellValue.Empty;
        }

        if (!int.TryParse(cell.RawValue, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            return ExcelCellValue.Empty;
        }

        // Unsigned cast handles negative index implicitly
        if ((uint)index >= (uint)sharedStrings.Count)
        {
            return ExcelCellValue.Empty;
        }

        return ExcelCellValue.FromText(sharedStrings.GetString(index));
    }

    private static ExcelCellValue DecodeBoolean(in ParsedCell cell)
    {
        if (string.IsNullOrEmpty(cell.RawValue))
        {
            return ExcelCellValue.Empty;
        }

        return ExcelCellValue.FromBoolean(cell.RawValue[0] == '1');
    }

    private static ExcelCellValue DecodeNumber(in ParsedCell cell, StyleTable styles, bool isDate1904)
    {
        if (string.IsNullOrEmpty(cell.RawValue))
        {
            return ExcelCellValue.Empty;
        }

        if (!double.TryParse(cell.RawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return ExcelCellValue.Empty;
        }

        var formatClass = styles.GetClassification(cell.StyleIndex);
        if (formatClass is FormatClass.Date or FormatClass.DateTime or FormatClass.Time)
        {
            var dt = ExcelDateConverter.FromSerial(value, isDate1904);
            if (dt is not null)
            {
                return ExcelCellValue.FromDate(dt.Value);
            }
        }

        return ExcelCellValue.FromNumber(value);
    }
}
