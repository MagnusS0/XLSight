namespace XLSight.Analysis.Layout.Internal;

[Flags]
internal enum LayoutKindMask : ushort
{
    None = 0,
    Text = 1,
    Number = 2,
    Date = 4,
    Boolean = 8,
    Formula = 16,
    YearLikeNumber = 32,
}
