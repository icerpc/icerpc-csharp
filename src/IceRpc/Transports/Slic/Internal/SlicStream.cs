// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Slic.Internal;

/// <summary>The stream implementation for Slic. The stream implementation implements flow control to ensure data
/// isn't buffered indefinitely if the application doesn't consume it.</summary>
internal class SlicStream : IMultiplexedStream
{
    public ulong Id
    {
        get
        {
            ulong id = Thread.VolatileRead(ref _id);
            if (id == ulong.MaxValue)
            {
                throw new InvalidOperationException("The stream ID isn't allocated yet.");
            }
            return id;
        }

        set
        {
            Debug.Assert(_id == ulong.MaxValue);
            Thread.VolatileWrite(ref _id, value);
        }
    }

    public PipeReader Input =>
        _inputPipeReader ?? throw new InvalidOperationException("A local unidirectional stream has no Input.");

    /// <inheritdoc/>
    public bool IsBidirectional { get; }

    /// <inheritdoc/>
    public bool IsRemote { get; }

    /// <inheritdoc/>
    public bool IsStarted => Thread.VolatileRead(ref _id) != ulong.MaxValue;

    public PipeWriter Output =>
        _outputPipeWriter ?? throw new InvalidOperationException("A remote unidirectional stream has no Output.");

    public Task WritesClosed => _writesClosedTcs.Task;

    private bool _closeReadsOnWritesClosure;
    private readonly SlicConnection _connection;
    private ulong _id = ulong.MaxValue;
    private readonly SlicPipeReader? _inputPipeReader;
    // This mutex protects _writesClosePending, _closeReadsOnWritesClosure.
    private readonly object _mutex = new();
    private readonly SlicPipeWriter? _outputPipeWriter;
    // FlagEnumExtensions operations are used to update the state. These operations are atomic and don't require mutex
    // locking.
    private int _state;
    private readonly TaskCompletionSource _writesClosedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _writesClosePending;

    internal SlicStream(SlicConnection connection, bool bidirectional, bool remote)
    {
        _connection = connection;

        IsBidirectional = bidirectional;
        IsRemote = remote;

        if (!IsBidirectional)
        {
            if (IsRemote)
            {
                // Write-side of remote unidirectional stream is marked as closed.
                TrySetWritesClosed();
            }
            else
            {
                // Read-side of local unidirectional stream is marked as closed.
                TrySetReadsClosed();
            }
        }

        if (IsRemote || IsBidirectional)
        {
            _inputPipeReader = new SlicPipeReader(this, _connection);
        }

        if (!IsRemote || IsBidirectional)
        {
            _outputPipeWriter = new SlicPipeWriter(this, _connection);
        }
    }

    internal ValueTask<int> AcquireSendCreditAsync(CancellationToken cancellationToken) =>
        _outputPipeWriter!.AcquireSendCreditAsync(cancellationToken);

    internal void Close(Exception closeException)
    {
        if (TrySetReadsClosed())
        {
            Debug.Assert(_inputPipeReader is not null);
            _inputPipeReader.CompleteReads(closeException);
        }
        if (TrySetWritesClosed())
        {
            Debug.Assert(_outputPipeWriter is not null);
            _outputPipeWriter.CompleteWrites(closeException);
        }
    }

