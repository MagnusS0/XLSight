using System.Xml;

namespace XLSight.Worksheets;

internal sealed class XlsxNameTable
{
    internal XmlNameTable Table { get; }

    // workbook.xml elements
    internal readonly string Workbook;
    internal readonly string WorkbookPr;
    internal readonly string Sheets;
    internal readonly string Sheet;
    internal readonly string DefinedNames;
    internal readonly string DefinedName;

    // workbook.xml attributes
    internal readonly string Name;
    internal readonly string SheetId;
    internal readonly string State;
    internal readonly string Hidden;
    internal readonly string RefId;   // r:id attribute on <sheet>
    internal readonly string Date1904;

    // sharedStrings.xml elements
    internal readonly string Sst;
    internal readonly string Si;
    internal readonly string T;   // <t> element and t= attribute on <c> share this atom
    internal readonly string R;   // <r> rich-text run; also r= row/cell-ref attribute

    // styles.xml elements
    internal readonly string StyleSheet;
    internal readonly string NumFmts;
    internal readonly string NumFmt;
    internal readonly string CellXfs;
    internal readonly string Xf;

    // styles.xml attributes
    internal readonly string NumFmtId;
    internal readonly string FormatCode;
    internal readonly string Count;
    internal readonly string UniqueCount;

    // worksheet.xml elements
    internal readonly string Worksheet;
    internal readonly string SheetData;
    internal readonly string Row;
    internal readonly string C;
    internal readonly string V;
    internal readonly string F;
    internal readonly string Is;

    // worksheet.xml merge/dimension elements and attributes
    internal readonly string Dimension;
    internal readonly string MergeCell;
    internal readonly string MergeCells;
    internal readonly string Ref;

    // worksheet.xml attributes
    internal readonly string S;      // s= style index on <c>
    internal readonly string Spans;

    // CellRef/RowRef both alias R ("r") — no separate field needed.
    // CellType aliases T ("t") — the same atom serves both the <t> element and t= attribute.

    internal XlsxNameTable()
    {
        var nt = new NameTable();
        Table = nt;

        Workbook = nt.Add("workbook");
        WorkbookPr = nt.Add("workbookPr");
        Sheets = nt.Add("sheets");
        Sheet = nt.Add("sheet");
        DefinedNames = nt.Add("definedNames");
        DefinedName = nt.Add("definedName");

        Name = nt.Add("name");
        SheetId = nt.Add("sheetId");
        State = nt.Add("state");
        Hidden = nt.Add("hidden");
        RefId = nt.Add("id");
        Date1904 = nt.Add("date1904");

        Sst = nt.Add("sst");
        Si = nt.Add("si");
        T = nt.Add("t");
        R = nt.Add("r");

        StyleSheet = nt.Add("styleSheet");
        NumFmts = nt.Add("numFmts");
        NumFmt = nt.Add("numFmt");
        CellXfs = nt.Add("cellXfs");
        Xf = nt.Add("xf");

        NumFmtId = nt.Add("numFmtId");
        FormatCode = nt.Add("formatCode");
        Count = nt.Add("count");
        UniqueCount = nt.Add("uniqueCount");

        Worksheet = nt.Add("worksheet");
        SheetData = nt.Add("sheetData");
        Row = nt.Add("row");
        C = nt.Add("c");
        V = nt.Add("v");
        F = nt.Add("f");
        Is = nt.Add("is");

        Dimension = nt.Add("dimension");
        MergeCell = nt.Add("mergeCell");
        MergeCells = nt.Add("mergeCells");
        Ref = nt.Add("ref");

        S = nt.Add("s");
        Spans = nt.Add("spans");
    }
}
