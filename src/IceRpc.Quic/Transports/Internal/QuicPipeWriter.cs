// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Quic;

namespace IceRpc.Transports.Internal;

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
#pragma warning disable CA1001 // Type owns disposable field(s) '_abortCts' but is not disposable
internal class QuicPipeWriter : ReadOnlySequencePipeWriter
#pragma warning restore CA1001
{
    private readonly CancellationTokenSource _abortCts = new();
    private readonly Action _completedCallback;
    private readonly IMultiplexedStreamErrorCodeConverter _errorCodeConverter;
    private Exception? _exception;
    private readonly int _minSegmentSize;
    private readonly Pipe _pipe;
    private int _state;
    private readonly QuicStream _stream;

    public override void Advance(int bytes) => _pipe.Writer.Advance(bytes);

    public override void CancelPendingFlush() => _pipe.Writer.CancelPendingFlush();

    public override void Complete(Exception? exception = null)
    {
        if (_state.TrySetFlag(State.Completed))
        {
            if (exception is null && _pipe.Writer.UnflushedBytes > 0)
            {
                throw new NotSupportedException($"can't complete {nameof(QuicPipeWriter)} with unflushed bytes");
            }

            if (exception is null)
            {
                // Unlike Slic, it's important to complete the writes and not abort the stream with the NoError error
                // code. Data might still be buffered for send on the Quic stream and aborting the stream would discard
                // this data.
                _stream.CompleteWrites();
            }
            else
            {
                // Abort the write side of the stream with the error code corresponding to the exception.
                _stream.Abort(QuicAbortDirection.Write, (long)_errorCodeConverter.ToErrorCode(exception));
            }

            _pipe.Writer.Complete();
            Abort(exception);

            // Cleanup resources.
            _abortCts.Dispose();

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
        if (_state.HasFlag(State.Completed))
        {
            // If the writer is completed, the caller is bogus, it shouldn't call write operations after completing the
            // pipe writer.
            throw new InvalidOperationException("writing is not allowed once the writer is completed");
        }

        if (_exception is not null)
        {
            throw ExceptionUtil.Throw(_exception);
        }

        using CancellationTokenRegistration _ = cancellationToken.UnsafeRegister(
            cts => ((CancellationTokenSource)cts!).Cancel(),
            _abortCts);

        ReadResult readResult = default;

        // Make sure the pipe reader is not completed by Abort while it's being used.
        if (!_state.TrySetFlag(State.PipeReaderInUse))
        {
            throw new InvalidOperationException($"{nameof(WriteAsync)} is not thread safe");
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
                bool tryReadOk = _pipe.Reader.TryRead(out readResult);
                Debug.Assert(tryReadOk);
                Debug.Assert(!readResult.IsCanceled && !readResult.IsCompleted && readResult.Buffer.Length > 0);

                try
                {
                    // Write buffered data to the stream
                    await WriteSequenceAsync(readResult.Buffer, completeWrites: endStream && source.IsEmpty)
                        .ConfigureAwait(false);
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

            await WriteSequenceAsync(source, completeWrites: endStream).ConfigureAwait(false);
            return new FlushResult(isCanceled: false, isCompleted: endStream);
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Aborted
            Debug.Assert(_exception is not null);
            throw ExceptionUtil.Throw(_exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TransportException(TransportErrorCode.Unspecified, exception);
        }
        finally
        {
            if (_state.HasFlag(State.PipeReaderCompleted))
            {
                // If the pipe reader has been completed while we were writing the stream data, we make sure to
                // complete the reader now since Complete or Abort didn't do it.
                await _pipe.Reader.CompleteAsync(_exception).ConfigureAwait(false);
            }
            _state.ClearFlag(State.PipeReaderInUse);
        }

        Task WriteSequenceAsync(ReadOnlySequence<byte> sequence, bool completeWrites)
        {
            return sequence.IsSingleSegment ?
                WriteBufferAsync(sequence.First, completeWrites) : PerformWriteSequenceAsync();

            async Task PerformWriteSequenceAsync()
            {
                var enumerator = new ReadOnlySequence<byte>.Enumerator(sequence);
                bool hasMore = enumerator.MoveNext();
                Debug.Assert(hasMore);
                do
                {
                    ReadOnlyMemory<byte> buffer = enumerator.Current;
                    hasMore = enumerator.MoveNext();
                    await WriteBufferAsync(buffer, completeWrites: completeWrites && !hasMore).ConfigureAwait(false);
                }
                while (hasMore);
            }

            // We don't cancel QuicStream.WriteAsync since its cancellation aborts the stream reads under the hood. See
            // https://github.com/dotnet/runtime/issues/72607
            Task WriteBufferAsync(ReadOnlyMemory<byte> buffer, bool completeWrites)
            {
                _abortCts.Token.ThrowIfCancellationRequested();

                // TODO: add support for ValueTask.WaitAsync
                return _stream.WriteAsync(buffer, completeWrites, CancellationToken.None).AsTask().WaitAsync(
                    _abortCts.Token);
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
    }

    internal void Abort(Exception? exception)
    {
        Interlocked.CompareExchange(ref _exception, exception, null);

        if (_state.TrySetFlag(State.PipeReaderCompleted))
        {
            _abortCts.Cancel();
            if (!_state.HasFlag(State.PipeReaderInUse))
            {
                _pipe.Reader.Complete(exception);
            }
        }
    }

    /// <summary>The state enumeration is used to ensure the writer is not used after it's completed and to ensure
    /// that the internal pipe reader isn't completed concurrently when it's being used by WriteAsync.</summary>
    private enum State : int
    {
        /// <summary><see cref="Complete" /> was called on this Slic pipe writer.</summary>
        Completed = 1,

        /// <summary>Data is being read from the internal pipe reader.</summary>
        PipeReaderInUse = 2,

        /// <summary>The internal pipe reader was completed either by <see cref="Abort" />.</summary>
        PipeReaderCompleted = 4
    }
}
