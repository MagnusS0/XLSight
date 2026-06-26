namespace XLSight.Analysis;

/// <summary>Coarse structural categories inferred for worksheet regions.</summary>
public enum RegionKind : byte
{
    Unknown = 0,
    HeaderBand = 1,
    DataTable = 2,
    ParameterBlock = 3,
    SummaryBlock = 4,
    Crosstab = 5,
    Transposed = 6,
    TitleRow = 7,
}
