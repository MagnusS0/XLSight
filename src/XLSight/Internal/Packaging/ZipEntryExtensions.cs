using System.IO.Compression;

namespace XLSight.Internal.Packaging;

internal static class ZipEntryExtensions
{
    private const int MaxReadBufferSize = 65536;
    private const int MinReadBufferSize = 512;

    /// <summary>
    /// Opens a zip entry with a read buffer of up to 64 KB.
    /// <para>
    /// Without buffering, the underlying <see cref="System.IO.Compression.DeflateStream"/>
    /// receives many tiny reads from <see cref="System.Xml.XmlReader"/>, each incurring
    /// decompressor overhead. A 64 KB buffer amortises those calls significantly.
    /// ExcelDataReader uses this same technique specifically for .NET Core performance.
    /// </para>
    /// </summary>
    internal static Stream OpenBuffered(this ZipArchiveEntry entry) =>
        new BufferedStream(entry.Open(), ReadBufferSize(entry));

    /// <summary>
    /// Sizes a read buffer to the entry's declared uncompressed length, capped at 64 KB.
    /// Most package parts (workbook.xml, rels, styles) are a few KB, so a fixed 64 KB
    /// buffer is mostly unused heap. The declared length is only a sizing hint: if it
    /// understates the real size the stream still reads correctly, just in smaller chunks.
    /// </summary>
    internal static int ReadBufferSize(ZipArchiveEntry entry) =>
        (int)Math.Clamp(entry.Length, MinReadBufferSize, MaxReadBufferSize);
}
