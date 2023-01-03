// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

/// <summary>The stream implementation for Slic. The stream implementation implements flow control to ensure data
/// isn't buffered indefinitely if the application doesn't consume it. Buffering and flow control are only enable
/// when sending multiple Slic packet or if the Slic packet size exceeds the peer packet maximum size.</summary>
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

    public Task ReadsClosed => _readsClosedTcs.Task;

    public Task WritesClosed => _writesClosedTcs.Task;

    internal bool ReadsCompleted => _state.HasFlag(State.ReadsCompleted);

    internal bool WritesCompleted => _state.HasFlag(State.WritesCompleted);

    private readonly SlicConnection _connection;
    private ulong _id = ulong.MaxValue;
    private readonly SlicPipeReader? _inputPipeReader;
    private readonly SlicPipeWriter? _outputPipeWriter;
    private readonly TaskCompletionSource _readsClosedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _state;
    private readonly TaskCompletionSource _writesClosedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal SlicStream(SlicConnection connection, bool bidirectional, bool remote)
    {
        _connection = connection;

        IsBidirectional = bidirectional;
        IsRemote = remote;

        if (!IsBidirectional)
        {
            if (IsRemote)
            {
                // Write-side of remote unidirectional stream is marked as completed.
                TrySetWritesCompleted();
            }
            else
            {
                // Read-side of local unidirectional stream is marked as completed.
                TrySetReadsCompleted();
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

    internal void Abort(IceRpcException completeException)
    {
        // Console.Error.WriteLine($"stream aborted {(IsStarted ? Id : -1)}-{IsRemote} ReadsCompleted={ReadsCompleted} WriteCompleted={WritesCompleted} {completeException.IceRpcError}");
        if (TrySetReadsCompleted())
        {
            Debug.Assert(_inputPipeReader is not null);
            _inputPipeReader.Abort(completeException);
        }
        if (TrySetWritesCompleted())
        {
            Debug.Assert(_outputPipeWriter is not null);
            _outputPipeWriter.Abort(completeException);
        }
    }

    internal ValueTask<int> AcquireSendCreditAsync(CancellationToken cancellationToken) =>
        _outputPipeWriter!.AcquireSendCreditAsync(cancellationToken);

    internal void CompleteReads(ulong? errorCode = null)
    {
        // Console.Error.WriteLine($"complete reads {(IsStarted ? Id : -1)}-{IsRemote} IsStarted={IsStarted} ReadsCompleted={ReadsCompleted} {errorCode}");
        if (!ReadsCompleted)
        {
            if (IsStarted && (errorCode is not null || IsRemote))
            {
                _ = SendCompleteReadsFrameAsync(errorCode);
            }
            else
            {
                TrySetReadsCompleted();
            }

            _readsClosedTcs.TrySetResult();
        }

        async Task SendCompleteReadsFrameAsync(ulong? errorCode)
        {
            try
            {
                if (IsRemote)
                {
                    // If it's a remote stream, we complete reads before sending the reads completed or stop sending
                    // frame. This ensures that the stream max count is decreased before the peer receives the frame.
                    TrySetReadsCompleted();
                }

                if (errorCode is not null)
                {
                    // Console.Error.WriteLine($"sending stop sending frame {(IsStarted ? Id : -1)}-{IsRemote} IsStarted={IsStarted} ReadsCompleted={ReadsCompleted} WritesCompleted={WritesCompleted}");
                    await _connection.SendStreamFrameAsync(
                        stream: this,
                        FrameType.StreamStopSending,
                        new StreamStopSendingBody(errorCode.Value).Encode,
                        default).ConfigureAwait(false);
                }
                else if (IsRemote)
                {
                    // The stream reads completed frame is only sent for remote streams.
                    // Console.Error.WriteLine($"sending reads completed frame {(IsStarted ? Id : -1)}-{IsRemote} IsStarted={IsStarted} ReadsCompleted={ReadsCompleted} WritesCompleted={WritesCompleted}");
                    await _connection.SendStreamFrameAsync(
                        stream: this,
                        FrameType.StreamReadsCompleted,
                        encode: null,
                        default).ConfigureAwait(false);
                }
                // When completing reads for a local stream, there's no need to notify the peer. The peer already
                // completed writes writes after sending the last stream frame.

                if (!IsRemote)
                {
                    // We can now complete reads to permit a new stream to be started. The peer will receive the stop
                    // sending or reads completed frame before the new stream sends a stream frame.
                    TrySetReadsCompleted();
                }
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail(
                    $"Failed to send Slic stream complete reads frame due to an unhandled exception: {exception}");
                throw;
            }
        }
    }

    internal void CompleteWrites(ulong? errorCode = null)
    {
        // Console.Error.WriteLine($"complete writes {(IsStarted ? Id : -1)}-{IsRemote} IsStarted={IsStarted} WritesCompleted={WritesCompleted} {errorCode}");
        if (!WritesCompleted)
        {
            if (IsStarted)
            {
                _ = SendCompleteWritesFrameAsync(errorCode);
            }
            else
            {
                TrySetWritesCompleted();
            }

            _writesClosedTcs.TrySetResult();
        }

        async Task SendCompleteWritesFrameAsync(ulong? errorCode)
        {
            try
            {
                if (IsRemote)
                {
                    // If it's a remote stream, we complete writes before sending the stream last frame or reset frame
                    // to ensure the stream max count is decreased before the peer receives the frame.
                    TrySetWritesCompleted();
                }

                if (errorCode is null)
                {
                    // Console.Error.WriteLine($"sending last stream frame {(IsStarted ? Id : -1)}-{IsRemote} IsStarted={IsStarted} ReadsCompleted={ReadsCompleted} WritesCompleted={WritesCompleted} SendCredit={_sendCredit}");
                    await _connection.SendStreamFrameAsync(
                        this,
                        ReadOnlySequence<byte>.Empty,
                        ReadOnlySequence<byte>.Empty,
                        endStream: true,
                        default).ConfigureAwait(false);
                }
                else
                {
                    // Console.Error.WriteLine($"sending reset frame {(IsStarted ? Id : -1)}-{IsRemote} IsStarted={IsStarted} ReadsCompleted={ReadsCompleted} WritesCompleted={WritesCompleted}");
                    await _connection.SendStreamFrameAsync(
                        stream: this,
                        FrameType.StreamReset,
                        new StreamResetBody(applicationErrorCode: 0).Encode,
                        default).ConfigureAwait(false);

                    if (!IsRemote)
                    {
                        // We can now complete writes to permit a new stream to be started. The peer will receive the
                        // last stream frame or reset frame before the new stream sends a stream frame.
                        TrySetWritesCompleted();
                    }
                }
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail(
                    $"Failed to send Slic stream complete writes frame due to an unhandled exception: {exception}");
                throw;
            }
        }
    }

    internal void ConsumedSendCredit(int consumed) => _outputPipeWriter!.ConsumedSendCredit(consumed);

    internal ValueTask FillBufferWriterAsync(
        IBufferWriter<byte> bufferWriter,
        int byteCount,
        CancellationToken cancellationToken) =>
        _connection.FillBufferWriterAsync(bufferWriter, byteCount, cancellationToken);

    internal void ReceivedConsumedFrame(int size)
    {
        int newSendCredit = _outputPipeWriter!.ReceivedConsumedFrame(size);

        // Ensure the peer is not trying to increase the credit to a value which is larger than what it is allowed to.
        if (newSendCredit > _connection.PeerPauseWriterThreshold)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "The consumed frame size is trying to increase the credit to a value larger than allowed.");
        }
    }

    internal void ReceivedReadsCompletedFrame()
    {
        // Console.Error.WriteLine($"received reads completed frame {(IsStarted ? Id : -1)}-{IsRemote} WritesCompleted={WritesCompleted}");

        if (IsRemote)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "Received invalid Slic stream reads completed frame, the stream is a remote stream.");
        }

        TrySetWritesCompleted();

        // Ensure writes operations are completed first. Write operations will return a completed flush result
        // regardless of wether or not the peer aborted reads with the 0ul error code or completed reads.
        _outputPipeWriter?.Abort(exception: null);
    }

    internal void ReceivedResetFrame(ulong errorCode)
    {
        // Console.Error.WriteLine($"received reset frame {(IsStarted ? Id : -1)}-{IsRemote} ReadsCompleted={ReadsCompleted} {errorCode}");

        TrySetReadsCompleted();

        if (errorCode == 0ul)
        {
            // Read operations will return a TruncatedData if the peer aborted writes.
            _inputPipeReader?.Abort(new IceRpcException(IceRpcError.TruncatedData));
        }
        else
        {
            // The peer aborted writes with unknown application error code.
            _inputPipeReader?.Abort(new IceRpcException(
                IceRpcError.IceRpcError,
                $"The peer aborted stream writes with an unknown application error code: '{errorCode}'"));
        }
    }

    internal void ReceivedStopSendingFrame(ulong errorCode)
    {
        // Console.Error.WriteLine($"received stop sending frame {(IsStarted ? Id : -1)}-{IsRemote} WritesCompleted={WritesCompleted} {errorCode}");

        TrySetWritesCompleted();

        if (errorCode == 0ul)
        {
            // Write operations will return a completed flush result regardless of wether or not the peer aborted
            // reads with the 0ul error code or completed reads.
            _outputPipeWriter?.Abort(exception: null);
        }
        else
        {
            // The peer aborted reads with unknown application error code.
            _outputPipeWriter?.Abort(new IceRpcException(
                IceRpcError.IceRpcError,
                $"The peer aborted stream reads with an unknown application error code: '{errorCode}'"));
        }
    }

    internal ValueTask<int> ReceivedStreamFrameAsync(int size, bool endStream, CancellationToken cancellationToken)
    {
        // Console.Error.WriteLine($"received stream frame {(IsStarted ? Id : -1)}-{IsRemote} WritesCompleted={WritesCompleted} ReadsCompleted={ReadsCompleted} Size={size} EndStream={endStream}");

        Debug.Assert(_inputPipeReader is not null);
        return ReadsCompleted ? new(0) : _inputPipeReader.ReceivedStreamFrameAsync(size, endStream, cancellationToken);
    }

    internal void SendStreamConsumed(int size)
    {
        _ = SendStreamConsumedFrameAsync();

        async Task SendStreamConsumedFrameAsync()
        {
            try
            {
                // Send the stream consumed frame.
                await _connection.SendStreamFrameAsync(
                    stream: this,
                    FrameType.StreamConsumed,
                    new StreamConsumedBody((ulong)size).Encode,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (IceRpcException)
            {
                // Ignore connection failures.
            }
            catch (Exception exception)
            {
                Debug.Fail($"Sending of Slic stream consumed frame failed due to an unhandled exception: {exception}");
                throw;
            }
        }
    }

    internal ValueTask<FlushResult> SendStreamFrameAsync(
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        bool endStream,
        CancellationToken cancellationToken)
    {
        // Console.Error.WriteLine($"sending stream frame {(IsStarted ? Id : -1)}-{IsRemote} IsStarted={IsStarted} ReadsCompleted={ReadsCompleted} WritesCompleted={WritesCompleted} Length={source1.Length} EndStream={endStream} CREDIT={_sendCredit}");
        return _connection.SendStreamFrameAsync(this, source1, source2, endStream, cancellationToken);
    }

    internal void SentLastStreamFrame()
    {
        if (IsRemote)
        {
            TrySetWritesCompleted();
        }
        // Writes will be completed when the peer's sends the stop sending or reads completed frame.

        _writesClosedTcs.TrySetResult();
    }

    internal bool TrySetReadsCompleted()
    {
        if (TrySetState(State.ReadsCompleted))
        {
            _readsClosedTcs.TrySetResult();
            return true;
        }
        else
        {
            return false;
        }
    }

    internal bool TrySetWritesCompleted()
    {
        if (TrySetState(State.WritesCompleted))
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
            if ((state.HasFlag(State.ReadsCompleted) || state.HasFlag(State.WritesCompleted)) &&
                newState.HasFlag(State.ReadsCompleted | State.WritesCompleted))
            {
                // The stream reads and writes are completed, it's time to release the stream.
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
        ReadsCompleted = 1,
        WritesCompleted = 2,
    }
}
