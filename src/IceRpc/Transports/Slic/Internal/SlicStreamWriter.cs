// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Slic.Internal;

// Type owns disposable field(s) '_completeWritesCts' and '_sendCreditSemaphore' but is not disposable
#pragma warning disable CA1001
internal class SlicStreamWriter : IMultiplexedStreamWriter
#pragma warning restore CA1001
{
    public long UnflushedBytes => _pipe.Writer.UnflushedBytes;

    // We can avoid disposing _completeWritesCts because it was not created using CreateLinkedTokenSource, and it
    // doesn't use a timer. It is not easy to dispose it because CompleteWrites can be called by another thread after
    // Abort has been called.
    private readonly CancellationTokenSource _completeWritesCts = new();
    private Exception? _exception;
    private bool _isCompleted;
    // Set by a WriteAsync call with endStream: no further write is allowed.
    private bool _isEnded;
    private volatile int _peerWindowSize;
    private readonly Pipe _pipe;
    // The semaphore is used when flow control is enabled to wait for additional send credit to be available.
    private readonly SemaphoreSlim _sendCreditSemaphore = new(1, 1);
    private readonly SlicStream _stream;

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

            // Forcefully close the stream writes unless they were already gracefully closed by a WriteAsync call with
            // endStream set.
            _stream.AbortWrites();

            _pipe.Writer.Complete();
            _pipe.Reader.Complete();

            // Don't dispose the semaphore. It's not needed and we don't want to have to catch ObjectDisposedException
            // from AdjustPeerWindowSize if a StreamWindowUpdate is received after the application completed the stream
            // output. An alternative would be to add a lock but it's a bit overkill given than disposing the semaphore
            // is only useful when using SemaphoreSlim.AvailableWaitHandle.
            // _sendCreditSemaphore.Dispose();
        }
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        // Advance throws once the stream is ended, so no byte can be pending after an endStream write.
        Debug.Assert(!_isEnded || _pipe.Writer.UnflushedBytes == 0);

        if (!_isCompleted)
        {
            // The StreamLast frame is needed when the peer hasn't closed its reads yet and the stream is started or
            // has unflushed bytes; an unstarted stream with nothing to send is unknown to the peer.
            if (!_isEnded &&
                (_pipe.Writer.UnflushedBytes > 0 || (_stream.IsStarted && !_stream.WritesClosed.IsCompleted)))
            {
                try
                {
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

            // Closes the writes of an unstarted stream, or abruptly after a canceled write; otherwise, writes are already
            // closed or a StreamLast frame is queued.
            _stream.AbortWrites();

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

        if (_pipe.Writer.UnflushedBytes > 0)
        {
            await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _stream.ThrowIfConnectionClosed();

        // Abort the stream if the invocation is canceled.
        using CancellationTokenRegistration cancelTokenRegistration = cancellationToken.UnsafeRegister(
            cts => ((CancellationTokenSource)cts!).Cancel(),
            _completeWritesCts);

        ReadOnlySequence<byte> source1;
        ReadOnlySequence<byte> source2;
        if (_pipe.Reader.TryRead(out ReadResult readResult))
        {
            Debug.Assert(!readResult.IsCanceled && !readResult.IsCompleted && readResult.Buffer.Length > 0);
            source1 = readResult.Buffer;
            source2 = source;
        }
        else
        {
            source1 = source;
            source2 = ReadOnlySequence<byte>.Empty;
        }

        if (source1.IsEmpty && source2.IsEmpty && !endStream)
        {
            // Nothing to send; a payload writer such as the compressor can call WriteAsync with an empty buffer to
            // flush.
            return WriteResult.Written;
        }

        try
        {
            await _stream.WriteStreamFrameAsync(
                source1,
                source2,
                endStream,
                _completeWritesCts.Token).ConfigureAwait(false);
            return WriteResult.Written;
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _exception is null ? WriteResult.WritesClosed : throw ExceptionUtil.Throw(_exception);
        }
        finally
        {
            if (readResult.Buffer.Length > 0)
            {
                _pipe.Reader.AdvanceTo(readResult.Buffer.End);

                // Make sure there's no more data to consume from the pipe.
                Debug.Assert(!_pipe.Reader.TryRead(out ReadResult _));
            }
        }
    }

    internal SlicStreamWriter(SlicStream stream, SlicConnection connection)
    {
        _stream = stream;
        _peerWindowSize = connection.PeerInitialStreamWindowSize;

        // Create a pipe that never pauses on flush or write. The SlicStreamWriter will pause the write if the Slic
        // flow control doesn't permit sending more data.
        // The readerScheduler doesn't matter (we don't call _pipe.Reader.ReadAsync) and the writerScheduler doesn't
        // matter (_pipe.Writer.FlushAsync never blocks).
        _pipe = new(new PipeOptions(
            pool: connection.Pool,
            minimumSegmentSize: connection.MinSegmentSize,
            pauseWriterThreshold: 0,
            useSynchronizationContext: false));
    }

    /// <summary>Acquires send credit.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The available send credit.</returns>
    /// <remarks>The send credit matches the size of the peer's flow-control window.</remarks>
    internal async ValueTask<int> AcquireSendCreditAsync(CancellationToken cancellationToken)
    {
        // Acquire the semaphore to ensure flow control allows sending additional data. It's important to acquire the
        // semaphore before checking the peer window size. The semaphore acquisition will block if we can't send
        // additional data (_peerWindowSize <= 0).
        await _sendCreditSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return _peerWindowSize;
    }

    /// <summary>Complete writes.</summary>
    /// <param name="exception">The exception that will be raised by <see cref="WriteAsync" />.</param>
    internal void CompleteWrites(Exception? exception)
    {
        Interlocked.CompareExchange(ref _exception, exception, null);
        _completeWritesCts.Cancel();
    }

    /// <summary>Notifies the writer of the amount of send credit consumed by the sending of a stream frame.</summary>
    /// <param name="size">The size of the stream frame.</param>
    internal void ConsumedSendCredit(int size)
    {
        Debug.Assert(_sendCreditSemaphore.CurrentCount == 0); // Can only be called with the semaphore acquired.

        // Release the semaphore if the peer's window size is still superior to 0
        int newPeerWindowSize = Interlocked.Add(ref _peerWindowSize, -size);
        if (newPeerWindowSize > 0)
        {
            _sendCreditSemaphore.Release();
        }
    }

    /// <summary>Notifies the writer of the reception of a <see cref="FrameType.StreamWindowUpdate" /> frame.</summary>
    /// <param name="size">The window size increment.</param>
    internal void ReceivedWindowUpdateFrame(int size)
    {
        Debug.Assert(size > 0);

        // With _peerWindowSize >= 0 and size > 0, the atomic sum is positive unless it overflows int.MaxValue
        // (MaxWindowSize), in which case it wraps to a non-positive value.
        int newPeerWindowSize = Interlocked.Add(ref _peerWindowSize, size);
        if (newPeerWindowSize <= 0)
        {
            throw new InvalidDataException(
                "The window update is trying to increase the window size to a value larger than allowed.");
        }

        int previousPeerWindowSize = newPeerWindowSize - size;

        // A zero peer window size indicates that the last write consumed all the send credit and as a result didn't
        // release the semaphore. We can now release the semaphore to allow another write to send data.
        if (previousPeerWindowSize == 0)
        {
            Debug.Assert(_sendCreditSemaphore.CurrentCount == 0);
            _sendCreditSemaphore.Release();
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
