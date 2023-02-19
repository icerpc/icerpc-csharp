// Copyright (c) ZeroC, Inc.

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

            // We don't use the application error code, it's irrelevant.
            _stream.Abort(QuicAbortDirection.Read, errorCode: 0);

            // This does not call _stream.Dispose since leaveOpen is set to true.
            _pipeReader.Complete();

            _completeCallback();
        }
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken != cancellationToken)
        {
            // Workaround for https://github.com/dotnet/runtime/issues/82594
            cancellationToken.ThrowIfCancellationRequested();
            throw;
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
