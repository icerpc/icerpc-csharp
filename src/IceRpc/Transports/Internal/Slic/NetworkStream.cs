// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;

namespace IceRpc.Transports.Internal.Slic
{

    /// <summary>The NetworkStream abstract base class to be overridden by multi-stream network connection
    /// implementations. There's an instance of this class for each active stream managed by the multi-stream
    /// network connection.</summary>
    public abstract class NetworkStream : INetworkStream
    {
        /// <inheritdoc/>
        public long Id
        {
            get
            {
                if (_id == -1)
                {
                    throw new InvalidOperationException("stream ID isn't allocated yet");
                }
                return _id;
            }
            set
            {
                Debug.Assert(_id == -1);
                // First add the stream and then assign the ID. AddStream can throw if the connection is closed and
                // in this case we want to make sure the id isn't assigned since the stream isn't considered
                // allocated if not added to the connection.
                _connection.AddStream(value, this, ref _id);
            }
        }

        /// <inheritdoc/>
        public bool IsRemote => _id != -1 && _id % 2 == (_connection.IsServer ? 0 : 1);

        /// <inheritdoc/>
        public bool IsBidirectional { get; }

        /// <inheritdoc/>
        public bool IsShutdown => (Thread.VolatileRead(ref _state) & (int)State.Shutdown) > 0;

        /// <summary>Returns <c>true</c> if the receiving side of the stream is completed, <c>false</c> otherwise.
        /// </summary>
        public bool ReadsCompleted => (Thread.VolatileRead(ref _state) & (int)State.ReadCompleted) > 0;

        /// <summary>Returns <c>true</c> if the sending side of the stream is completed, <c>false</c> otherwise.
        /// </summary>
        public bool WritesCompleted => (Thread.VolatileRead(ref _state) & (int)State.WriteCompleted) > 0;

        /// <inheritdoc/>
        public Action? ShutdownAction
        {
            get => _shutdownAction;
            set
            {
                _shutdownAction = value;
                if (IsShutdown)
                {
                    // It's possible for the action to be called twice if shutdown occurs between the assignment
                    // and the IsShutdown check. Callers should make sure the action is safe when called twice.
                    _shutdownAction?.Invoke();
                }
            }
        }

        /// <inheritdoc/>
        public virtual ReadOnlyMemory<byte> TransportHeader => default;

        /// <summary>Returns true if the stream ID is assigned</summary>
        internal bool IsStarted => _id != -1;

        private readonly MultiStreamConnection _connection;

        // Depending on the stream implementation, the _id can be assigned on construction or only once SendAsync
        // is called. Once it's assigned, it's immutable. The specialization of the stream is responsible for not
        // accessing this data member concurrently when it's not safe.
        private long _id = -1;
        private int _state;
        private volatile Action? _shutdownAction;

        /// <inheritdoc/>
        public abstract void AbortRead(StreamError errorCode);

        /// <inheritdoc/>
        public abstract void AbortWrite(StreamError errorCode);

        /// <inheritdoc/>
        public virtual System.IO.Stream AsByteStream() => new ByteStream(this);

        /// <inheritdoc/>
        public abstract void EnableReceiveFlowControl();

        /// <inheritdoc/>
        public abstract void EnableSendFlowControl();

        /// <inheritdoc/>
        public abstract ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <inheritdoc/>
        public abstract ValueTask SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel);

