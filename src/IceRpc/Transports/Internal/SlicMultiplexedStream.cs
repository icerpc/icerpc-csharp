// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The stream implementation for Slic. The stream implementation implements flow control to ensure data
    /// isn't buffered indefinitely if the application doesn't consume it. Buffering and flow control are only enable
    /// when sending multiple Slic packet or if the Slic packet size exceeds the peer packet maximum size.</summary>
    internal class SlicMultiplexedStream : IMultiplexedStream
    {
        /// <inheritdoc/>
        public long Id
        {
            get
            {
                long id = Thread.VolatileRead(ref _id);
                if (id == -1)
                {
                    throw new InvalidOperationException("stream ID isn't allocated yet");
                }
                return id;
            }
            set
            {
                Debug.Assert(_id == -1);
                Thread.VolatileWrite(ref _id, value);
            }
        }

        public PipeReader Input => IsRemote || IsBidirectional ?
            _inputPipeReader :
            throw new InvalidOperationException($"can't get {nameof(Input)} on unidirectional local stream");

        /// <inheritdoc/>
        public bool IsBidirectional { get; }

        /// <inheritdoc/>
        public bool IsStarted => Thread.VolatileRead(ref _id) != -1;

        /// <inheritdoc/>
        public Action? ShutdownAction
        {
            get
            {
                lock (_mutex)
                {
                    return _shutdownAction;
                }
            }
            set
            {
                bool alreadyShutdown = false;
                lock (_mutex)
                {
                    if (IsShutdown)
                    {
                        alreadyShutdown = true;
                    }
                    else
                    {
                        _shutdownAction = value;
                    }
                }
                if (alreadyShutdown)
                {
                    value?.Invoke();
                }
            }
        }

        public PipeWriter Output => !IsRemote || IsBidirectional ?
            _outputPipeWriter :
            throw new InvalidOperationException($"can't get {nameof(Output)} on unidirectional remote stream");

        /// <summary>Specifies whether or not this is a stream initiated by the peer.</summary>
        internal bool IsRemote => _id != -1 && _id % 2 == (_connection.IsServer ? 0 : 1);

        internal bool IsShutdown => ReadsCompleted && WritesCompleted;

        internal bool ReadsCompleted => _state.HasFlag(State.ReadCompleted);

        /// <summary>The stream reset error is set if the stream is reset by the peer or locally. It's used to set
        /// the error for the <see cref="MultiplexedStreamAbortedException"/> raised by the stream.</summary>
        internal long? ResetError
        {
            get
            {
                lock (_mutex)
                {
                    return _resetErrorCode;
                }
            }
            private set
            {
                lock (_mutex)
                {
                    _resetErrorCode = value;
                }
            }
        }

        internal bool WritesCompleted => _state.HasFlag(State.WriteCompleted);

        private readonly SlicNetworkConnection _connection;
        private readonly ISlicFrameReader _frameReader;
        private readonly ISlicFrameWriter _frameWriter;
        private long _id = -1;
        private readonly SlicPipeReader _inputPipeReader;
        private readonly object _mutex = new();
        private readonly SlicPipeWriter _outputPipeWriter;
        private volatile int _sendCredit = int.MaxValue;
        // The semaphore is used when flow control is enabled to wait for additional send credit to be available.
        private readonly AsyncSemaphore _sendCreditSemaphore = new(1, 1);
        private volatile Action? _shutdownAction;
        private TaskCompletionSource? _shutdownCompletedTaskSource;
        private long? _resetErrorCode;
        private int _state;

        public void AbortRead(long errorCode)
        {
            if (ReadsCompleted)
            {
                return;
            }

            ResetError = errorCode;

            if (IsStarted && !IsShutdown)
            {
                // Notify the peer of the read abort by sending a stop sending frame.
                _ = SendStopSendingFrameAndShutdownAsync();
            }
            else
            {
                TrySetReadCompleted();
            }

            async Task SendStopSendingFrameAndShutdownAsync()
            {
                try
                {
                    await _connection.SendFrameAsync(
                        stream: this,
                        FrameType.StreamStopSending,
                        new StreamStopSendingBody(errorCode).Encode,
                        default).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore.
                }
                TrySetReadCompleted();
            }
        }

        public void AbortWrite(long errorCode)
        {
            ResetError = errorCode;

            if (IsStarted && !IsShutdown)
            {
                // Notify the peer of the write abort by sending a reset frame.
                _ = SendResetFrameAndCompleteWritesAsync();
            }
            else
            {
                TrySetWriteCompleted();
            }

            async Task SendResetFrameAndCompleteWritesAsync()
            {
                try
                {
                    await _connection.SendFrameAsync(
                        stream: this,
                        FrameType.StreamReset,
                        new StreamResetBody(errorCode).Encode,
                        default).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore.
                }
                TrySetWriteCompleted();
            }
        }

        public async Task WaitForShutdownAsync(CancellationToken cancel)
        {
            lock (_mutex)
            {
                if (IsShutdown)
                {
                    return;
                }
                _shutdownCompletedTaskSource ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            await _shutdownCompletedTaskSource.Task.WaitAsync(cancel).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override string ToString() => $"{base.ToString()} (ID={Id})";

        internal SlicMultiplexedStream(
            SlicNetworkConnection connection,
            bool bidirectional,
            bool remote,
            ISlicFrameReader reader,
            ISlicFrameWriter writer)
        {
            _connection = connection;
            _sendCredit = _connection.PeerPauseWriterThreshold;

            _frameReader = reader;
            _frameWriter = writer;

            _inputPipeReader = new SlicPipeReader(
                this,
                _connection.Pool,
                _connection.MinimumSegmentSize,
                _connection.ResumeWriterThreshold,
                _connection.PauseWriterThreshold,
                _frameReader.ReadFrameDataAsync);

            _outputPipeWriter = new SlicPipeWriter(this, _connection.Pool, _connection.MinimumSegmentSize);

            IsBidirectional = bidirectional;
            if (!IsBidirectional)
            {
                if (remote)
                {
                    // Write-side of remote unidirectional stream is marked as completed.
                    TrySetWriteCompleted();
                }
                else
                {
                    // Read-side of local unidirectional stream is marked as completed.
                    TrySetReadCompleted();
                }
            }
        }

        internal void Abort()
        {
            // Abort the stream without notifying the peer. This is used for Slic streams which are still registered
            // with the Slic connection when the connection is disposed.
            if (!IsShutdown)
            {
                // Shutdown the stream.
                TrySetStateAndShutdown(State.ReadCompleted | State.WriteCompleted);

                // Ensure the Slic pipe reader and writer are completed.
                var exception = new ConnectionLostException();
                _inputPipeReader.Complete(exception);
                _outputPipeWriter.Complete(exception);
            }
        }

        internal async ValueTask<int> AcquireSendCreditAsync(CancellationToken cancel)
        {
            // Acquire the semaphore to ensure flow control allows sending additional data. It's important to acquire
            // the semaphore before checking _sendCredit. The semaphore acquisition will block if we can't send
            // additional data (_sendCredit == 0). Acquiring the semaphore ensures that we are allowed to send
            // additional data and _sendCredit can be used to figure out the size of the next packet to send.
            await _sendCreditSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            return _sendCredit;
        }

        internal void ConsumeSendCredit(int consumed)
        {
            // Decrease the size of remaining data that we are allowed to send. If all the credit is consumed,
            // _sendCredit will be 0 and we don't release the semaphore to prevent further sends. The semaphore will be
            // released once the stream receives a StreamResumeWrite frame.
            int sendCredit = Interlocked.Add(ref _sendCredit, -consumed);
            if (sendCredit > 0)
            {
                _sendCreditSemaphore.Release();
            }
        }

        internal void ReceivedConsumedFrame(int size)
        {
            int newValue = Interlocked.Add(ref _sendCredit, size);
            if (newValue == size)
            {
                Debug.Assert(_sendCreditSemaphore.Count == 0);
                _sendCreditSemaphore.Release();
            }
            else if (newValue > _connection.PauseWriterThreshold)
            {
                // The peer is trying to increase the credit to a value which is larger than what it is allowed to.
                throw new InvalidDataException("invalid flow control credit increase");
            }
        }

        internal ValueTask<int> ReceivedStreamFrameAsync(int size, bool endStream, CancellationToken cancel) =>
            ReadsCompleted ? new(0) : _inputPipeReader.ReceivedStreamFrameAsync(size, endStream, cancel);

        internal void ReceivedResetFrame(long error)
        {
            if (!IsBidirectional && !IsRemote)
            {
                throw new InvalidDataException("received reset frame on local unidirectional stream");
            }

            ResetError = error;

            _inputPipeReader.ReceivedResetFrame(error);

            TrySetReadCompleted();
        }

        internal void ReceivedStopSendingFrame(long error)
        {
            if (!IsBidirectional && !IsRemote)
            {
                throw new InvalidDataException("received reset frame on local unidirectional stream");
            }

            ResetError = error;

            _outputPipeWriter.ReceivedStopSendingFrame(error);

            TrySetWriteCompleted();
        }

        internal ValueTask<FlushResult> SendStreamFrameAsync(
            ReadOnlySequence<byte> source1,
            ReadOnlySequence<byte> source2,
            bool completeWhenDone,
            CancellationToken cancel) =>
            _connection.SendStreamFrameAsync(this, source1, source2, completeWhenDone, cancel);

        internal void SendStreamResumeWrite(int size) =>
            _ = _connection.SendFrameAsync(
                stream: this,
                FrameType.StreamResumeWrite,
                new StreamResumeWriteBody((ulong)size).Encode,
                CancellationToken.None).AsTask();

        internal bool TrySetReadCompleted() => TrySetStateAndShutdown(State.ReadCompleted);

        internal bool TrySetWriteCompleted() => TrySetStateAndShutdown(State.WriteCompleted);

        private void Shutdown()
        {
            Debug.Assert(_state == (int)(State.ReadCompleted | State.WriteCompleted));
            Action? shutdownAction = null;
            lock (_mutex)
            {
                shutdownAction = _shutdownAction;
                _shutdownCompletedTaskSource?.SetResult();
            }

            try
            {
                shutdownAction?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.Assert(false, $"unexpected exception from stream shutdown action {ex}");
                throw;
            }

            if (IsStarted)
            {
                // Release connection stream count or semaphore for this stream and remove it from the connection.
                _connection.ReleaseStream(this);

                // Local streams are released from the connection when the StreamLast or StreamReset frame is received.
                // Since a remote unidirectional stream doesn't send stream frames, we have to send a
                // UnidirectionalStreamReleased frame here to ensure the local stream is released from the connection by
                // the peer. It's important to decrement the stream count after the ReleaseStream call above to prevent
                // a race condition where the peer could start a new stream before the local counter is decreased.
                if (IsRemote && !IsBidirectional)
                {
                    try
                    {
                        _ = _connection.SendFrameAsync(
                            stream: this,
                            FrameType.UnidirectionalStreamReleased,
                            encode: null,
                            default).AsTask();
                    }
                    catch
                    {
                        // Ignore, the connection is closed.
                    }
                }
            }
        }

        private bool TrySetStateAndShutdown(State state)
        {
            if (!_state.TrySetFlag(state, out State newState))
            {
                return false;
            }
            else
            {
                if (newState == (State.ReadCompleted | State.WriteCompleted))
                {
                    // The stream reads and writes are completed.
                    try
                    {
                        Shutdown();
                    }
                    catch (Exception exception)
                    {
                        Debug.Assert(false, $"unexpected exception {exception}");
                    }
                }
                return true;
            }
        }

        [Flags]
        private enum State : int
        {
            ReadCompleted = 1,
            WriteCompleted = 2,
        }
    }
}
