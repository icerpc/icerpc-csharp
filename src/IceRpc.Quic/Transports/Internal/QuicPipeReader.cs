// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using System.Net.Quic;

namespace IceRpc.Transports.Internal;

/// <summary>Implements a PipeReader over a QuicStream.</summary>
internal class QuicPipeReader : PipeReader
{
    internal Task Closed { get; }

    // Complete is not thread-safe; it's volatile because we check _isCompleted in the implementation of Closed.
    private volatile bool _isCompleted;
    private readonly PipeReader _pipeReader;
    private readonly TaskCompletionSource _readsClosed = new();
    private readonly QuicStream _stream;

    // StreamPipeReader.AdvanceTo does not call the underlying stream and as a result does not throw any QuicException.
    public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _pipeReader.AdvanceTo(consumed, examined);

    public override void CancelPendingRead() => _pipeReader.CancelPendingRead();

    public override void Complete(Exception? exception = null)
    {
        if (!_isCompleted)
        {
            _isCompleted = true;

            // This does not call _stream.Dispose since leaveOpen is set to true. The current implementation of
            // StreamPipeReader doesn't use the exception and it's unclear how it could use it.
            _pipeReader.Complete(exception);

            // Tell the remote writer we're done reading. The error code is irrelevant.
            try
            {
                _stream.Abort(QuicAbortDirection.Read, errorCode: 0);
            }
            catch (ObjectDisposedException)
            {
                // Expected if the stream has been disposed.
            }

            _readsClosed.TrySetResult();
        }
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ReadResult readResult = await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (readResult.IsCompleted)
            {
                _readsClosed.TrySetResult();
            }
            return readResult;
        }
        catch (QuicException exception)
        {
            _readsClosed.TrySetResult();
            throw exception.ToIceRpcException();
        }
        catch (ObjectDisposedException)
        {
            // The Quic stream can be disposed as soon as ReadsClosed fails. We still want ReadAsync to raise the reason
            // of the ReadsClosed failure instead of OperationAborted. Otherwise, the caller wouldn't get the reason of
            // the stream's reading side closure (ConnectionAborted, TruncatedData, ...).
            try
            {
                await _stream.ReadsClosed.ConfigureAwait(false);
            }
            catch (QuicException exception)
            {
                _readsClosed.TrySetResult();
                throw exception.ToIceRpcException();
            }

            _readsClosed.TrySetResult();
            throw new IceRpcException(IceRpcError.OperationAborted);
        }
        // We don't catch and wrap other exceptions. It could be for example an InvalidOperationException when
        // attempting to read while another read is in progress.
    }

    // StreamPipeReader.TryRead does not call the underlying QuicStream and as a result does not throw any
    // QuicException.
    public override bool TryRead(out ReadResult result) => _pipeReader.TryRead(out result);

    internal QuicPipeReader(QuicStream stream, MemoryPool<byte> pool, int minimumSegmentSize)
    {
        _stream = stream;

        _pipeReader = Create(
            _stream,
            new StreamPipeReaderOptions(pool, minimumSegmentSize, minimumReadSize: -1, leaveOpen: true));

        Closed = ClosedAsync();

        async Task ClosedAsync()
        {
            try
            {
                await _stream.ReadsClosed.ConfigureAwait(false);

                // See https://github.com/dotnet/runtime/issues/79818. We can't return immediately if reads are closed
                // successfully. The Quic stream would be disposed too early, before all the stream buffered data is
                // consumed. We wait on _readsClosed here to ensure the stream's buffered data is consumed by ReadAsync.
                await _readsClosed.Task.ConfigureAwait(false);
            }
            catch
            {
                // Ignore
            }
        }
    }
}
