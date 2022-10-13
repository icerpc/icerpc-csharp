// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using System.Net.Quic;

namespace IceRpc.Transports.Internal;

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal class QuicPipeReader : PipeReader
{
    private Exception? _abortException;
    private readonly Action _completedCallback;
    private readonly IMultiplexedStreamErrorCodeConverter _errorCodeConverter;
    private volatile bool _isCompleted;
    private readonly PipeReader _pipeReader;
    private readonly QuicStream _stream;

    public bool IsCompleted => _isCompleted;

    public override void AdvanceTo(SequencePosition consumed) =>
        _pipeReader.AdvanceTo(consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _pipeReader.AdvanceTo(consumed, examined);

    public override void CancelPendingRead() => _pipeReader.CancelPendingRead();

    public override void Complete(Exception? exception = null)
    {
        // This does not call _stream.Dispose since we have leaveOpen set to true.
        _pipeReader.Complete(exception);

        // TODO: it appears we need to call Abort even when exception is null, which does not make sense. Otherwise,
        // we apparently close with the default error code.
        Abort(exception);

        // Notify the stream of the reader completion.
        _completedCallback();

        _isCompleted = true;
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeReader.ReadAsync(CancellationToken.None).AsTask().WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (QuicException exception) when (
            exception.QuicError == QuicError.StreamAborted &&
            exception.ApplicationErrorCode is not null)
        {
            throw _abortException ?? _errorCodeConverter.FromErrorCode((ulong)exception.ApplicationErrorCode)!;
        }
        catch (QuicException exception) when (exception.QuicError == QuicError.ConnectionAborted)
        {
            // If the connection is closed before the stream. This indicates that the peer forcefully closed the
            // connection (it called DisposeAsync before completing the streams).
            throw _abortException ?? new TransportException(TransportErrorCode.ConnectionReset, exception);
        }
        catch (QuicException exception)
        {
            throw _abortException ?? exception.ToTransportException();
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
        {
            // We can't let _pipeReader.ReadAsync(CancellationToken.None) run, we need to abort it.
            Abort(exception);
            throw _abortException ?? exception;
        }
        catch (Exception exception)
        {
            throw _abortException ?? new TransportException(TransportErrorCode.Unspecified, exception);
        }
    }

    public override bool TryRead(out ReadResult result) => _pipeReader.TryRead(out result);

    internal QuicPipeReader(
        QuicStream stream,
        IMultiplexedStreamErrorCodeConverter errorCodeConverter,
        MemoryPool<byte> pool,
        int minimumSegmentSize,
        Action completedCallback)
    {
        _stream = stream;
        _errorCodeConverter = errorCodeConverter;
        _completedCallback = completedCallback;

        // TODO: configure minimumReadSize?
        _pipeReader = Create(
            _stream,
            new StreamPipeReaderOptions(pool, minimumSegmentSize, minimumReadSize: -1, leaveOpen: true));
    }

    // Note that the exception has 2 separate purposes: transmit an error code to the peer _and_ throw this exception
    // from the current or next ReadAsync.
    internal void Abort(Exception? exception)
    {
        _abortException ??= exception;

        try
        {
            _stream.Abort(QuicAbortDirection.Read, (long)_errorCodeConverter.ToErrorCode(exception));
        }
        catch (ObjectDisposedException)
        {
            // ignored
        }
    }
}
