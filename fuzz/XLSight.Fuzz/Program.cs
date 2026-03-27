using System.Text;
using System.Xml;
using SharpFuzz;
using XLSight.ByteEngine;
using XLSight.Models;
using XLSight.SharedStrings;
using XLSight.Styles;
using XLSight.Worksheets;

namespace XLSight.Fuzz;

internal static class Program
{
    private const int MaxInputBytes = 2 * 1024 * 1024;
    private const int MaxRowsToInspect = 256;

    private static readonly XlsxNameTable Names = new();
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

            bool xmlOk = TryScanWithXml(data, out var xmlRows);
            bool byteOk = TryScanWithByte(data, out var byteRows);

            if (xmlOk && !byteOk)
            {
                throw new InvalidOperationException("Byte engine rejected an input accepted by XmlReader scanner.");
            }

            if (xmlOk && byteOk)
            {
                AssertParity(xmlRows, byteRows);
            }
        });
    }

    private static bool TryScanWithXml(byte[] data, out IReadOnlyList<ExcelRow> rows)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            rows = DrainRows(WorksheetScanner.ScanRows(
                stream,
                Names,
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

    private static bool TryScanWithByte(byte[] data, out IReadOnlyList<ExcelRow> rows)
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
        ex is XmlException
            or InvalidDataException
            or FormatException
            or OverflowException
            or ArgumentException
            or DecoderFallbackException;

    private static void AssertParity(IReadOnlyList<ExcelRow> xmlRows, IReadOnlyList<ExcelRow> byteRows)
    {
        if (xmlRows.Count != byteRows.Count)
        {
            throw new InvalidOperationException($"Row count mismatch. xml={xmlRows.Count}, byte={byteRows.Count}");
        }

        for (int i = 0; i < xmlRows.Count; i++)
        {
            var a = xmlRows[i];
            var b = byteRows[i];
            if (a.RowIndex != b.RowIndex || a.StartColumn != b.StartColumn || a.CellCount != b.CellCount)
            {
                throw new InvalidOperationException($"Row shape mismatch at index {i}.");
            }

            for (int c = 0; c < a.CellCount; c++)
            {
                if (a.Cells[c] != b.Cells[c])
                {
                    throw new InvalidOperationException($"Cell mismatch at row={a.RowIndex}, offset={c}.");
                }
            }
        }
    }
}
