namespace XLSight;

/// <summary>Identifies the Excel workbook container format.</summary>
public enum WorkbookFormat : byte
{
    /// <summary>Open XML workbook (.xlsx).</summary>
    Xlsx = 0,

    /// <summary>Macro-enabled Open XML workbook (.xlsm).</summary>
    Xlsm = 1,

    /// <summary>Binary workbook (.xlsb).</summary>
    Xlsb = 2,
}
