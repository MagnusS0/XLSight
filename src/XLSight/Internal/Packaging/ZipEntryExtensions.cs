using System.IO.Compression;

namespace XLSight.Internal.Packaging;

internal static class ZipEntryExtensions
{
    /// <summary>
    /// Opens a zip entry with a 64 KB read buffer.
    /// <para>
    /// Without buffering, the underlying <see cref="System.IO.Compression.DeflateStream"/>
    /// receives many tiny reads from <see cref="System.Xml.XmlReader"/>, each incurring
    /// decompressor overhead. A 64 KB buffer amortises those calls significantly.
    /// ExcelDataReader uses this same technique specifically for .NET Core performance.
    /// </para>
    /// </summary>
    internal static Stream OpenBuffered(this ZipArchiveEntry entry) =>
        new BufferedStream(entry.Open(), 65536);
}
