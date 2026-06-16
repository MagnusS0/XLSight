using System.IO.Compression;
using System.Text;
using XLSight.Internal.Packaging;
using Xunit;

namespace XLSight.Tests.Packaging;

/// <summary>
/// Tests for OwnedEntryStream through XlsxPackage,
/// exercised through <see cref="XlsxPackage.TryOpenFreshEntry"/> which requires
/// a file-backed (FileStream) package.
/// </summary>
public sealed class OwnedEntryStreamTests : IDisposable
{
    private readonly string _tempFile;

    public OwnedEntryStreamTests()
    {
        // Write a small xlsx-shaped zip to a temp file in the test output dir
        _tempFile = Path.Combine(AppContext.BaseDirectory, $"TestData_Temp_{Guid.NewGuid():N}.xlsx");
        CreateSmallPackage(_tempFile);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    private static void CreateSmallPackage(string path)
    {
        using var fs = File.Create(path);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);
        ZipArchiveEntry entry = archive.CreateEntry("xl/workbook.xml");
        using Stream entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8, leaveOpen: true);
        writer.Write("<workbook/>");
    }

    private XlsxPackage OpenFileBacked()
    {
        var fs = new FileStream(_tempFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        return XlsxPackage.Open(fs, ownsStream: true);
    }

    // ── IsFileBacked ──────────────────────────────────────────────────────────

    [Fact]
    public void IsFileBacked_WhenOpenedFromFileStream_IsTrue()
    {
        using var package = OpenFileBacked();
        Assert.True(package.IsFileBacked);
    }

    // ── TryOpenFreshEntry — OwnedEntryStream.CanRead / CanSeek / CanWrite ────

    [Fact]
    public void TryOpenFreshEntry_CanRead_IsTrue()
    {
        using var package = OpenFileBacked();
        using Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);
        Assert.True(stream!.CanRead);
    }

    [Fact]
    public void TryOpenFreshEntry_CanSeek_IsFalse()
    {
        using var package = OpenFileBacked();
        using Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);
        // Zip entry contents go through DeflateStream which is non-seekable
        Assert.False(stream!.CanSeek);
    }

    [Fact]
    public void TryOpenFreshEntry_CanWrite_IsFalse()
    {
        using var package = OpenFileBacked();
        using Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);
        Assert.False(stream!.CanWrite);
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TryOpenFreshEntry_Read_ReturnsBytesFromEntry()
    {
        using var package = OpenFileBacked();
        using Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!, Encoding.UTF8);
        string content = reader.ReadToEnd();
        Assert.Equal("<workbook/>", content);
    }

    [Fact]
    public void TryOpenFreshEntry_ReadSpan_ReturnsBytesFromEntry()
    {
        using var package = OpenFileBacked();
        using Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);

        var buf = new byte[64];
        int read = stream!.Read(buf.AsSpan());
        Assert.True(read > 0);
    }

    [Fact]
    public void TryOpenFreshEntry_ReadByte_ReturnsByteFromEntry()
    {
        using var package = OpenFileBacked();
        using Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);

        Assert.NotEqual(-1, stream!.ReadByte());
    }


    [Fact]
    public void TryOpenFreshEntry_Length_Getter_IsInvoked()
    {
        using var package = OpenFileBacked();
        using Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);

        if (!stream!.CanSeek)
        {
            Assert.Throws<NotSupportedException>(() => _ = stream.Length);
        }
        else
        {
            _ = stream.Length;
        }
    }

    [Fact]
    public void TryOpenFreshEntry_Position_GetSet_AreInvoked()
    {
        using var package = OpenFileBacked();
        using Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);

        if (!stream!.CanSeek)
        {
            Assert.Throws<NotSupportedException>(() => _ = stream.Position);
            Assert.Throws<NotSupportedException>(() => stream.Position = 0);
        }
        else
        {
            long position = stream.Position;
            stream.Position = position;
        }
    }

    // ── Flush ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TryOpenFreshEntry_Flush_DoesNotThrow()
    {
        using var package = OpenFileBacked();
        using Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);
        stream!.Flush(); // must not throw
    }

    // ── Seek / SetLength / Write throw on non-seekable inner stream ───────────

    [Fact]
    public void TryOpenFreshEntry_Seek_ThrowsWhenNotSeekable()
    {
        using var package = OpenFileBacked();
        using Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);
        if (!stream!.CanSeek)
        {
            Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        }
    }

    [Fact]
    public void TryOpenFreshEntry_Write_ThrowsWhenNotWritable()
    {
        using var package = OpenFileBacked();
        using Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);
        if (!stream!.CanWrite)
        {
            Assert.Throws<NotSupportedException>(
                () => stream.Write(new byte[] { 1, 2, 3 }, 0, 3));
        }
    }

    [Fact]
    public void TryOpenFreshEntry_SetLength_ThrowsWhenNotSupported()
    {
        using var package = OpenFileBacked();
        using Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);
        if (!stream!.CanWrite)
        {
            Assert.ThrowsAny<NotSupportedException>(() => stream.SetLength(10));
        }
    }

    // ── ReadAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task TryOpenFreshEntry_ReadAsync_ReturnsBytesFromEntry()
    {
        using var package = OpenFileBacked();
        Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);
        await using (stream)
        {
            var buf = new byte[64];
            int read = await stream!.ReadAsync(buf.AsMemory(), TestContext.Current.CancellationToken);
            Assert.True(read > 0);
        }
    }

    [Fact]
    public async Task TryOpenFreshEntry_ReadAsyncMemory_ReturnsBytesFromEntry()
    {
        using var package = OpenFileBacked();
        Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);
        await using (stream)
        {
            var buf = new byte[64];
            int read = await stream!.ReadAsync(buf.AsMemory(), TestContext.Current.CancellationToken);
            Assert.True(read > 0);
        }
    }

    // ── Dispose closes the archive owner ─────────────────────────────────────

    [Fact]
    public void TryOpenFreshEntry_DisposeStream_ClosesOwnerArchive()
    {
        using var package = OpenFileBacked();
        Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);
        stream!.Dispose(); // should dispose both BufferedStream and ZipArchive owner
        // Attempting to read after dispose should throw
        Assert.Throws<ObjectDisposedException>(() => stream.Read(new byte[1], 0, 1));
    }

    // ── DisposeAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TryOpenFreshEntry_DisposeAsync_DisposesCorrectly()
    {
        using var package = OpenFileBacked();
        Stream? stream = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.NotNull(stream);
        await stream!.DisposeAsync();
        await stream.DisposeAsync();
        stream.Dispose();
    }

    // ── Entry not found returns null ──────────────────────────────────────────

    [Fact]
    public void TryOpenFreshEntry_MissingEntry_ReturnsNull()
    {
        using var package = OpenFileBacked();
        Stream? stream = package.TryOpenFreshEntry("xl/nonexistent.xml");
        Assert.Null(stream);
    }

    // ── Non-file-backed package returns null ─────────────────────────────────

    [Fact]
    public void TryOpenFreshEntry_MemoryBackedPackage_ReturnsNull()
    {
        // MemoryStream backing → _backing.Stream is NOT FileStream → returns null
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("xl/workbook.xml");
            using Stream entryStream = entry.Open();
            using var w = new StreamWriter(entryStream, Encoding.UTF8, leaveOpen: true);
            w.Write("<workbook/>");
        }
        ms.Position = 0;

        using XlsxPackage package = XlsxPackage.Open(ms);
        Stream? result = package.TryOpenFreshEntry("xl/workbook.xml");
        Assert.Null(result);
    }
}
