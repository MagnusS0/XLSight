using System.Text;
using Xunit;
using XLSight.Infrastructure;

namespace XLSight.Tests.Infrastructure;

public sealed class SeekableBackingTests
{
    [Fact]
    public void Create_FromMemoryStream_UsesOriginalStream()
    {
        // Expandable MemoryStream (new MemoryStream() / MemoryStream(int capacity)) sets
        // _exposable = true so TryGetBuffer succeeds — the backing uses it directly without copying.
        var stream = new MemoryStream();
        stream.Write(Encoding.UTF8.GetBytes("abc"));
        stream.Position = 0;

        using SeekableBacking backing = SeekableBacking.Create(stream);

        Assert.Same(stream, backing.Stream);
        Assert.False(backing.OwnsStream);
    }

    [Fact]
    public void Create_FromSeekableFileStream_CopiesToOwnedMemoryStream()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "xlsx");
            using var fileStream = File.OpenRead(path);

            using SeekableBacking backing = SeekableBacking.Create(fileStream);

            Assert.NotSame(fileStream, backing.Stream);
            Assert.True(backing.OwnsStream);
            Assert.IsType<MemoryStream>(backing.Stream);

            using var reader = new StreamReader(backing.Stream, Encoding.UTF8, leaveOpen: true);
            Assert.Equal("xlsx", reader.ReadToEnd());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Create_FromNonSeekableStream_ThrowsInvalidOperationException()
    {
        using var stream = new NonSeekableReadStream(new MemoryStream(Encoding.UTF8.GetBytes("abc")));

        var exception = Assert.Throws<InvalidOperationException>(() => SeekableBacking.Create(stream));

        Assert.Contains("OpenAsync", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_FromMemoryStreamWithHiddenBuffer_CopiesStream()
    {
        // MemoryStream with publiclyVisible=false returns false from TryGetBuffer —
        // the spec requires a copy in this case.
        var buffer = Encoding.UTF8.GetBytes("abc");
        using var stream = new MemoryStream(buffer, index: 0, count: buffer.Length, writable: true, publiclyVisible: false);

        using SeekableBacking backing = SeekableBacking.Create(stream);

        Assert.NotSame(stream, backing.Stream);
        Assert.True(backing.OwnsStream);
        Assert.IsType<MemoryStream>(backing.Stream);
    }

    [Fact]
    public async Task CreateAsync_FromMemoryStream_UsesOriginalStream()
    {
        // Expandable MemoryStream — TryGetBuffer returns true, no copy made.
        var stream = new MemoryStream();
        stream.Write(Encoding.UTF8.GetBytes("abc"));
        stream.Position = 0;

        await using SeekableBacking backing = await SeekableBacking.CreateAsync(stream, TestContext.Current.CancellationToken);

        Assert.Same(stream, backing.Stream);
        Assert.False(backing.OwnsStream);
    }

    [Fact]
    public async Task CreateAsync_FromNonSeekableStream_CopiesToOwnedMemoryStream()
    {
        using var inner = new MemoryStream(Encoding.UTF8.GetBytes("async content"));
        using var stream = new NonSeekableReadStream(inner);

        await using SeekableBacking backing = await SeekableBacking.CreateAsync(stream, TestContext.Current.CancellationToken);

        Assert.NotSame(stream, backing.Stream);
        Assert.True(backing.OwnsStream);
        Assert.IsType<MemoryStream>(backing.Stream);
        using var reader = new StreamReader(backing.Stream, Encoding.UTF8, leaveOpen: true);
        Assert.Equal("async content", reader.ReadToEnd());
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            return base.DisposeAsync();
        }
    }
}