        /// <inheritdoc/>
        public async ValueTask ShutdownCompleted(CancellationToken cancel)
        {
            if (_shutdownAction != null)
            {
                throw new InvalidOperationException(
                    $"{nameof(ShutdownCompleted)} can't be used if {nameof(ShutdownAction)} is set");
            }
            var shutdownCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ShutdownAction = () => shutdownCompletionSource.TrySetResult();
            await shutdownCompletionSource.Task.WaitAsync(cancel).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override string ToString() => $"{base.ToString()} (ID={Id})";

        /// <summary>Constructs a remote stream with the given ID.</summary>
        /// <param name="streamId">The stream ID.</param>
        /// <param name="connection">The parent connection.</param>
        protected NetworkStream(MultiStreamConnection connection, long streamId)
        {
            _connection = connection;
            IsBidirectional = streamId % 4 < 2;
            _connection.AddStream(streamId, this, ref _id);

            if (!IsBidirectional)
            {
                // Write-side of remote unidirectional stream is marked as completed.
                TrySetWriteCompleted();
            }
        }

        /// <summary>Constructs a local stream.</summary>
        /// <param name="bidirectional"><c>true</c> to create a bidirectional stream, <c>false</c> otherwise.</param>
        /// <param name="connection">The parent connection.</param>
        protected NetworkStream(MultiStreamConnection connection, bool bidirectional)
        {
            _connection = connection;
            IsBidirectional = bidirectional;
            if (!IsBidirectional)
            {
                // Read-side of local unidirectional stream is marked as completed.
                TrySetReadCompleted();
            }
        }

        /// <summary>Shutdown the stream. This is called when the stream read and write sides are completed.</summary>
        protected virtual void Shutdown()
        {
            Debug.Assert(_state == (int)(State.ReadCompleted | State.WriteCompleted | State.Shutdown));
            try
            {
                ShutdownAction?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.Assert(false, $"unexpected exception {ex}");
                throw;
            }
            _connection.RemoveStream(Id);
        }

        /// <summary>Mark reads as completed for this stream.</summary>
        /// <returns><c>true</c> if the stream reads were successfully marked as completed, <c>false</c> if
        /// the stream reads were already completed.</returns>
        protected internal bool TrySetReadCompleted(bool shutdown = true) =>
            TrySetState(State.ReadCompleted, shutdown);

        /// <summary>Mark writes as completed for this stream.</summary>
        /// <returns><c>true</c> if the stream writes were successfully marked as completed, <c>false</c> if
        /// the stream writes were already completed.</returns>
        protected internal bool TrySetWriteCompleted(bool shutdown = true) =>
            TrySetState(State.WriteCompleted, shutdown);

        /// <summary>Shutdown the stream if it's not already shutdown.</summary>
        protected void TryShutdown()
        {
            // If both reads and writes are completed, the stream is started and not already shutdown, call
            // shutdown.
            if (ReadsCompleted && WritesCompleted && TrySetState(State.Shutdown, false) && IsStarted)
            {
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

        internal void Abort(StreamError errorCode)
        {
            // Abort writes.
            AbortWrite(errorCode);

            // Abort reads.
            AbortRead(errorCode);
        }

        private bool TrySetState(State state, bool shutdown)
        {
            if (((State)Interlocked.Or(ref _state, (int)state)).HasFlag(state))
            {
                return false;
            }
            else
            {
                if (shutdown)
                {
                    TryShutdown();
                }
                return true;
            }
        }

        private enum State : int
        {
            ReadCompleted = 1,
            WriteCompleted = 2,
            Shutdown = 4
        }

        // A System.IO.Stream class to wrap SendAsync/ReceiveAsync functionality of the RpcStream. For Quic,
        // this won't be needed since the QuicStream is a System.IO.Stream.
        private class ByteStream : System.IO.Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotImplementedException();

            public override long Position
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }

            private readonly ReadOnlyMemory<byte>[] _buffers;
            private readonly NetworkStream _stream;

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
                ReadAsync(new Memory<byte>(buffer, offset, count), cancel).AsTask();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
            {
                try
                {
                    if (_stream.ReadsCompleted)
                    {
                        return 0;
                    }
                    return await _stream.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
                }
                catch (StreamAbortedException ex) when (ex.ErrorCode == StreamError.StreamingCanceledByWriter)
                {
                    throw new System.IO.IOException("streaming canceled by the writer", ex);
                }
                catch (StreamAbortedException ex) when (ex.ErrorCode == StreamError.StreamingCanceledByReader)
                {
                    throw new System.IO.IOException("streaming canceled by the reader", ex);
                }
                catch (StreamAbortedException ex)
                {
                    throw new System.IO.IOException($"unexpected streaming error {ex.ErrorCode}", ex);
                }
                catch (Exception ex)
                {
                    throw new System.IO.IOException($"unexpected exception", ex);
                }
            }

            public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotImplementedException();

            public override void SetLength(long value) => throw new NotImplementedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
                WriteAsync(new Memory<byte>(buffer, offset, count), cancel).AsTask();

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
            {
                try
                {
                    _buffers[^1] = buffer;
                    await _stream.SendAsync(_buffers, buffer.Length == 0, cancel).ConfigureAwait(false);
                }
                catch (StreamAbortedException ex) when (ex.ErrorCode == StreamError.StreamingCanceledByWriter)
                {
                    throw new System.IO.IOException("streaming canceled by the writer", ex);
                }
                catch (StreamAbortedException ex) when (ex.ErrorCode == StreamError.StreamingCanceledByReader)
                {
                    throw new System.IO.IOException("streaming canceled by the reader", ex);
                }
                catch (StreamAbortedException ex)
                {
                    throw new System.IO.IOException($"unexpected streaming error {ex.ErrorCode}", ex);
                }
                catch (Exception ex)
                {
                    throw new System.IO.IOException($"unexpected exception", ex);
                }
            }

            internal ByteStream(NetworkStream stream)
            {
                _stream = stream;
                if (_stream.TransportHeader.Length > 0)
                {
                    _buffers = new ReadOnlyMemory<byte>[2];
                    _buffers[0] = _stream.TransportHeader.ToArray();
                }
                else
                {
                    _buffers = new ReadOnlyMemory<byte>[1];
                }
            }
        }
    }
}
