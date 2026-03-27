using System.Text;
using SharpFuzz;
using XLSight.ByteEngine;
using XLSight.Models;
using XLSight.Models.Analysis;
using XLSight.SharedStrings;
using XLSight.Styles;
using XLSight.Worksheets;

namespace XLSight.Fuzz;

internal static class Program
{
    private const int MaxInputBytes = 2 * 1024 * 1024;
    private const int MaxRowsToInspect = 256;

    private static readonly SharedStringTable SharedStrings = SharedStringTable.Empty;

    public static void Main(string[] args)
    {
        Fuzzer.OutOfProcess.Run(stream =>
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);

            var data = ms.ToArray();
            if (data.Length == 0 || data.Length > MaxInputBytes)
            {
                return;
            }

            bool rowsOk = TryScanWithRows(data, out var rows);
            bool sinkOk = TryScanWithSink(data);

            if (rowsOk && !sinkOk)
            {
                throw new InvalidOperationException("ByteEngine sink scanner rejected an input accepted by row scanner.");
            }

            if (rowsOk && sinkOk)
            {
                _ = rows.Sum(static r => r.CellCount);
            }
        });
    }

    private static bool TryScanWithRows(byte[] data, out IReadOnlyList<ExcelRow> rows)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            rows = DrainRows(XlsxSheetScanner.ScanRows(
                stream,
                SharedStrings,
                StyleTable.Default,
                isDate1904: false,
                ExcelReadMode.Values,
                ExcelRange.Unbounded));
            return true;
        }
        catch (Exception ex) when (IsExpectedInputException(ex))
        {
            rows = [];
            return false;
        }
    }

    private static bool TryScanWithSink(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            var sink = new NoopSink();
            XlsxSheetScanner.ScanSheet(
                stream,
                SharedStrings,
                StyleTable.Default,
                isDate1904: false,
                ExcelRange.Unbounded,
                ref sink);
            return true;
        }
        catch (Exception ex) when (IsExpectedInputException(ex))
        {
            return false;
        }
    }

    private static List<ExcelRow> DrainRows(IEnumerable<ExcelRow> source)
    {
        var rows = new List<ExcelRow>(Math.Min(32, MaxRowsToInspect));
        foreach (var row in source)
        {
            rows.Add(row);
            if (rows.Count >= MaxRowsToInspect)
            {
                break;
            }
        }

        return rows;
    }

    private static bool IsExpectedInputException(Exception ex) =>
        ex is InvalidDataException
            or FormatException
            or OverflowException
            or ArgumentException
            or DecoderFallbackException;

    private struct NoopSink : IByteSheetSink
    {
        public void OnDimension(in ExcelRange dimension) { }
        public void OnRowStart(int rowIndex) { }
        public bool OnCell(int column, CellDataKind kind, int styleIdx, ExcelCellValue value) => true;
        public void OnMergeCell(in ExcelMergedRegion region) { }
        public void OnEnd() { }
    }
}
