using System.Reflection;
using XLSight.Analysis;
using XLSight.Internal.Readers;
using XLSight.Internal.Scanning;
using XLSight.Layout;
using XLSight.Query;
using XLSight.Query.Tests;
using Xunit;

namespace XLSight.Tests.Cancellation;

public sealed class AsyncCancellationRegressionTests
{
    [Fact]
    public async Task ScanWorksheetAsync_CancellationOnFinalCell_ThrowsOperationCanceledException()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        using var source = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workbook.ScanWorksheetAsync(
                SalesWorkbook.SheetName,
                new CancelOnCellSink(source, row: 11, column: 6),
                source.Token));
    }

    [Fact]
    public async Task AnalyzeSheetAsync_CancellationDuringRunningAnalysis_ThrowsOperationCanceledException()
    {
        using var inner = SalesWorkbook.Build();
        using var stream = new GateStream(inner);
        using var workbook = ExcelWorkbook.Open(stream);

        // Populate analyzer metadata, shared strings, and styles before arming the gate.
        // The next read is therefore running worksheet analysis, not lazy initialization.
        workbook.AnalyzeSheet(SalesWorkbook.SheetName, AnalysisLevel.Full);

        using var source = new CancellationTokenSource();
        stream.ArmNextSynchronousRead();
        Task<SheetInfo> operation = workbook.AnalyzeSheetAsync(
            SalesWorkbook.SheetName,
            AnalysisLevel.Full,
            options: null,
            source.Token);

        await stream.ReadStarted.WaitAsync(TestContext.Current.CancellationToken);
        source.Cancel();
        stream.ReleaseRead();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task GetSheetReaderAsync_CancellationDuringCursorAcquisition_ThrowsOperationCanceledException()
    {
        using var inner = SalesWorkbook.Build();
        using var stream = new GateStream(inner);
        using var workbook = ExcelWorkbook.Open(stream);

        // Populate the lazy shared-string and style state before isolating cursor acquisition.
        using (ExcelSheetReader preload = workbook.GetSheetReader(SalesWorkbook.SheetName))
        {
            Assert.True(preload.Read());
        }

        using var source = new CancellationTokenSource();
        stream.ArmNextSynchronousRead();

        Task<ExcelSheetReader> operation = workbook
            .GetSheetReaderAsync(SalesWorkbook.SheetName, ct: source.Token)
            .AsTask();

        await stream.ReadStarted.WaitAsync(TestContext.Current.CancellationToken);
        source.Cancel();
        stream.ReleaseRead();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
            {
                await using ExcelSheetReader reader = await operation.ConfigureAwait(false);
            });
    }

    [Fact]
    public async Task AnalyzeSheetAsync_CancellationDuringFirstMetadataLoad_StopsBlockedRead()
    {
        using var inner = SalesWorkbook.Build();
        using var stream = new GateStream(inner);
        using var workbook = ExcelWorkbook.Open(stream);
        using var source = new CancellationTokenSource();

        stream.ArmNextSynchronousRead();
        Task<SheetInfo> operation = workbook.AnalyzeSheetAsync(
            SalesWorkbook.SheetName,
            AnalysisLevel.Full,
            options: null,
            source.Token);

        await stream.ReadStarted.WaitAsync(TestContext.Current.CancellationToken);
        source.Cancel();
        stream.ReleaseRead();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task ExcelSheetReader_ReadAsync_CancellationDuringParsing_ThrowsOnNextRead()
    {
        using var source = new CancellationTokenSource();
        await using var reader = new ExcelSheetReader(new CancelDuringParseCursor(source));

        // Row-level contract: a row fully parsed before cancellation is observed is still
        // delivered; the cancellation surfaces on the next read.
        Assert.True(await reader.ReadAsync(source.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await reader.ReadAsync(source.Token));
    }

    [Fact]
    public async Task OpenAsync_CancellationDuringSynchronousPackageParsing_ThrowsOperationCanceledException()
    {
        using var inner = SalesWorkbook.Build();
        using var stream = new GateStream(inner);
        using var source = new CancellationTokenSource();

        stream.ArmNextSynchronousRead();
        Task<ExcelWorkbook> operation = Task.Run(
            () => ExcelWorkbook.OpenAsync(stream, source.Token));

        await stream.ReadStarted.WaitAsync(TestContext.Current.CancellationToken);
        source.Cancel();
        stream.ReleaseRead();

        ExcelWorkbook? unexpectedlyOpened = null;
        Exception? observed = null;
        try
        {
            unexpectedlyOpened = await operation;
        }
        catch (Exception exception)
        {
            observed = exception;
        }
        finally
        {
            if (unexpectedlyOpened is not null)
            {
                await unexpectedlyOpened.DisposeAsync();
            }
        }

        Assert.IsAssignableFrom<OperationCanceledException>(observed);
    }

    [Fact]
    public async Task QueryExecuteAsync_PreCanceledStatsPrunedQuery_ThrowsOperationCanceledException()
    {
        using var stream = SalesWorkbook.Build();
        using var workbook = ExcelWorkbook.Open(stream);
        using var source = new CancellationTokenSource();
        source.Cancel();

        SheetQuery query = workbook
            .QueryRange(SalesWorkbook.SheetName, "A1:F11")
            .Where("Units", QueryOperator.GreaterThan, 10)
            .Select(QueryAggregates.Count())
            .WithStats([UnitsProfile(min: 1, max: 10)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => query.ExecuteAsync(source.Token));
    }

    [Fact]
    public async Task ExecuteQueryAsync_HeaderAutoCancellation_StopsBlockedAnalysis()
    {
        using var inner = SalesWorkbook.Build();
        using var stream = new GateStream(inner);
        using var workbook = ExcelWorkbook.Open(stream);

        // Ensure the armed read belongs to HEADER AUTO's sheet scan rather than lazy metadata.
        workbook.AnalyzeSheet(SalesWorkbook.SheetName, AnalysisLevel.Full);

        using var source = new CancellationTokenSource();
        stream.ArmNextSynchronousRead();
        Task<QueryResult> operation = workbook.ExecuteQueryAsync(
            "FROM Sales!A1:F11 HEADER AUTO\nSELECT COUNT()",
            source.Token);

        await stream.ReadStarted.WaitAsync(TestContext.Current.CancellationToken);
        source.Cancel();
        stream.ReleaseRead();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task AnalyzeLayoutAsync_CancellationAfterFinalScannedCell_ThrowsOperationCanceledException()
    {
        using var source = new CancellationTokenSource();
        using var workbook = CreateWorkbook(new CancelAfterScanWorkbookReader(source));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workbook.AnalyzeLayoutAsync("Sheet1", source.Token));
    }

    private static ExcelWorkbook CreateWorkbook(IWorkbookReader reader)
    {
        ConstructorInfo constructor = typeof(ExcelWorkbook).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(IWorkbookReader)],
            modifiers: null)!;
        return (ExcelWorkbook)constructor.Invoke([reader]);
    }

    private static ColumnProfile UnitsProfile(double min, double max) => new()
    {
        ColumnIndex = 4,
        InferredHeader = "Units",
        DominantType = CellType.Number,
        NonEmptyCount = SalesWorkbook.Data.Length,
        TextCount = 0,
        NumberCount = SalesWorkbook.Data.Length,
        DateCount = 0,
        BooleanCount = 0,
        DistinctValueEstimate = SalesWorkbook.Data.Length,
        MinNumericValue = min,
        MaxNumericValue = max,
        MaxTextLength = null,
        HasFormulas = false,
    };

    private readonly struct CancelOnCellSink(
        CancellationTokenSource source,
        int row,
        int column) : IWorksheetScanSink
    {
        public void OnCell(int cellRow, int cellColumn, in ExcelCellValue value, bool isFormula)
        {
            if (cellRow == row && cellColumn == column)
            {
                source.Cancel();
            }
        }
    }

    private sealed class CancelDuringParseCursor(CancellationTokenSource source) : IRowCursor
    {
        private bool _parsed;

        public ExcelRow Current { get; private set; }

        public bool IsSheetDone => _parsed;

        public bool MoveNext() => throw new NotSupportedException();

        public bool TryParseNext(out ExcelRow row)
        {
            source.Cancel();
            _parsed = true;
            Current = new ExcelRow(1, new[] { ExcelCellValue.FromNumber(1) });
            row = Current;
            return true;
        }

        public ValueTask<bool> RefillAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(false);

        public void Dispose() { }
    }

    private sealed class CancelAfterScanWorkbookReader(CancellationTokenSource source) : IWorkbookReader
    {
        public bool IsFileBacked => true;
        public WorkbookFormat Format => WorkbookFormat.Xlsx;
        public IReadOnlyList<string> SheetNames => ["Sheet1"];
        public bool IsDate1904 => false;
        public bool HasMacros => false;

        public void ScanWorksheet<TSink>(
            string sheetName,
            ref TSink sink,
            CancellationToken ct = default)
            where TSink : struct, IWorksheetScanSink
        {
            ExcelCellValue value = ExcelCellValue.FromNumber(1);
            sink.OnCell(1, 1, in value, isFormula: false);
            source.Cancel();
        }

        public VbaProjectInfo? GetVbaProject() => throw new NotSupportedException();
        public string GetVbaModuleSource(string moduleName) => throw new NotSupportedException();
        public byte[] GetVbaModuleSourceBytes(string moduleName) => throw new NotSupportedException();
        public ExcelCellValue ReadCell(string sheetName, ExcelAddress address, ReadMode mode) =>
            throw new NotSupportedException();
        public RangeResult ReadRange(string sheetName, ExcelRange range, ReadMode mode) =>
            throw new NotSupportedException();
        public Task<ExcelCellValue> ReadCellAsync(
            string sheetName,
            ExcelAddress address,
            ReadMode mode,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<RangeResult> ReadRangeAsync(
            string sheetName,
            ExcelRange range,
            ReadMode mode,
            CancellationToken ct) => throw new NotSupportedException();
        public WorkbookInfo Analyze(
            AnalysisLevel level,
            int maxDegreeOfParallelism = -1,
            AnalysisOptions? options = null) => throw new NotSupportedException();
        public SheetInfo AnalyzeSheet(
            string sheetName,
            AnalysisLevel level,
            AnalysisOptions? options = null) => throw new NotSupportedException();
        public Task<WorkbookInfo> AnalyzeAsync(
            AnalysisLevel level,
            int maxDegreeOfParallelism = -1,
            AnalysisOptions? options = null,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SheetInfo> AnalyzeSheetAsync(
            string sheetName,
            AnalysisLevel level,
            AnalysisOptions? options,
            CancellationToken ct) => throw new NotSupportedException();
        public IRowCursor OpenCursor(
            string sheetName,
            ExcelRange range,
            ReadMode mode,
            RowProjection? projection = null) => throw new NotSupportedException();
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class GateStream(Stream inner) : Stream
    {
        private const int PostReleaseReadBudget = 1024;
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(initialState: false);
        private int _armed;
        private int _remainingPostReleaseReadBudget;
        private int _enforcePostReleaseReadBudget;

        public Task ReadStarted => _readStarted.Task;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public void ArmNextSynchronousRead() => Volatile.Write(ref _armed, 1);

        public void ReleaseRead() => _release.Set();

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            bool wasArmed = WaitIfArmed();
            if (wasArmed)
            {
                Volatile.Write(ref _remainingPostReleaseReadBudget, PostReleaseReadBudget);
                Volatile.Write(ref _enforcePostReleaseReadBudget, 1);
            }
            int read = inner.Read(buffer, offset, LimitReadCount(count));
            ConsumeReadBudget(read);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            bool wasArmed = WaitIfArmed();
            if (wasArmed)
            {
                Volatile.Write(ref _remainingPostReleaseReadBudget, PostReleaseReadBudget);
                Volatile.Write(ref _enforcePostReleaseReadBudget, 1);
            }
            int read = inner.Read(buffer[..LimitReadCount(buffer.Length)]);
            ConsumeReadBudget(read);
            return read;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _release.Set();
                _release.Dispose();
            }

            base.Dispose(disposing);
        }

        private bool WaitIfArmed()
        {
            if (Interlocked.Exchange(ref _armed, 0) == 0)
            {
                return false;
            }

            _readStarted.TrySetResult();
            _release.Wait();
            return true;
        }

        private int LimitReadCount(int requested)
        {
            if (Volatile.Read(ref _enforcePostReleaseReadBudget) == 0)
            {
                return requested;
            }

            int remaining = Volatile.Read(ref _remainingPostReleaseReadBudget);
            if (remaining <= 0)
            {
                throw new UnexpectedSynchronousReadException();
            }

            return Math.Min(requested, remaining);
        }

        private void ConsumeReadBudget(int read)
        {
            if (Volatile.Read(ref _enforcePostReleaseReadBudget) != 0)
            {
                Interlocked.Add(ref _remainingPostReleaseReadBudget, -read);
            }
        }
    }

    private sealed class UnexpectedSynchronousReadException : Exception { }
}
