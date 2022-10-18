// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Quic;

namespace IceRpc.Transports.Internal;

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal class QuicPipeWriter : ReadOnlySequencePipeWriter
{
    internal Task WritesClosed { get; }

    private Exception? _abortException;
    private readonly Action _completedCallback;
    private readonly IMultiplexedStreamErrorCodeConverter _errorCodeConverter;
    private bool _isCompleted;
    private readonly int _minSegmentSize;
    private readonly Pipe _pipe;
    private readonly QuicStream _stream;

    public override void Advance(int bytes) => _pipe.Writer.Advance(bytes);

    public override void CancelPendingFlush() => _pipe.Writer.CancelPendingFlush();

    public override void Complete(Exception? exception = null)
    {
        if (!_isCompleted)
        {
            if (exception is null && _pipe.Writer.UnflushedBytes > 0)
            {
                throw new NotSupportedException($"can't complete {nameof(QuicPipeWriter)} with unflushed bytes");
            }

            _isCompleted = true;

            if (exception is null)
            {
                // Unlike Slic, it's important to complete the writes and not abort the stream with the NoError error
                // code. Data might still be buffered for send on the Quic stream and aborting the stream would discard
                // this data.
                _stream.CompleteWrites();
            }
            else
            {
                Abort(exception);
            }

            _pipe.Writer.Complete();

            // Notify the stream of the writer completion.
            _completedCallback();
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
        // Writing an empty buffer completes the stream.
        WriteAsync(new ReadOnlySequence<byte>(source), endStream: source.Length == 0, cancellationToken);

    public override async ValueTask<FlushResult> WriteAsync(
        ReadOnlySequence<byte> source,
        bool endStream,
        CancellationToken cancellationToken)
    {
        if (_isCompleted)
        {
            throw new InvalidOperationException("writing is not allowed once the writer is completed");
        }

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

            await WriteSequenceAsync(source, completeWrites: endStream, cancellationToken).ConfigureAwait(false);
            return new FlushResult(isCanceled: false, isCompleted: endStream);
        }
        catch (QuicException) when (Volatile.Read(ref _abortException) is Exception abortException)
        {
            throw abortException;
        }
        catch (QuicException exception) when (
            exception.QuicError == QuicError.StreamAborted &&
            exception.ApplicationErrorCode is not null)
        {
            if (_errorCodeConverter.FromErrorCode((ulong)exception.ApplicationErrorCode) is Exception ex)
            {
                throw ex;
            }
            else
            {
                return new FlushResult(isCanceled: false, isCompleted: true);
            }
        }
        catch (QuicException exception) when (exception.QuicError == QuicError.ConnectionAborted)
        {
            // If the connection is closed before the stream. This indicates that the peer forcefully closed the
            // connection (it called DisposeAsync before completing the streams).
            throw new TransportException(TransportErrorCode.ConnectionReset, exception);
        }
        catch (QuicException exception)
        {
            throw exception.ToTransportException();
        }
        // We don't wrap other exceptions

        ValueTask WriteSequenceAsync(
            ReadOnlySequence<byte> sequence,
            bool completeWrites,
            CancellationToken cancellationToken)
        {
            return sequence.IsSingleSegment ?
                _stream.WriteAsync(sequence.First, completeWrites, cancellationToken) : PerformWriteSequenceAsync();

            async ValueTask PerformWriteSequenceAsync()
            {
                var enumerator = new ReadOnlySequence<byte>.Enumerator(sequence);
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
        IMultiplexedStreamErrorCodeConverter errorCodeConverter,
        MemoryPool<byte> pool,
        int minSegmentSize,
        Action completedCallback)
    {
        _stream = stream;
        _errorCodeConverter = errorCodeConverter;
        _minSegmentSize = minSegmentSize;
        _completedCallback = completedCallback;

        // Create a pipe that never pauses on flush or write. The QuicPipeWriter will pause the flush or write if the
        // Quic flow control doesn't permit sending more data. We also use an inline pipe scheduler for write to avoid
        // thread context switches when FlushAsync is called on the internal pipe writer.
        _pipe = new(new PipeOptions(
            pool: pool,
            minimumSegmentSize: minSegmentSize,
            pauseWriterThreshold: 0,
            writerScheduler: PipeScheduler.Inline));

        WritesClosed = CreateWritesClosedTask();

        async Task CreateWritesClosedTask()
        {
            try
            {
                await _stream.WritesClosed.ConfigureAwait(false);
            }
            catch (QuicException exception)
            {
                throw exception.ToTransportException();
            }
            // we don't wrap other exceptions
        }
    }

    // The exception has 2 separate purposes: transmit an error code to the remote reader and throw this exception
    // exception from the current or next WriteAsync or FlushAsync.
    internal void Abort(Exception exception)
    {
        // If WritesClosed is already completed or this is not the first call to Abort, there is nothing to abort.
        if (!_stream.WritesClosed.IsCompleted &&
            Interlocked.CompareExchange(ref _abortException, exception, null) is null)
        {
            _stream.Abort(QuicAbortDirection.Write, (long)_errorCodeConverter.ToErrorCode(exception));
        }
    }
}
