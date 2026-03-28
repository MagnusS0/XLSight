namespace XLSight.Internal.Metadata;

internal static class ExcelDateConverter
{
    /// <summary>
    /// Converts an Excel serial date number to a <see cref="DateTime"/>.
    /// Returns <see langword="null"/> for invalid serials (negative values or the phantom Feb 29, 1900).
    /// </summary>
    internal static DateTime? FromSerial(double serial, bool isDate1904)
    {
        if (serial < 0)
        {
            return null;
        }

        if (isDate1904)
        {
            return new DateTime(1904, 1, 1).AddDays(serial);
        }

        // 1900 date system
        if (serial == 0)
        {
            return DateTime.MinValue;
        }

        if (serial < 1)
        {
            return DateTime.MinValue.AddDays(serial);
        }

        if (serial < 60)
        {
            // serial 1 = Jan 1, 1900 … serial 59 = Feb 28, 1900
            return new DateTime(1899, 12, 31).AddDays(serial);
        }

        if (serial < 61)
        {
            // Phantom "Feb 29, 1900" — this date does not exist
            return null;
        }

        // serial >= 61: offset by one to skip the phantom leap day
        // 1899-12-30 + 61 = Mar 1, 1900 ✓
        return new DateTime(1899, 12, 30).AddDays(serial);
    }
}
