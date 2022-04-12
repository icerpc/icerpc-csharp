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
        public bool IsRemote => _id != -1 && _id % 2 == (_connection.IsServer ? 0 : 1);

        /// <inheritdoc/>
        public bool IsStarted => Thread.VolatileRead(ref _id) != -1;

        public PipeWriter Output => !IsRemote || IsBidirectional ?
            _outputPipeWriter :
            throw new InvalidOperationException($"can't get {nameof(Output)} on unidirectional remote stream");

        internal bool IsShutdown => ReadsCompleted && WritesCompleted;

        internal bool ReadsCompleted => _state.HasFlag(State.ReadsCompleted);

        internal bool WritesCompleted => _state.HasFlag(State.WritesCompleted);

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
        private Action? _shutdownAction;
        private Action? _peerInputCompletedAction;
        private int _state;

        public void Abort(Exception exception)
        {
            _inputPipeReader.Abort(exception);
            _outputPipeWriter.Abort(exception);
        }

        /// <inheritdoc/>
        public void OnPeerInputCompleted(Action callback) =>
            RegisterStateAction(ref _peerInputCompletedAction, State.ReceivedStopSending, callback);

        /// <inheritdoc/>
        public void OnShutdown(Action callback) =>
            RegisterStateAction(ref _shutdownAction, State.WritesCompleted | State.ReadsCompleted, callback);

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
                _frameReader.NetworkConnectionReader);

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

        internal void AbortRead(long errorCode)
        {
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

        internal void AbortWrite(long errorCode)
        {
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
            // released once the stream receives a StreamConsumed frame.
            int sendCredit = Interlocked.Add(ref _sendCredit, -consumed);
            if (sendCredit > 0)
            {
                _sendCreditSemaphore.Release();
            }
            Debug.Assert(sendCredit >= 0);
        }

        internal void ReceivedConsumedFrame(int size)
        {
            int newValue = Interlocked.Add(ref _sendCredit, size);
            if (newValue == size)
            {
                Debug.Assert(_sendCreditSemaphore.Count == 0);
                _sendCreditSemaphore.Release();
            }
            else if (newValue > _connection.PeerPauseWriterThreshold)
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
            else if (!TrySetState(State.ReceivedReset))
            {
                throw new InvalidDataException("already received reset frame");
            }

            if (TrySetReadCompleted())
            {
                _inputPipeReader.ReceivedResetFrame(error);
            }
        }

        internal void ReceivedStopSendingFrame(long error)
        {
            if (!IsBidirectional && !IsRemote)
            {
                throw new InvalidDataException("received stop sending on remote unidirectional stream");
            }
            else if (!TrySetState(State.ReceivedStopSending))
            {
                throw new InvalidDataException("already received stop sending frame");
            }

            if (TrySetWriteCompleted())
            {
                _outputPipeWriter.ReceivedStopSendingFrame(error);
            }
        }

        internal ValueTask<FlushResult> SendStreamFrameAsync(
            ReadOnlySequence<byte> source1,
            ReadOnlySequence<byte> source2,
            bool endStream,
            CancellationToken cancel) =>
            _connection.SendStreamFrameAsync(this, source1, source2, endStream, cancel);

        internal void SendStreamConsumed(int size) =>
            _ = _connection.SendFrameAsync(
                stream: this,
                FrameType.StreamConsumed,
                new StreamConsumedBody((ulong)size).Encode,
                CancellationToken.None).AsTask();

        private void ExecuteStateAction(ref Action? stateAction)
        {
            Action? action;
            lock (_mutex)
            {
                action = stateAction;
            }

            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.Assert(false, $"unexpected exception from stream state action\n{ex}");
                throw;
            }
        }

        private void RegisterStateAction(ref Action? stateAction, State state, Action action)
        {
            bool isStateAlreadySet = false;
            lock (_mutex)
            {
                if (_state.HasFlag(state))
                {
                    isStateAlreadySet = true;
                }
                else
                {
                    stateAction = action;
                }
            }
            if (isStateAlreadySet)
            {
                action();
            }
        }

        private void Shutdown()
        {
            Debug.Assert(_state.HasFlag(State.ReadsCompleted | State.WritesCompleted));

            if (IsStarted)
            {
                // Release connection stream count or semaphore for this stream and remove it from the connection.
                _connection.ReleaseStream(this);

                // Local bidirectional streams are released from the connection when the StreamLast or StreamReset frame
                // is received. Since a remote unidirectional stream doesn't send stream frames, we have to send a
                // UnidirectionalStreamReleased frame here to ensure the peer's stream is released. It's important to
                // send the frame after the ReleaseStream call above to prevent a race condition where the peer could
                // start a new unidirectional stream before the local counter is decreased.
                if (IsRemote && !IsBidirectional && !_connection.IsAborted)
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

        internal bool TrySetReadCompleted() => TrySetState(State.ReadsCompleted);

        private bool TrySetState(State state)
        {
            if (_state.TrySetFlag(state, out int newState))
            {
                if ((state.HasFlag(State.ReadsCompleted) || state.HasFlag(State.WritesCompleted)) &&
                    newState.HasFlag(State.ReadsCompleted | State.WritesCompleted))
                {
                    // The stream reads and writes are completed, it's time to shutdown the stream.
                    Shutdown();

                    // Notify the registered shutdown action.
                    ExecuteStateAction(ref _shutdownAction);
                }
                if (state.HasFlag(State.ReceivedStopSending))
                {
                    // Notify the registered peer input completed action.
                    ExecuteStateAction(ref _peerInputCompletedAction);
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        internal bool TrySetWriteCompleted() => TrySetState(State.WritesCompleted);

        [Flags]
        private enum State : int
        {
            ReadsCompleted = 1,
            WritesCompleted = 2,
            ReceivedStopSending = 4,
            ReceivedReset = 8
        }
    }
}
