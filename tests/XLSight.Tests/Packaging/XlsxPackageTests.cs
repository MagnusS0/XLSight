using System.IO.Compression;
using System.Text;
using Xunit;
using XLSight.Infrastructure;
using XLSight.Packaging;

namespace XLSight.Tests.Packaging;

public sealed class XlsxPackageTests
{
    [Fact]
    public void Open_EnumeratesEntriesFromWorkbook()
    {
        using var stream = CreatePackage(("xl/workbook.xml", "<workbook/>"), ("[Content_Types].xml", "<Types/>"));
        using XlsxPackage package = XlsxPackage.Open(stream);

        string[] entries = [.. package.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal)];

        Assert.Equal(["[Content_Types].xml", "xl/workbook.xml"], entries);
    }

    [Fact]
    public void GetEntry_NormalizesBackslashPaths()
    {
        using var stream = CreatePackage(("xl\\workbook.xml", "<workbook/>"));
        using XlsxPackage package = XlsxPackage.Open(stream);

        ZipArchiveEntry? entry = package.GetEntry("xl/workbook.xml");

        Assert.NotNull(entry);
        Assert.Equal("xl\\workbook.xml", entry.FullName);
    }

    [Fact]
    public void GetEntry_FallsBackToCaseInsensitiveLookup()
    {
        using var stream = CreatePackage(("XL/WORKBOOK.XML", "<workbook/>"));
        using XlsxPackage package = XlsxPackage.Open(stream);

        ZipArchiveEntry? entry = package.GetEntry("xl/workbook.xml");

        Assert.NotNull(entry);
        Assert.Equal("XL/WORKBOOK.XML", entry.FullName);
    }

    [Fact]
    public async Task OpenAsync_EnumeratesEntriesFromWorkbook()
    {
        using var stream = CreatePackage(("xl/workbook.xml", "<workbook/>"), ("[Content_Types].xml", "<Types/>"));

        await using XlsxPackage package = await XlsxPackage.OpenAsync(stream, TestContext.Current.CancellationToken);
        string[] entries = [.. package.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal)];

        Assert.Equal(["[Content_Types].xml", "xl/workbook.xml"], entries);
    }

    [Fact]
    public void PathNormalizer_ReplacesBackslashes()
    {
        Assert.Equal("xl/workbook.xml", PathNormalizer.Normalize("xl\\workbook.xml"));
    }

    private static MemoryStream CreatePackage(params (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using Stream entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, Encoding.UTF8, leaveOpen: true);
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
