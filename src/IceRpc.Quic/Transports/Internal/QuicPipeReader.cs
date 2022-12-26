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
    private readonly Action _completeCallback;
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

            // This does not call _stream.Dispose since leaveOpen is set to true. The current implementation of
            // StreamPipeReader doesn't use the exception and it's unclear how it could use it.
            _pipeReader.Complete(exception);

            // Tell the remote writer we're done reading. The error code is irrelevant.
            _stream.Abort(QuicAbortDirection.Read, errorCode: 0);

            _completeCallback();
        }
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (QuicException exception)
        {
            throw exception.ToIceRpcException();
        }
        // We don't catch and wrap other exceptions. It could be for example an InvalidOperationException when
        // attempting to read while another read is in progress.
    }

    // StreamPipeReader.TryRead does not call the underlying QuicStream and as a result does not throw any
    // QuicException.
    public override bool TryRead(out ReadResult result) => _pipeReader.TryRead(out result);

    internal QuicPipeReader(QuicStream stream, MemoryPool<byte> pool, int minimumSegmentSize, Action completeCallback)
    {
        _stream = stream;
        _completeCallback = completeCallback;
        _pipeReader = Create(
            _stream,
            new StreamPipeReaderOptions(pool, minimumSegmentSize, minimumReadSize: -1, leaveOpen: true));

        Closed = ClosedAsync();

        async Task ClosedAsync()
        {
            try
            {
                await _stream.ReadsClosed.ConfigureAwait(false);
            }
            catch
            {
                // Ignore
            }
        }
    }
}
