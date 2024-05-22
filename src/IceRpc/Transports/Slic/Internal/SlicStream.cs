// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using ZeroC.Slice;

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
            ulong id = Volatile.Read(ref _id);
            if (id == ulong.MaxValue)
            {
                throw new InvalidOperationException("The stream ID isn't allocated yet.");
            }
            return id;
        }

        set
        {
            Debug.Assert(_id == ulong.MaxValue);
            Volatile.Write(ref _id, value);
        }
    }

    public PipeReader Input =>
        _inputPipeReader ?? throw new InvalidOperationException("A local unidirectional stream has no Input.");

    /// <inheritdoc/>
    public bool IsBidirectional { get; }

    /// <inheritdoc/>
    public bool IsRemote { get; }

    /// <inheritdoc/>
    public bool IsStarted => Volatile.Read(ref _id) != ulong.MaxValue;

    public PipeWriter Output =>
        _outputPipeWriter ?? throw new InvalidOperationException("A remote unidirectional stream has no Output.");

    public Task WritesClosed => _writesClosedTcs.Task;

    internal int WindowUpdateThreshold => _connection.StreamWindowUpdateThreshold;

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

    internal SlicStream(SlicConnection connection, bool isBidirectional, bool isRemote)
    {
        _connection = connection;

        IsBidirectional = isBidirectional;
        IsRemote = isRemote;

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

    /// <summary>Acquires send credit.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The available send credit.</returns>
    /// <remarks>This method should be called before sending a <see cref="FrameType.Stream"/> or <see
    /// cref="FrameType.StreamLast"/> frame to ensure enough send credit is available. If no send credit is available,
    /// it will block until send credit is available. The send credit matches the size of the peer's flow-control
    /// window.</remarks>
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
            if (IsRemote)
            {
                // If it's a remote stream, we close writes before sending the StreamReadsClosed frame to ensure
                // _connection._bidirectionalStreamCount or _connection._unidirectionalStreamCount is decreased before
                // the peer receives the frame. This is necessary to prevent a race condition where the peer could
                // release the connection's bidirectional or unidirectional stream semaphore before this connection's
                // stream count is actually decreased.
                TrySetReadsClosed();
            }

            try
            {
                WriteStreamFrame(FrameType.StreamReadsClosed, encode: null, writeReadsClosedFrame: false);
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }

            if (!IsRemote)
            {
                // We can now close reads to permit a new stream to be started. The peer will receive the
                // StreamReadsClosed frame before the new stream sends a Stream frame.
                TrySetReadsClosed();
            }
        }
        else
        {
            TrySetReadsClosed();
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
                try
                {
                    WriteStreamFrame(FrameType.StreamLast, encode: null, writeReadsClosedFrame);
                }
                catch (IceRpcException)
                {
                    // Ignore connection failures.
                }

                // If the stream is a local stream, writes are not closed until the StreamReadsClosed frame is
                // received from the peer (see ReceivedReadsClosedFrame). This ensures that the connection's
                // bidirectional or unidirectional stream semaphore is released only once the peer consumed the
                // buffered data.
            }
            else
            {
                try
                {
                    WriteStreamFrame(FrameType.StreamWritesClosed, encode: null, writeReadsClosedFrame);
                }
                catch (IceRpcException)
                {
                    // Ignore connection failures.
                }

                if (!IsRemote)
                {
                    // We can now close writes to allow starting a new stream. Since the sending of frames is
                    // serialized over the connection, the peer will receive this StreamWritesClosed frame before
                    // a new stream sends a StreamFrame frame.
                    TrySetWritesClosed();
                }
            }
        }
        else
        {
            TrySetWritesClosed();
        }
    }

    /// <summary>Notifies the stream of the amount of data consumed by the connection to send a <see
    /// cref="FrameType.Stream" /> or <see cref="FrameType.StreamLast" /> frame.</summary>
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

    /// <summary>Notifies the stream of the reception of a <see cref="FrameType.Stream" /> or <see
    /// cref="FrameType.StreamLast" /> frame.</summary>
    /// <param name="size">The size of the data carried by the stream frame.</param>
    /// <param name="endStream"><see langword="true" /> if the received stream frame is the <see
    /// cref="FrameType.StreamLast" /> frame; otherwise, <see langword="false" />.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    internal ValueTask<bool> ReceivedDataFrameAsync(int size, bool endStream, CancellationToken cancellationToken)
    {
        Debug.Assert(_inputPipeReader is not null);
        if (_state.HasFlag(State.ReadsClosed))
        {
            return new(false);
        }
        else
        {
            if (endStream && !IsRemote)
            {
                // For a local stream we can close reads after we have received the StreamLast frame. For remote
                // streams reads are closed after the application has consumed all the data.
                CloseReads(graceful: true);
            }
            return _inputPipeReader.ReceivedDataFrameAsync(size, endStream, cancellationToken);
        }
    }

    /// <summary>Notifies the stream of the reception of a <see cref="FrameType.StreamReadsClosed" /> frame.</summary>
    internal void ReceivedReadsClosedFrame()
    {
        TrySetWritesClosed();
        _outputPipeWriter?.CompleteWrites(exception: null);
    }

    /// <summary>Notifies the stream of the reception of a <see cref="FrameType.StreamWindowUpdate" /> frame.</summary>
    /// <param name="frame">The body of the <see cref="FrameType.StreamWindowUpdate" /> frame.</param>
    internal void ReceivedWindowUpdateFrame(StreamWindowUpdateBody frame)
    {
        if (frame.WindowSizeIncrement > SlicTransportOptions.MaxWindowSize)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                $"The window update is trying to increase the window size to a value larger than allowed.");
        }
        _outputPipeWriter!.ReceivedWindowUpdateFrame((int)frame.WindowSizeIncrement);
    }

    /// <summary>Notifies the stream of the reception of a <see cref="FrameType.StreamWritesClosed" /> frame.</summary>
    internal void ReceivedWritesClosedFrame()
    {
        TrySetReadsClosed();

        // Read operations will return a TruncatedData error if the peer closed writes.
        _inputPipeReader?.CompleteReads(new IceRpcException(IceRpcError.TruncatedData));
    }

    /// <summary>Notifies the stream of the window update.</summary>
    /// <param name="size">The amount of data consumed by the application on the stream <see cref="Input" />.</param>
    internal void WindowUpdate(int size)
    {
        try
        {
            // Notify the sender of the window update to permit the sending of additional data.
            WriteStreamFrame(
                FrameType.StreamWindowUpdate,
                new StreamWindowUpdateBody((ulong)size).Encode,
                writeReadsClosedFrame: false);
        }
        catch (IceRpcException)
        {
            // Ignore connection failures.
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

    private void WriteStreamFrame(FrameType frameType, EncodeAction? encode, bool writeReadsClosedFrame) =>
        _connection.WriteStreamFrame(stream: this, frameType, encode, writeReadsClosedFrame);

    [Flags]
    private enum State : int
    {
        ReadsClosed = 1,
        WritesClosed = 2
    }
}
