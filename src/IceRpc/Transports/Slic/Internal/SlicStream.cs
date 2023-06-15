// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Slic.Internal;

/// <summary>The stream implementation for Slic.</summary>
/// <remarks>The stream implementation implements flow control to ensure data isn't buffered indefinitely if the
/// application doesn't consume it.</remarks>
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

    /// <summary>Acquires send credit. This method should be called to ensure credit is available to send a stream
    /// frame. If no send credit is available, it will block until send credit is available.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The available send credit.</returns>
    internal ValueTask<int> AcquireSendCreditAsync(CancellationToken cancellationToken) =>
        _outputPipeWriter!.AcquireSendCreditAsync(cancellationToken);

    /// <summary>Closes the read and write sides of the stream and notifies the stream <see cref="Input" /> and <see
    /// cref="Output" /> of the reads and writes closure.</summary>
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

    /// <summary>Closes the read-side of the stream. It's only called by <see cref="SlicPipeReader.Complete" />, <see
    /// cref="SlicPipeReader.TryRead" /> or <see cref="SlicPipeReader.ReadAsync" /> and never called concurrently.
    /// </summary>
    /// <param name="graceful"><see langword="true" /> if the application consumed all the stream data from the stream
    /// <see cref="Input" />; otherwise, <see langword="false" />.</param>
    internal void CloseReads(bool graceful)
    {
        bool writeReadsClosedFrame = false;

        lock (_mutex)
        {
            if (IsStarted && !_state.HasFlag(State.ReadsClosed))
            {
                // As an optimization, if reads are gracefully closed once the buffered data is consumed but before
                // writes are closed, we don't send the StreamReadsClosed frame just yet. Instead, when writes are
                // closed, CloseWrites will bundle the sending of the StreamReadsClosed with the sending of the
                // StreamLast or StreamWritesClosed frame. This allows to send both frames with a single write on the
                // duplex connection.
                if (graceful &&
                    IsBidirectional &&
                    IsRemote &&
                    !_state.HasFlag(State.WritesClosed) &&
                    !_writesClosePending)
                {
                    _closeReadsOnWritesClosure = true;
                }
                else if (!graceful || IsRemote)
                {
                    // If forcefully closed because the input was completed before the data was fully read or if writes
                    // are already closed and the stream is a remote stream, we send the StreamReadsClosed frame to
                    // notify the peer that reads are closed.
                    writeReadsClosedFrame = true;
                }
            }
        }

        if (writeReadsClosedFrame)
        {
            _ = WriteReadsClosedFrameAsync();
        }
        else
        {
            TrySetReadsClosed();
        }

        async Task WriteReadsClosedFrameAsync()
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

                await _connection.WriteStreamFrameAsync(
                    stream: this,
                    FrameType.StreamReadsClosed,
                    encode: null,
                    writeReadsClosedFrame: false).ConfigureAwait(false);

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
                Debug.Fail($"Writing of StreamReadsClosed frame failed due to an unhandled exception: {exception}");
                throw;
            }
        }
    }

    /// <summary>Closes the write-side of the stream. It's only called by <see cref="SlicPipeWriter.Complete" /> and
    /// never called concurrently.</summary>
    /// <param name="graceful"><see langword="true" /> if the application wrote all the stream data on the stream <see
    /// cref="Output" />; otherwise, <see langword="false" />.</param>
    internal void CloseWrites(bool graceful)
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
            _ = WriteWritesClosedFrameAsync();
        }
        else
        {
            TrySetWritesClosed();
        }

        async Task WriteWritesClosedFrameAsync()
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

                if (graceful)
                {
                    await _connection.WriteStreamFrameAsync(
                        this,
                        FrameType.StreamLast,
                        encode: null,
                        writeReadsClosedFrame).ConfigureAwait(false);

                    // If the stream is a local stream, writes are not closed until the StreamReadsClosed frame is
                    // received from the peer (see ReceivedReadsClosedFrame). This ensures that the connection's
                    // bidirectional or unidirectional stream semaphore is released only once the peer consumed the
                    // buffered data.
                }
                else
                {
                    await _connection.WriteStreamFrameAsync(
                        stream: this,
                        FrameType.StreamWritesClosed,
                        encode: null,
                        writeReadsClosedFrame).ConfigureAwait(false);

                    if (!IsRemote)
                    {
                        // We can now close writes to allow starting a new stream. Since the sending of frames is
                        // serialized over the connection, the peer will receive this StreamWritesClosed frame before
                        // a new stream sends a StreamFrame frame.
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
                Debug.Fail($"Writing of StreamWritesClosed frame failed due to an unhandled exception: {exception}");
                throw;
            }
        }
    }

    /// <summary>Notifies the stream of the amount of data consumed by the connection to send a stream frame.</summary>
    /// <param name="size">The size of the stream frame.</param>
    internal void ConsumedSendCredit(int size) => _outputPipeWriter!.ConsumedSendCredit(size);

    /// <summary>Fills the given writer with stream data received on the connection.</summary>
    /// <param name="bufferWriter">The destination buffer writer.</param>
    /// <param name="byteCount">The amount of stream data to read.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    internal ValueTask FillBufferWriterAsync(
        IBufferWriter<byte> bufferWriter,
        int byteCount,
        CancellationToken cancellationToken) =>
        _connection.FillBufferWriterAsync(bufferWriter, byteCount, cancellationToken);

    /// <summary>Notifies the stream of the reception of a <see cref="FrameType.StreamConsumed" /> frame.</summary>
    /// <param name="frame">The body of the <see cref="FrameType.StreamConsumed" /> frame.</param>
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

    /// <summary>Notifies the stream of the reception of a <see cref="FrameType.StreamReadsClosed" /> frame.</summary>
    internal void ReceivedReadsClosedFrame()
    {
        TrySetWritesClosed();
        _outputPipeWriter?.CompleteWrites(exception: null);
    }

    /// <summary>Notifies the stream of the reception of a <see cref="FrameType.Stream" /> or <see
    /// cref="FrameType.StreamLast" /> frame.</summary>
    /// <param name="size">The size of the data carried by the stream frame.</param>
    /// <param name="endStream"><see langword="true" /> if the received stream frame is the <see
    /// cref="FrameType.StreamLast" /> frame; otherwise, <see langword="false" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    internal ValueTask<bool> ReceivedStreamFrameAsync(int size, bool endStream, CancellationToken cancellationToken)
    {
        Debug.Assert(_inputPipeReader is not null);
        return _state.HasFlag(State.ReadsClosed) ?
            new(false) :
            _inputPipeReader.ReceivedStreamFrameAsync(size, endStream, cancellationToken);
    }

    /// <summary>Notifies the stream of the reception of a <see cref="FrameType.StreamWritesClosed" /> frame.</summary>
    internal void ReceivedWritesClosedFrame()
    {
        TrySetReadsClosed();

        // Read operations will return a TruncatedData error if the peer closed writes.
        _inputPipeReader?.CompleteReads(new IceRpcException(IceRpcError.TruncatedData));
    }

    /// <summary>Writes a <see cref="FrameType.StreamConsumed" /> frame on the connection.</summary>
    /// <param name="size">The amount of data consumed by the application on the stream <see cref="Input" />.</param>
    internal void WriteStreamConsumedFrame(int size)
    {
        _ = WriteStreamConsumedFrame();

        async Task WriteStreamConsumedFrame()
        {
            try
            {
                // Send the stream consumed frame.
                await _connection.WriteStreamFrameAsync(
                    stream: this,
                    FrameType.StreamConsumed,
                    new StreamConsumedBody((ulong)size).Encode,
                    writeReadsClosedFrame: false).ConfigureAwait(false);
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail($"Writing of the StreamConsumed frame failed due to an unhandled exception: {exception}");
                throw;
            }
        }
    }

    /// <summary>Writes a <see cref="FrameType.Stream" /> or <see cref="FrameType.StreamLast" /> frame on the
    /// connection.</summary>
    /// <param name="source1">The first stream frame data source.</param>
    /// <param name="source2">The second stream frame data source.</param>
    /// <param name="endStream"><see langword="true" /> to write a <see cref="FrameType.StreamLast" /> frame; otherwise,
    /// <see langword="false" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
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

    /// <summary>Notifies the stream that the <see cref="FrameType.StreamLast" /> was written by the
    /// connection.</summary>
    internal void WroteLastStreamFrame()
    {
        if (IsRemote)
        {
            TrySetWritesClosed();
        }
        // For local streams, writes will be closed only once the peer sends the StreamReadsClosed frame.

        _writesClosedTcs.TrySetResult();
    }

    /// <summary>Throws the connection closure exception if the connection is closed.</summary>
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
