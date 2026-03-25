namespace XLSight.Styles;

/// <summary>
/// Classification of an Excel number format for value decoding purposes.
/// </summary>
internal enum FormatClass : byte
{
    General = 0,
    Number = 1,
    Date = 2,
    Time = 3,
    DateTime = 4,
    Text = 5,
}
