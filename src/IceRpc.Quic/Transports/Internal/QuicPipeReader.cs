// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Quic;

namespace IceRpc.Transports.Internal;

#pragma warning disable CA1001 // Type owns disposable field(s) '_abortCts' but is not disposable
[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal class QuicPipeReader : PipeReader
#pragma warning restore CA1001
{
    private readonly CancellationTokenSource _abortCts = new();
    private readonly Action _completedCallback;
    private readonly IMultiplexedStreamErrorCodeConverter _errorCodeConverter;
    private Exception? _exception;
    private volatile bool _isCompleted;
    private readonly Pipe _pipe;
    private readonly QuicStream _stream;

    public bool IsCompleted => _isCompleted;

    public override void AdvanceTo(SequencePosition consumed) =>
        _pipe.Reader.AdvanceTo(consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _pipe.Reader.AdvanceTo(consumed, examined);

    public override void CancelPendingRead() => _pipe.Reader.CancelPendingRead();

    public override void Complete(Exception? exception = null)
    {
        _pipe.Reader.Complete(exception);

        // TODO: it appears we need to call Abort even when exception is null, which does not make sense.
        Abort(exception);

        // Cleanup resources.
        _abortCts.Dispose();

        // Notify the stream of the reader completion.
        _completedCallback();

        _isCompleted = true;
    }

    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
        _pipe.Reader.ReadAsync(cancellationToken);

    public override bool TryRead(out ReadResult result) => _pipe.Reader.TryRead(out result);

    internal QuicPipeReader(
        QuicStream stream,
        IMultiplexedStreamErrorCodeConverter errorCodeConverter,
        int pauseReaderThreshold,
        int resumeReaderThreshold,
        MemoryPool<byte> pool,
        int minimumSegmentSize,
        Action completedCallback)
    {
        _stream = stream;
        _errorCodeConverter = errorCodeConverter;
        _completedCallback = completedCallback;

        // The pause/resume reader threshold configuration are in turn the configuration for the pipe writer
        // pause/resume writer threshold. The background reads from the Quic stream will stop once the pause writer
        // threshold is reached.
        _pipe = new(new PipeOptions(
            pool: pool,
            pauseWriterThreshold: pauseReaderThreshold,
            resumeWriterThreshold: resumeReaderThreshold,
            minimumSegmentSize: minimumSegmentSize,
            writerScheduler: PipeScheduler.Inline));

        // Start a task to read data from the stream and feed the pipe.
        _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        Memory<byte> buffer = _pipe.Writer.GetMemory();

                        // We don't cancel QuicStream.ReadAsync since its cancellation aborts the stream reads under
                        // the hood. See https://github.com/dotnet/runtime/issues/72607
                        // TODO: add support for ValueTask.WaitAsync?
                        int byteCount = await _stream.ReadAsync(buffer, CancellationToken.None).AsTask().WaitAsync(
                            _abortCts.Token).ConfigureAwait(false);
                        _pipe.Writer.Advance(byteCount);
                        if (byteCount == 0)
                        {
                            return;
                        }

                        FlushResult flushResult = await _pipe.Writer.FlushAsync(_abortCts.Token).ConfigureAwait(false);
                        Debug.Assert(!flushResult.IsCanceled && !flushResult.IsCompleted);
                    }
                }
                catch (QuicException exception) when (
                    exception.QuicError == QuicError.StreamAborted &&
                    exception.ApplicationErrorCode is not null)
                {
                    Abort(_errorCodeConverter.FromErrorCode((ulong)exception.ApplicationErrorCode));
                }
                catch (QuicException exception) when (exception.QuicError == QuicError.ConnectionAborted)
                {
                    // If the connection is closed before the stream. This indicates that the peer forcefully closed the
                    // connection (it called DisposeAsync before completing the streams).
                    Abort(new TransportException(TransportErrorCode.ConnectionReset, exception));
                }
                catch (QuicException exception)
                {
                    Abort(exception.ToTransportException());
                }
                catch (OperationCanceledException)
                {
                    // Abort already called.
                }
                catch (ObjectDisposedException)
                {
                    // Stream disposed after completion of this QuicPipeReader.
                }
                catch (Exception exception)
                {
                    Abort(new TransportException(TransportErrorCode.Unspecified, exception));
                }
                finally
                {
                    _pipe.Writer.Complete(_exception);
                }
            });
    }

    // Note that the exception has 2 separate purposes: transmit an error code to the peer _and_ throw this exception
    // from the current or next ReadAsync.
    internal void Abort(Exception? exception)
    {
        try
        {
            _exception ??= exception;
            _stream.Abort(QuicAbortDirection.Read, (long)_errorCodeConverter.ToErrorCode(exception));
            _abortCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignored
        }
    }
}
