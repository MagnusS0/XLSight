using System.Xml;

namespace XLSight.Worksheets;

internal static class XlsxReaderSettings
{
    internal static XmlReaderSettings Create(XmlNameTable nameTable) => new()
    {
        IgnoreComments = true,
        IgnoreWhitespace = true,
        IgnoreProcessingInstructions = true,
        DtdProcessing = DtdProcessing.Prohibit,
        NameTable = nameTable,
    };
}
