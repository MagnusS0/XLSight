using System.Buffers.Text;
using System.Runtime.CompilerServices;

namespace XLSight.Internal.Readers.Xlsx;

/// <summary>
/// Parses an Excel cell reference (e.g. "BZ42") from raw UTF-8 bytes
/// into a 1-based (column, row) pair without any string allocation.
/// </summary>
internal static class ColumnRefDecoder
{
    /// <summary>
    /// Parses a cell reference from UTF-8 bytes.
    /// </summary>
    /// <param name="bytes">The bytes between the quotes of an r="..." attribute (no quotes).</param>
    /// <param name="column">1-based column index, or 0 on failure.</param>
    /// <param name="row">1-based row index, or 0 on failure.</param>
    /// <returns><see langword="true"/> if the reference was parsed successfully.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryParse(ReadOnlySpan<byte> bytes, out int column, out int row)
    {
        column = 0;
        row = 0;

        if (bytes.IsEmpty)
        {
            return false;
        }

        // Parse leading letter(s): A–Z only (uppercase ASCII).
        int i = 0;
        int col = 0;
        while (i < bytes.Length)
        {
            byte b = bytes[i];
            if (b < (byte)'A' || b > (byte)'Z')
            {
                break;
            }

            col = col * 26 + (b - (byte)'A' + 1);
            i++;
        }

        if (i == 0 || i >= bytes.Length)
        {
            // No letters found, or no digits follow.
            return false;
        }

        // Parse trailing digit(s).
        if (!Utf8Parser.TryParse(bytes[i..], out int r, out _))
        {
            return false;
        }

        column = col;
        row = r;
        return true;
    }
}
