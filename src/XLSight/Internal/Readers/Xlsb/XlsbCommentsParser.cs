namespace XLSight.Internal.Readers.Xlsb;

internal static class XlsbCommentsParser
{
    internal static int Count(Stream commentsStream)
    {
        ArgumentNullException.ThrowIfNull(commentsStream);

        try
        {
            int count = 0;
            using var iterator = new XlsbRecordIterator(commentsStream);
            while (iterator.TryRead(out XlsbRecord record))
            {
                if (record.Type == XlsbRecordType.BrtBeginComment) { count++; }
            }

            return count;
        }
        catch (Exception ex) when (ex is MalformedWorkbookException or IOException or InvalidDataException or ArgumentException or ArithmeticException)
        {
            return 0;
        }
    }
}
