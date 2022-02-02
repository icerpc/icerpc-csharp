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

        public PipeReader Input => _inputPipeReader;

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

        public PipeWriter Output => _outputPipeWriter;

        /// <summary>Specifies whether or not this is a stream initiated by the peer.</summary>
        internal bool IsRemote => _id != -1 && _id % 2 == (_connection.IsServer ? 0 : 1);

        internal bool IsShutdown => ReadsCompleted && WritesCompleted;

        internal bool ReadsCompleted => ((State)Thread.VolatileRead(ref _state)).HasFlag(State.ReadCompleted);

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

        internal bool WritesCompleted => ((State)Thread.VolatileRead(ref _state)).HasFlag(State.WriteCompleted);

        private readonly SlicNetworkConnection _connection;
        private long _id = -1;
        private readonly PipeWriter _inputPipeWriter;
        private readonly ISlicFrameReader _frameReader;
        private readonly ISlicFrameWriter _frameWriter;
        private readonly object _mutex = new();
        private volatile int _sendCredit = int.MaxValue;
        // The semaphore is used when flow control is enabled to wait for additional send credit to be available.
        private readonly AsyncSemaphore _sendCreditSemaphore = new(1);
        private volatile Action? _shutdownAction;
        private TaskCompletionSource? _shutdownCompletedTaskSource;
        private readonly SlicPipeReader _inputPipeReader;
        private readonly SlicPipeWriter _outputPipeWriter;
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

            // Create a pipe to push the Slice received frame data to the SlicPipeReader.
            var inputPipe = new Pipe(new PipeOptions(
                pool: _connection.Pool,
                minimumSegmentSize: _connection.MinimumSegmentSize,
                pauseWriterThreshold: 0,
                writerScheduler: PipeScheduler.Inline));
            _inputPipeWriter = inputPipe.Writer;
            _inputPipeReader = new SlicPipeReader(this, inputPipe.Reader, _connection.ResumeWriterThreshold);

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
                // Ensure the Slic pipe reader reports ConnectionLostException.
                _inputPipeWriter.Complete(new ConnectionLostException());

                // Ensure the Slic pipe reader and writer are completed.
                _inputPipeReader.Complete();
                _outputPipeWriter.Complete();

                // Shutdown the stream after completing the input pipe writer to ensure the Slic pipe reader will
                // report ConnectionLostException.
                TrySetState(State.ReadCompleted | State.WriteCompleted);
            }
        }

        internal void ReceivedConsumed(int size)
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

        internal async ValueTask ReceivedFrameAsync(int size, bool endStream)
        {
            // Set the reading state to ensure the stream isn't shutdown until the frame read is completed. Shutdown
            // completes in the _inputPipeWriter so it's not safe to call shutdown while a frame is read.
            SetState(State.Reading);

            try
            {
                if (IsShutdown)
                {
                    // The stream is being shutdown. Read and ignore the data.
                    using IMemoryOwner<byte> owner = _connection.Pool.Rent(_connection.MinimumSegmentSize);
                    while (size > 0)
                    {
                        Memory<byte> chunk = owner.Memory;
                        if (chunk.Length > size)
                        {
                            chunk = chunk[0..size];
                        }
                        await _frameReader.ReadFrameDataAsync(chunk, CancellationToken.None).ConfigureAwait(false);
                        size -= chunk.Length;
                    }
                }
                else
                {
                    // Read and append the received data to the input pipe writer.
                    while (size > 0)
                    {
                        // Receive the data and push it to the SlicReader pipe.
                        Memory<byte> chunk = _inputPipeWriter.GetMemory();
                        if (chunk.Length > size)
                        {
                            chunk = chunk[0..size];
                        }

                        await _frameReader.ReadFrameDataAsync(chunk, CancellationToken.None).ConfigureAwait(false);

                        _inputPipeWriter.Advance(chunk.Length);
                        size -= chunk.Length;

                        // This should always complete synchronously since the sender isn't supposed to send more data
                        // than it is allowed.
                        ValueTask<FlushResult> flushTask = _inputPipeWriter.FlushAsync(CancellationToken.None);
                        if (!flushTask.IsCompletedSuccessfully)
                        {
                            _ = flushTask.AsTask();
                            throw new InvalidDataException("received more data than flow control permits");
                        }

                        // We don't check if the input pipe reader completed because we need to consume the whole frame
                        // from the network connection.
                        await flushTask.ConfigureAwait(false);
                    }

                    if (endStream)
                    {
                        // We complete the input pipe writer but we don't mark reads as completed. Reads will be marked
                        // as completed by the Slic pipe reader once the application calls TryRead/ReadAsync. It's
                        // important for unidirectional stream which would otherwise be shutdown before the data has
                        // been consumed by the application. This would allow a malicious client to open many
                        // unidirectional streams before the application gets a chance to consume the data, defeating
                        // the purpose of the UnidirectionalStreamMaxCount option.
                        await _inputPipeWriter.CompleteAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                ClearState(State.Reading);
            }
        }

        internal void ReceivedReset(long error)
        {
            if (!IsBidirectional && !IsRemote)
            {
                throw new InvalidDataException("received reset frame on local unidirectional stream");
            }

            // Complete the input pipe writer.
            if (error.ToSlicError() == SlicStreamError.NoError)
            {
                _inputPipeWriter.Complete();
            }
            else
            {
                _inputPipeWriter.Complete(new MultiplexedStreamAbortedException(error));
                ResetError = error;
            }

            TrySetReadCompleted();
        }

        internal void ReceivedStopSending(long error)
        {
            if (!IsBidirectional && !IsRemote)
            {
                throw new InvalidDataException("received reset frame on local unidirectional stream");
            }

            ResetError = error;
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

        internal async ValueTask<int> SendCreditAcquireAsync(CancellationToken cancel)
        {
            // Acquire the semaphore to ensure flow control allows sending additional data. It's important to acquire
            // the semaphore before checking _sendCredit. The semaphore acquisition will block if we can't send
            // additional data (_sendCredit == 0). Acquiring the semaphore ensures that we are allowed to send
            // additional data and _sendCredit can be used to figure out the size of the next packet to send.
            await _sendCreditSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            return _sendCredit;
        }

        internal void SendCreditConsumed(int consumed)
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

        internal bool TrySetReadCompleted() => TrySetState(State.ReadCompleted);

        internal bool TrySetWriteCompleted() => TrySetState(State.WriteCompleted);

        private void CheckShutdown(State state)
        {
            if (state == (State.ReadCompleted | State.WriteCompleted))
            {
                // The stream reads and writes are completed and we're not reading frames, shutdown the stream.
                try
                {
                    Shutdown();
                }
                catch (Exception exception)
                {
                    Debug.Assert(false, $"unexpected exception {exception}");
                }
            }
        }

        private void ClearState(State state)
        {
            var previousState = (State)Interlocked.And(ref _state, ~(int)state);
            State newState = previousState & ~state;
            Debug.Assert(previousState != newState);
            CheckShutdown(newState);
        }

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
                // the peer. It's important to decrement the stream count with the ReleaseStream call above to prevent a
                // race where the peer could start a new stream before the counter is decreased
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

            if (ResetError is long error)
            {
                _inputPipeWriter.Complete(new MultiplexedStreamAbortedException(error));
            }
            else
            {
                _inputPipeWriter.Complete();
            }
        }

        private void SetState(State state)
        {
            if (!TrySetState(state))
            {
                throw new InvalidOperationException($"state {state} already set");
            }
        }

        private bool TrySetState(State state)
        {
            var previousState = (State)Interlocked.Or(ref _state, (int)state);
            State newState = previousState | state;
            if (previousState == newState)
            {
                // The given state was already set.
                return false;
            }
            else
            {
                CheckShutdown(newState);
                return true;
            }
        }

        [Flags]
        private enum State : byte
        {
            ReadCompleted = 1,
            WriteCompleted = 2,
            Reading = 4,
        }
    }
}
