// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.IO.Pipelines;
using System.Net.Quic;
using System.Net.Sockets;

namespace IceRpc.Transports.Quic.Internal;

/// <summary>Implements a PipeReader over a QuicStream.</summary>
internal class QuicPipeReader : PipeReader
{
    private bool _isCompleted;
    private readonly Action _completeCallback;
    private readonly Action _throwIfConnectionClosedOrDisposed;
    private readonly PipeReader _pipeReader;
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

            // We don't use the application error code, it's irrelevant.
            _stream.Abort(QuicAbortDirection.Read, errorCode: 0);

            // This does not call _stream.Dispose since leaveOpen is set to true.
            _pipeReader.Complete();

            _completeCallback();
        }
    }

    public override async Task CopyToAsync(Stream destination, CancellationToken cancellationToken)
    {
        _throwIfConnectionClosedOrDisposed();
        try
        {
            await _pipeReader.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        catch (QuicException exception)
        {
            throw exception.ToIceRpcException();
        }
        catch (SocketException exception)
        {
            throw exception.ToIceRpcException();
        }
        // We don't catch and wrap other exceptions. It could be for example an InvalidOperationException when
        // attempting to read while another read is in progress.
    }

    public override async Task CopyToAsync(PipeWriter writer, CancellationToken cancellationToken)
    {
        _throwIfConnectionClosedOrDisposed();
        try
        {
            await _pipeReader.CopyToAsync(writer, cancellationToken).ConfigureAwait(false);
        }
        catch (QuicException exception)
        {
            throw exception.ToIceRpcException();
        }
        catch (SocketException exception)
        {
            throw exception.ToIceRpcException();
        }
        // We don't catch and wrap other exceptions. It could be for example an InvalidOperationException when
        // attempting to read while another read is in progress.
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        // First check if there's buffered data. If the connection is closed, we still want to return this data.
        if (TryRead(out ReadResult readResult))
        {
            return readResult;
        }

        _throwIfConnectionClosedOrDisposed();

        try
        {
            return await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (QuicException exception)
        {
            throw exception.ToIceRpcException();
        }
        catch (SocketException exception)
        {
            throw exception.ToIceRpcException();
        }
        // We don't catch and wrap other exceptions. It could be for example an InvalidOperationException when
        // attempting to read while another read is in progress.
    }

    // StreamPipeReader.TryRead does not call the underlying QuicStream and as a result does not throw any
    // QuicException.
    public override bool TryRead(out ReadResult result) => _pipeReader.TryRead(out result);

    protected override async ValueTask<ReadResult> ReadAtLeastAsyncCore(
        int minimumSize,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _pipeReader.ReadAtLeastAsync(minimumSize, cancellationToken).ConfigureAwait(false);
        }
        catch (QuicException exception)
        {
            throw exception.ToIceRpcException();
        }
        catch (SocketException exception)
        {
            throw exception.ToIceRpcException();
        }
        // We don't catch and wrap other exceptions. It could be for example an InvalidOperationException when
        // attempting to read while another read is in progress.
    }

    internal QuicPipeReader(
        QuicStream stream,
        MemoryPool<byte> pool,
        int minimumSegmentSize,
        Action completeCallback,
        Action throwIfConnectionClosed)
    {
        _stream = stream;
        _completeCallback = completeCallback;

        // This callback is used to check if the connection is closed or disposed before calling ReadAsync or TryRead on
        // the pipe reader. This check works around the use of the QuicError.OperationAborted error code for both
        // reporting the abortion of the in-progress read call and for reporting a closed connection before the
        // operation process starts. In this latter case, we want to report ConnectionAborted.
        _throwIfConnectionClosedOrDisposed = throwIfConnectionClosed;

        _pipeReader = Create(
            _stream,
            new StreamPipeReaderOptions(pool, minimumSegmentSize, minimumReadSize: -1, leaveOpen: true));

#if !NET8_0_OR_GREATER
        // Work around bug from StreamPipeReader with the BugFixStreamPipeReaderDecorator
        _pipeReader = new BugFixStreamPipeReaderDecorator(_pipeReader);
#endif
    }
}
