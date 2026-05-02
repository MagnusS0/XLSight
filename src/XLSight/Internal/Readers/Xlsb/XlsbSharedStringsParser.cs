namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbSharedStringsParser
{
    internal static XlsbSharedStringTable Parse(Stream? stream)
    {
        if (stream is null)
        {
            return XlsbSharedStringTable.Empty;
        }

        return new XlsbSharedStringTable(stream);
    }
}
