namespace XLSight.Query.Internal;

/// <summary>An unresolved filter as recorded by the builder: column name, operator, typed literal.</summary>
internal readonly record struct FilterSpec(string Column, QueryOp Op, ExcelCellValue Literal);
