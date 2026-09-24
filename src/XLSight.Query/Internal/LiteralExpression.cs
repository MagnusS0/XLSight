namespace XLSight.Query.Internal;

internal sealed record LiteralExpression(ExcelCellValue Value) : QueryExpression;
