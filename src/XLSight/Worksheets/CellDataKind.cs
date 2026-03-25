namespace XLSight.Worksheets;

internal enum CellDataKind : byte
{
    Number = 0,        // default — no t attribute, or t="n"
    SharedString = 1,  // t="s"
    Boolean = 2,       // t="b"
    InlineString = 3,  // t="inlineStr"
    FormulaString = 4, // t="str"
    Error = 5,         // t="e"
}