    /// <summary>Closes the read-side of the stream. It's only called by SlicPipeReader methods and never called
    /// concurrently.</summary>
    /// <param name="errorCode">The error code. It's null if reads were closed after the StreamLast frame was consumed.
    /// It's non-null if the input was completed with an exception or before the buffer data was consumed.</param>
    internal void CloseReads(ulong? errorCode = null)
    {
        bool writeReadsClosedFrame = false;

        lock (_mutex)
        {
            if (IsStarted && !_state.HasFlag(State.ReadsClosed))
            {
                if (errorCode is null &&
                    IsBidirectional &&
                    IsRemote &&
                    !_state.HasFlag(State.WritesClosed) &&
                    !_writesClosePending)
                {
                    // As an optimization, if reads are closed once the buffered data is consumed but before writes are
                    // closed, we don't send the StreamReadsClosed frame just yet. Instead, when writes are closed,
                    // CloseWrites will bundle the sending of the StreamReadsClosed with the sending of the StreamLast
                    // or StreamWritesClosed frame.
                    _closeReadsOnWritesClosure = true;
                }
                else if (errorCode is not null || IsRemote)
                {
                    writeReadsClosedFrame = true;
                }
            }
        }

        if (writeReadsClosedFrame)
        {
            try
            {
                if (IsRemote)
                {
                    // If it's a remote stream, we close writes before sending the StreamReadsClosed frame to ensure
                    // _connection._bidirectionalStreamCount or _connection._unidirectionalStreamCount is decreased
                    // before the peer receives the frame. This is necessary to prevent a race condition where the peer
                    // could release the connection's bidirectional or unidirectional stream semaphore before this
                    // connection's stream count is actually decreased.
                    TrySetReadsClosed();
                }

                // The stream reads closed frame is only sent for remote streams to notify the local stream that the
                // buffered data on the SlicPipeReader was consumed. Once the peer receives this notification, it can
                // release the connection's bidirectional or unidirectional stream semaphore (if writes are also
                // closed).
                if (errorCode is not null || IsRemote)
                {
                    _connection.WriteStreamFrame(
                        stream: this,
                        FrameType.StreamReadsClosed,
                        new StreamReadsClosedBody(errorCode).Encode,
                        writeReadsClosedFrame: false);
                }
                // When closing reads for a local stream, there's no need to notify the peer. The peer already closed
                // writes after sending the StreamLast or StreamWritesClosed frame.

                if (!IsRemote)
                {
                    // We can now close reads to permit a new stream to be started. The peer will receive the
                    // StreamReadsClosed frame before the new stream sends a Stream frame.
                    TrySetReadsClosed();
                }
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail($"Writing of reads closed frame from due to an unhandled exception: {exception}");
                throw;
            }
        }
        else
        {
            TrySetReadsClosed();
        }
    }

    /// <summary>Closes the write-side of the stream. It's only called by SlicPipeWriter methods and never called
    /// concurrently.</summary>
    /// <param name="errorCode">The error code. It's null if the output was completed without an exception; otherwise,
    /// it's non-null.</param>
    internal void CloseWrites(ulong? errorCode = null)
    {
        bool writeWritesClosedFrame = false;
        bool writeReadsClosedFrame = false;

        lock (_mutex)
        {
            if (IsStarted && !_state.HasFlag(State.WritesClosed) && !_writesClosePending)
            {
                writeReadsClosedFrame = _closeReadsOnWritesClosure;
                _writesClosePending = true;
                writeWritesClosedFrame = true;
            }
        }

        if (writeWritesClosedFrame)
        {
            try
            {
                if (IsRemote)
                {
                    // If it's a remote stream, we close writes before sending the StreamLast or StreamWritesClosed
                    // frame to ensure _connection._bidirectionalStreamCount or _connection._unidirectionalStreamCount
                    // is decreased before the peer receives the frame. This is necessary to prevent a race condition
                    // where the peer could release the connection's bidirectional or unidirectional stream semaphore
                    // before this connection's stream count is actually decreased.
                    TrySetWritesClosed();
                }

                if (errorCode is null)
                {
                    _connection.WriteStreamFrame(this, FrameType.StreamLast, encode: null, writeReadsClosedFrame);

                    // If the stream is a local stream, writes are not close until the StreamReadsClosed frame is
                    // received from the peer. This ensures that the connection's bidirectional or unidirectional stream
                    // semaphore is released only once the peer consumed the buffered data.
                }
                else
                {
                    _connection.WriteStreamFrame(
                        stream: this,
                        FrameType.StreamWritesClosed,
                        new StreamWritesClosedBody(applicationErrorCode: errorCode.Value).Encode,
                        writeReadsClosedFrame);

                    if (!IsRemote)
                    {
                        // We can now close writes to allow starting a new stream. Since the sending of frames is
                        // serialized over the connection, the peer will receive this StreamWritesClosed frame before
                        // the new stream sends StreamFrame frame.
                        TrySetWritesClosed();
                    }
                }
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail($"Writing of the writes closed frame failed due to an unhandled exception: {exception}");
                throw;
            }
        }
        else
        {
            TrySetWritesClosed();
        }
    }

    internal void ConsumedSendCredit(int consumed) => _outputPipeWriter!.ConsumedSendCredit(consumed);

    internal ValueTask FillBufferWriterAsync(
        IBufferWriter<byte> bufferWriter,
        int byteCount,
        CancellationToken cancellationToken) =>
        _connection.FillBufferWriterAsync(bufferWriter, byteCount, cancellationToken);

