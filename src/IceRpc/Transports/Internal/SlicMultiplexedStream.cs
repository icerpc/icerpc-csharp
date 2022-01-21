// Copyright (c) ZeroC, Inc. All rights reserved.

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
        private volatile Action? _shutdownAction;
        private TaskCompletionSource? _shutdownCompletedTaskSource;
        private readonly SlicPipeReader _inputPipeReader;
        private readonly SlicPipeWriter _outputPipeWriter;
        private long? _resetErrorCode;
        private int _state;

        public void Abort()
        {
            // Abort the stream without notifying the peer. This is used when the connection
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
                    await _frameWriter.WriteStreamStopSendingAsync(
                        this,
                        new StreamStopSendingBody(errorCode),
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
                    await _frameWriter.WriteStreamResetAsync(
                        this,
                        new StreamResetBody(errorCode),
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

            _frameReader = reader;
            _frameWriter = writer;

            // TODO: cache resetable SlicPipeReader/SlicPipeWriter on the connection for re-use here.

            // TODO: using a Pipe for the SlicReader implementation is a bit overkill. We ensure the pipe never
            // blocks by setting a larger pause writer threeshold than the peer stream pause writer threeshold.
            var inputPipe = new Pipe(new PipeOptions(
                pool: _connection.Pool,
                minimumSegmentSize: _connection.MinimumSegmentSize,
                pauseWriterThreshold: _connection.PeerPauseWriterThreeshold + 1));
            _inputPipeWriter = inputPipe.Writer;
            _inputPipeReader = new SlicPipeReader(this, inputPipe.Reader, _connection.ResumeWriterThreeshold);

            _outputPipeWriter = new SlicPipeWriter(this, _connection);

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

        internal void ReceivedConsumed(int size) => _outputPipeWriter.ReceivedConsumed(size);

        internal async ValueTask ReceivedFrameAsync(int size, bool endStream)
        {
            if (!IsBidirectional && !IsRemote)
            {
                throw new InvalidDataException($"received stream frame on local unidirectional stream");
            }
            else if (size == 0 && !endStream)
            {
                throw new InvalidDataException("invalid stream frame, received 0 bytes without end of stream");
            }

            // Read and append the received data to the input pipe writer.
            if (size > 0)
            {
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

                    // This should always complete synchronously since the sender isn't supposed to send more data than
                    // it is allowed.
                    ValueTask<FlushResult> flushTask = _inputPipeWriter.FlushAsync(CancellationToken.None);
                    if (!flushTask.IsCompletedSuccessfully)
                    {
                        _ = flushTask.AsTask();
                        throw new InvalidDataException("received more data than flow control permits");
                    }
                    await flushTask.ConfigureAwait(false);
                }

                if (endStream)
                {
                    // We complete the input pipe writer but we don't mark reads as completed. Reads will be marked as
                    // completed by the Slic pipe reader once the application calls TryRead/ReadAsync. It's important
                    // for unidirectional stream which would otherwise be shutdown before the data has been consumed by
                    // the application. This would allow a malicious client to open many unidirectional streams before
                    // the application gets a chance to consume the data, defeating the purpose of the
                    // UnidirectionalStreamMaxCount option.
                    await _inputPipeWriter.CompleteAsync().ConfigureAwait(false);
                }
            }
            else
            {
                await _inputPipeWriter.CompleteAsync().ConfigureAwait(false);
            }
        }

        internal void ReceivedReset(long error)
        {
            if (!IsBidirectional && !IsRemote)
            {
                throw new InvalidDataException("received reset frame on local unidirectional stream");
            }

            // Complete the input pipe writer before marking reads as completed. Marking reads as completed might
            // shutdown the stream and we don't want the input pipe writer to be completed by Shutdown.
            _inputPipeWriter.Complete(new MultiplexedStreamAbortedException(error));

            ResetError = error;
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

        internal void SendStreamConsumed(int consumed) =>
            _ = _frameWriter.WriteStreamConsumedAsync(
                this,
                new StreamConsumedBody((ulong)consumed),
                CancellationToken.None).AsTask();

        internal bool TrySetReadCompleted() => TrySetState(State.ReadCompleted);

        internal bool TrySetWriteCompleted() => TrySetState(State.WriteCompleted);

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
                // Release connection stream count or semaphore for this stream and remove it from the factory.
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
                        _ = _frameWriter.WriteUnidirectionalStreamReleasedAsync(this, default).AsTask();
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

        private bool TrySetState(State state)
        {
            var previousState = (State)Interlocked.Or(ref _state, (int)state);
            State newState = previousState | state;
            if (previousState == newState)
            {
                // The given state was already set.
                return false;
            }
            else if (newState == (State.ReadCompleted | State.WriteCompleted))
            {
                // The stream reads and writes are completed, shutdown the stream.
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

        [Flags]
        private enum State : byte
        {
            ReadCompleted = 1,
            WriteCompleted = 2,
        }
    }
}
