using XLSight.Internal.Metadata;
using XLSight.Internal.Readers.Xlsx;
using Xunit;

namespace XLSight.Tests.Readers.Xlsx;

public sealed class FuzzCorpusRegressionTests
{
    private const int MaxRowsToInspect = 256;
    private static readonly SharedStringTable SharedStrings = SharedStringTable.Empty;

    [Fact]
    public void FuzzCorpus_ByteEngine_ParsesWithoutUnexpectedCrashes()
    {
        string corpusDir = Path.Combine(AppContext.BaseDirectory, "FuzzCorpus");
        if (!Directory.Exists(corpusDir))
        {
            return;
        }

        string[] files = Directory.GetFiles(corpusDir, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            byte[] data = File.ReadAllBytes(file);
            if (data.Length == 0)
            {
                continue;
            }

            TryScanWithByte(data, file);
        }
    }

    private static void TryScanWithByte(byte[] data, string file)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var cursor = XlsxSheetScanner.OpenCursor(
                stream, SharedStrings, StyleTable.Default, isDate1904: false,
                ReadMode.Values, ExcelRange.Unbounded);
            int rows = 0;
            while (rows < MaxRowsToInspect && cursor.MoveNext())
            {
                rows++;
            }
        }
        catch (Exception ex) when (IsExpectedInputException(ex))
        {
            // Expected failure for malformed input — not a bug.
        }
    }

    private static bool IsExpectedInputException(Exception ex) =>
        ex is InvalidDataException
            or FormatException
            or OverflowException
            or ArgumentException
            or System.Text.DecoderFallbackException;
}
