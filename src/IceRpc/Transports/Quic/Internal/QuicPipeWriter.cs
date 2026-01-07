// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Quic;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace IceRpc.Transports.Quic.Internal;

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
internal class QuicPipeWriter : ReadOnlySequencePipeWriter
{
    public override bool CanGetUnflushedBytes => true;

    public override long UnflushedBytes => _pipe.Writer.UnflushedBytes;

    internal Task Closed { get; }

    private bool _isCompleted;
    private readonly Action _completeCallback;
    private readonly int _minSegmentSize;

    // We use a helper Pipe instead of a StreamPipeWriter over _stream because StreamPipeWriter does not provide a
    // WriteAsync with an endStream/completeWrites parameter while QuicStream does.
    private readonly Pipe _pipe;
    private readonly QuicStream _stream;

    private readonly Action _throwIfConnectionClosedOrDisposed;

    public override void Advance(int bytes) => _pipe.Writer.Advance(bytes);

    // QuicPipeWriter does not support this method: the IceRPC core does not need it. And when the application code
    // installs a payload writer interceptor, this interceptor should never call it on "next".
    public override void CancelPendingFlush() => throw new NotSupportedException();

    public override void Complete(Exception? exception = null)
    {
        if (!_isCompleted)
        {
            if (exception is null && _pipe.Writer.UnflushedBytes > 0)
            {
                throw new InvalidOperationException(
                    $"Completing a {nameof(QuicPipeWriter)} without an exception is not allowed when this pipe writer has unflushed bytes.");
            }

            _isCompleted = true;

            if (exception is null)
            {
                // Unlike Slic, it's important to complete the writes and not abort the stream. Data might still be
                // buffered for send on the QUIC stream and aborting the stream would discard this data.
                _stream.CompleteWrites();
            }
            else
            {
                // We don't use the application error code, it's irrelevant.
                _stream.Abort(QuicAbortDirection.Write, errorCode: 0);
            }

            _completeCallback();

            _pipe.Writer.Complete();
            _pipe.Reader.Complete();
        }
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken) =>
        // WriteAsync will flush the internal buffer
        WriteAsync(ReadOnlySequence<byte>.Empty, endStream: false, cancellationToken);

    public override Memory<byte> GetMemory(int sizeHint) => _pipe.Writer.GetMemory(sizeHint);

    public override Span<byte> GetSpan(int sizeHint) => _pipe.Writer.GetSpan(sizeHint);

    public override ValueTask<FlushResult> WriteAsync(
        ReadOnlyMemory<byte> source,
        CancellationToken cancellationToken) =>
        WriteAsync(new ReadOnlySequence<byte>(source), endStream: false, cancellationToken);

    public override async ValueTask<FlushResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken)
    {
        if (_isCompleted)
        {
            throw new InvalidOperationException("Writing is not allowed once the writer is completed.");
        }

        _throwIfConnectionClosedOrDisposed();

        try
        {
            if (_pipe.Writer.UnflushedBytes > 0)
            {
                if (!source.IsEmpty && source.Length < _minSegmentSize)
                {
                    // When source fits in the last segment of _pipe.Writer, we copy it into _pipe.Writer.

                    Memory<byte> pipeMemory = _pipe.Writer.GetMemory();
                    if (source.Length <= pipeMemory.Length)
                    {
                        source.CopyTo(pipeMemory.Span);
                        _pipe.Writer.Advance((int)source.Length);
                        source = ReadOnlySequence<byte>.Empty;
                    }
                    else
                    {
                        _pipe.Writer.Advance(0);
                    }
                }

                // Flush the internal pipe.
                FlushResult flushResult = await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                Debug.Assert(!flushResult.IsCanceled && !flushResult.IsCompleted);

                // Read the data from the pipe.
                bool tryReadOk = _pipe.Reader.TryRead(out ReadResult readResult);
                Debug.Assert(tryReadOk);
                Debug.Assert(!readResult.IsCanceled && !readResult.IsCompleted && readResult.Buffer.Length > 0);

                try
                {
                    // Write buffered data to the stream
                    await WriteSequenceAsync(
                        readResult.Buffer,
                        completeWrites: endStream && source.IsEmpty,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _pipe.Reader.AdvanceTo(readResult.Buffer.End);
                }

                if (source.IsEmpty)
                {
                    // We're done, we don't want to write again an empty sequence.
                    return new FlushResult(isCanceled: false, isCompleted: endStream);
                }
            }

            if (source.IsEmpty && !endStream)
            {
                // Nothing to do; this typically corresponds to a call to FlushAsync when there was no unflushed bytes.
                return new FlushResult(isCanceled: false, isCompleted: false);
            }
            else
            {
                await WriteSequenceAsync(source, completeWrites: endStream, cancellationToken).ConfigureAwait(false);
                return new FlushResult(isCanceled: false, isCompleted: endStream);
            }
        }
        catch (QuicException exception) when (exception.QuicError == QuicError.StreamAborted)
        {
            return new FlushResult(isCanceled: false, isCompleted: true);
        }
        catch (QuicException exception)
        {
            throw exception.ToIceRpcException();
        }
        catch (SocketException exception)
        {
            throw exception.ToIceRpcException();
        }
        // We don't wrap other exceptions

        async ValueTask WriteSequenceAsync(
            ReadOnlySequence<byte> sequence,
            bool completeWrites,
            CancellationToken cancellationToken)
        {
            if (sequence.IsSingleSegment)
            {
                await _stream.WriteAsync(sequence.First, completeWrites, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                ReadOnlySequence<byte>.Enumerator enumerator = sequence.GetEnumerator();
                bool hasMore = enumerator.MoveNext();
                Debug.Assert(hasMore);
                do
                {
                    ReadOnlyMemory<byte> buffer = enumerator.Current;
                    hasMore = enumerator.MoveNext();
                    await _stream.WriteAsync(buffer, completeWrites: completeWrites && !hasMore, cancellationToken)
                        .ConfigureAwait(false);
                }
                while (hasMore);
            }
        }
    }

    internal QuicPipeWriter(
        QuicStream stream,
        MemoryPool<byte> pool,
        int minSegmentSize,
        Action completeCallback,
        Action throwIfConnectionClosedOrDisposed)
    {
        _stream = stream;
        _minSegmentSize = minSegmentSize;
        _completeCallback = completeCallback;

        // This callback is used to check if the connection is closed or disposed before calling WriteAsync on the QUIC
        // stream. This check works around the use of the QuicError.OperationAborted error code for both reporting the
        // abortion of the in-progress write call and for reporting a closed connection before the operation process
        // starts. In this latter case, we want to report ConnectionAborted.
        _throwIfConnectionClosedOrDisposed = throwIfConnectionClosedOrDisposed;

        // Create a pipe that never pauses on flush or write. The QUIC _stream.WriteAsync will block if the QUIC flow
        // control doesn't permit sending more data.
        // The readerScheduler doesn't matter (we don't call _pipe.Reader.ReadAsync) and the writerScheduler doesn't
        // matter (_pipe.Writer.FlushAsync never blocks).
        _pipe = new(new PipeOptions(
            pool: pool,
            minimumSegmentSize: minSegmentSize,
            pauseWriterThreshold: 0,
            useSynchronizationContext: false));

        Closed = ClosedAsync();

        async Task ClosedAsync()
        {
            try
            {
                await _stream.WritesClosed.ConfigureAwait(false);
            }
            catch
            {
                // Ignore failures.
            }
        }
    }
}
