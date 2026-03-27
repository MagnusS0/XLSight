namespace XLSight.SharedStrings;

internal static class SharedStringsParser
{
    internal static SharedStringTable Parse(Stream? sstStream)
    {
        if (sstStream is null) { return SharedStringTable.Empty; }
        return SharedStringsByteParser.Parse(sstStream);
    }
}
