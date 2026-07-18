using XLSight.Internal.Readers;

namespace XLSight;

/// <summary>
/// A high-performance forward-only reader for Excel rows.
/// The row returned by <see cref="Current"/> is borrowed from an internal reusable buffer
/// and is only valid until the next successful call to <see cref="Read"/> or
/// <see cref="ReadAsync"/>.
/// </summary>
public sealed class ExcelSheetReader : IDisposable, IAsyncDisposable
{
    private readonly IRowCursor _cursor;
    private readonly Action? _onDispose;
    private bool _disposed;
    private bool _hasCurrent;

    internal ExcelSheetReader(IRowCursor cursor, Action? onDispose = null)
    {
        _cursor = cursor;
        _onDispose = onDispose;
    }

    /// <summary>
    /// Gets the current borrowed row.
    /// The row is only valid until the next successful call to <see cref="Read"/> or
    /// <see cref="ReadAsync"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no row has been read yet or the reader has advanced past the final row.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown when this reader has been disposed.</exception>
    public ExcelRow Current
    {
        get
        {
            ThrowIfDisposed();
            if (!_hasCurrent)
            {
                throw new InvalidOperationException("No current row is available. Call Read() or ReadAsync() first.");
            }

            return _cursor.Current;
        }
    }

    /// <summary>Advances to the next row synchronously.</summary>
    /// <returns><see langword="true"/> when a row was read; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this reader has been disposed.</exception>
    public bool Read()
    {
        ThrowIfDisposed();
        _hasCurrent = _cursor.MoveNext();
        return _hasCurrent;
    }

    /// <summary>Advances to the next row asynchronously.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><see langword="true"/> when a row was read; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this reader has been disposed.</exception>
    public async ValueTask<bool> ReadAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (_cursor.TryParseNext(out _))
            {
                _hasCurrent = true;
                return true;
            }

            if (_cursor.IsSheetDone)
            {
                _hasCurrent = false;
                return false;
            }

            bool hasMore = await _cursor.RefillAsync(ct).ConfigureAwait(false);
            if (!hasMore)
            {
                _hasCurrent = false;
                return false;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _cursor.Dispose();
        }
        finally
        {
            _onDispose?.Invoke();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
