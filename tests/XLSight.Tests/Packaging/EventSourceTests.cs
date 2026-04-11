using System.Diagnostics.Tracing;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace XLSight.Tests.Packaging;

public sealed class EventSourceTests
{
    private const string WorkbookXml = """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Sheet1" sheetId="1" r:id="rId1" />
          </sheets>
        </workbook>
        """;

    private const string RelsXml = """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Target="worksheets/sheet1.xml" />
        </Relationships>
        """;

    private const string StylesXml = """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <cellXfs>
            <xf numFmtId="0" />
          </cellXfs>
        </styleSheet>
        """;

    private const string SheetXml = """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <dimension ref="A1:B2" />
          <sheetData>
            <row r="1">
              <c r="A1"><v>42</v></c>
              <c r="B1"><v>7</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    [Fact]
    public async Task AsyncApis_EmitMatchingEventSourceEvents()
    {
        using var listener = new TestEventListener();
        using var ms = CreateWorkbook();
        await using var workbook = await ExcelWorkbook.OpenAsync(ms, TestContext.Current.CancellationToken);

        _ = await workbook.ReadRangeAsync("Sheet1", "A1:B2", ct: TestContext.Current.CancellationToken);
        _ = await workbook.AnalyzeSheetAsync("Sheet1", ct: TestContext.Current.CancellationToken);

        var eventNames = listener.GetSnapshot();

        Assert.Contains("ReadRangeStart", eventNames);
        Assert.Contains("ReadRangeStop", eventNames);
        Assert.Contains("AnalyzeSheetStart", eventNames);
        Assert.Contains("AnalyzeSheetStop", eventNames);
    }

    private static MemoryStream CreateWorkbook()
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", WorkbookXml);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", RelsXml);
            WriteEntry(archive, "xl/styles.xml", StylesXml);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", SheetXml);
        }

        ms.Position = 0;
        return ms;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8, leaveOpen: true);
        writer.Write(content);
    }

    private sealed class TestEventListener : EventListener
    {
        private readonly Lock _gate = new();
        private readonly List<string> _eventNames = [];

        public string[] GetSnapshot()
        {
            lock (_gate)
            {
                return [.. _eventNames];
            }
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (string.Equals(eventSource.Name, "XLSight", StringComparison.Ordinal))
            {
                EnableEvents(eventSource, EventLevel.Verbose);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName is { } name)
            {
                lock (_gate)
                {
                    _eventNames.Add(name);
                }
            }
        }
    }
}
