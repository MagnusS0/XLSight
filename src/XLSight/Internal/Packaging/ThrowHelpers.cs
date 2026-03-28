using System.Diagnostics.CodeAnalysis;

namespace XLSight.Internal.Packaging;

internal static class ThrowHelpers
{
    [DoesNotReturn]
    public static void ThrowNonSeekableStreamRequiresAsync()
    {
        throw new InvalidOperationException(
            "Non-seekable streams require OpenAsync. Use ExcelWorkbook.OpenAsync() instead.");
    }

    [DoesNotReturn]
    public static void ThrowObjectDisposed(string objectName)
    {
        throw new ObjectDisposedException(objectName);
    }
}
