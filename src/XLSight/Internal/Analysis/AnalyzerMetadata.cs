using XLSight.Analysis;

namespace XLSight.Internal.Analysis;

internal sealed class AnalyzerMetadata
{
    public required WorkbookAnalysisExact WorkbookExact { get; init; }

    public required IReadOnlyDictionary<string, SheetExactMetadata> SheetsByPath { get; init; }
}