    internal void ReceivedConsumedFrame(StreamConsumedBody frame)
    {
        int newSendCredit = _outputPipeWriter!.ReceivedConsumedFrame((int)frame.Size);

        // Ensure the peer is not trying to increase the credit to a value which is larger than what it is allowed to.
        if (newSendCredit > _connection.PeerPauseWriterThreshold)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "The consumed frame size is trying to increase the credit to a value larger than allowed.");
        }
    }

    internal void ReceivedReadsClosedFrame(StreamReadsClosedBody frame)
    {
        TrySetWritesClosed();

        if (frame.ApplicationErrorCode == 0ul)
        {
            // Write operations will return a completed flush result regardless of whether or not the peer aborted
            // reads with the 0ul error code or completed reads.
            _outputPipeWriter?.CompleteWrites(exception: null);
        }
        else
        {
            // The peer aborted reads with unknown application error code.
            _outputPipeWriter?.CompleteWrites(new IceRpcException(
                IceRpcError.TruncatedData,
                $"The peer closed stream reads with an unknown application error code: '{frame.ApplicationErrorCode}'"));
        }
    }

    internal ValueTask<bool> ReceivedStreamFrameAsync(int size, bool endStream, CancellationToken cancellationToken)
    {
        Debug.Assert(_inputPipeReader is not null);
        return _state.HasFlag(State.ReadsClosed) ?
            new(false) :
            _inputPipeReader.ReceivedStreamFrameAsync(size, endStream, cancellationToken);
    }

    internal void ReceivedWritesClosedFrame(StreamWritesClosedBody frame)
    {
        TrySetReadsClosed();

        if (frame.ApplicationErrorCode == 0ul)
        {
            // Read operations will return a TruncatedData if the peer aborted writes.
            _inputPipeReader?.CompleteReads(new IceRpcException(IceRpcError.TruncatedData));
        }
        else
        {
            // The peer aborted writes with unknown application error code.
            _inputPipeReader?.CompleteReads(new IceRpcException(
                IceRpcError.TruncatedData,
                $"The peer closed stream writes with an unknown application error code: '{frame.ApplicationErrorCode}'"));
        }
    }

    internal void WriteStreamConsumedFrame(int size)
    {
        try
        {
            // Send the stream consumed frame.
            _connection.WriteStreamFrame(
                stream: this,
                FrameType.StreamConsumed,
                new StreamConsumedBody((ulong)size).Encode,
                writeReadsClosedFrame: false);
        }
        catch (IceRpcException)
        {
            // Ignore connection failures.
        }
        catch (Exception exception)
        {
            Debug.Fail($"Writing of the stream consumed frame failed due to an unhandled exception: {exception}");
            throw;
        }
    }

    internal ValueTask<FlushResult> WriteStreamFrameAsync(
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        bool endStream,
        CancellationToken cancellationToken)
    {
        bool writeReadsClosedFrame = false;
        if (endStream)
        {
            lock (_mutex)
            {
                writeReadsClosedFrame = _closeReadsOnWritesClosure;
                _writesClosePending = true;
            }
        }

        return _connection.WriteStreamDataFrameAsync(
            this,
            source1,
            source2,
            endStream,
            writeReadsClosedFrame,
            cancellationToken);
    }

    internal void WroteLastStreamFrame()
    {
        if (IsRemote)
        {
            TrySetWritesClosed();
        }
        // For local streams, writes will be closed only once the peer's sends the StreamReadsClosed frame.

        _writesClosedTcs.TrySetResult();
    }

    internal void ThrowIfConnectionClosed() => _connection.ThrowIfClosed();

    private bool TrySetReadsClosed() => TrySetState(State.ReadsClosed);

    private bool TrySetWritesClosed()
    {
        if (TrySetState(State.WritesClosed))
        {
            _writesClosedTcs.TrySetResult();
            return true;
        }
        else
        {
            return false;
        }
    }

    private bool TrySetState(State state)
    {
        if (_state.TrySetFlag(state, out int newState))
        {
            if (newState.HasFlag(State.ReadsClosed | State.WritesClosed))
            {
                // The stream reads and writes are closed, it's time to release the stream to either allow creating or
                // accepting a new stream.
                if (IsStarted)
                {
                    _connection.ReleaseStream(this);
                }
            }
            return true;
        }
        else
        {
            return false;
        }
    }

    [Flags]
    private enum State : int
    {
        ReadsClosed = 1,
        WritesClosed = 2,
    }
}
