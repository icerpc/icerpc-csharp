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
internal class QuicStreamWriter : IMultiplexedStreamWriter
{
    public long UnflushedBytes => _pipe.Writer.UnflushedBytes;

    internal Task Closed { get; }

    private readonly Action _completeCallback;
    private bool _isCompleted;
    // Set by a WriteAsync call with endStream: no further write is allowed.
    private bool _isEnded;
    private readonly int _minSegmentSize;

    // We use a helper Pipe instead of a StreamPipeWriter over _stream because StreamPipeWriter does not provide a
    // WriteAsync with an endStream/completeWrites parameter while QuicStream does.
    private readonly Pipe _pipe;
    private readonly QuicStream _stream;

    private readonly Action _throwIfConnectionClosedOrDisposed;

    public void Advance(int count)
    {
        ThrowIfWritingNotAllowed();
        _pipe.Writer.Advance(count);
    }

    public void Abort()
    {
        if (!_isCompleted)
        {
            _isCompleted = true;

            // We don't use the application error code, it's irrelevant.
            _stream.Abort(QuicAbortDirection.Write, errorCode: 0);

            _completeCallback();

            _pipe.Writer.Complete();
            _pipe.Reader.Complete();
        }
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        // Advance throws once the stream is ended, so no byte can be pending after an endStream write.
        Debug.Assert(!_isEnded || _pipe.Writer.UnflushedBytes == 0);

        if (!_isCompleted)
        {
            // Nothing to send when the writes are already closed (by the peer or a connection closure) and no bytes
            // are pending.
            if (!_isEnded && (_pipe.Writer.UnflushedBytes > 0 || !Closed.IsCompleted))
            {
                try
                {
                    // Flushes the unflushed bytes, if any, and completes the writes of the QUIC stream. Unlike Slic,
                    // it's important to complete the writes and not abort the stream: data might still be buffered for
                    // send on the QUIC stream and aborting the stream would discard this data.
                    _ = await WriteAsync(ReadOnlySequence<byte>.Empty, endStream: true, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    Abort();
                    throw;
                }
            }

            _isCompleted = true;

            _completeCallback();

            _pipe.Writer.Complete();
            _pipe.Reader.Complete();
        }
    }

    public Memory<byte> GetMemory(int sizeHint = 0) => _pipe.Writer.GetMemory(sizeHint);

    public Span<byte> GetSpan(int sizeHint = 0) => _pipe.Writer.GetSpan(sizeHint);

    public async ValueTask<WriteResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken)
    {
        ThrowIfWritingNotAllowed();
        _isEnded = endStream;

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
                    return WriteResult.Written;
                }
            }

            if (!source.IsEmpty || endStream)
            {
                // With an empty source and endStream, this only completes the writes of the QUIC stream.
                await WriteSequenceAsync(source, completeWrites: endStream, cancellationToken).ConfigureAwait(false);
            }
            // else nothing to do: this typically corresponds to a flush when there was no unflushed bytes.
            return WriteResult.Written;
        }
        catch (QuicException exception) when (exception.QuicError == QuicError.StreamAborted)
        {
            return WriteResult.WritesClosed;
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

    internal QuicStreamWriter(
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

    private void ThrowIfWritingNotAllowed()
    {
        if (_isCompleted || _isEnded)
        {
            throw new InvalidOperationException(
                "Writing is not allowed once the writer is completed or the stream is ended.");
        }
    }
}
