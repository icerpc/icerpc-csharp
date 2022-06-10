// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>A helper class to efficiently read a simple network connection. It provides a PipeReader-like API but
    /// is not a PipeReader.</summary>
    internal class SimpleNetworkConnectionReader : IDisposable
    {
        private readonly ISimpleNetworkConnection _connection;
        private readonly Action _deferIdleTimeout;
        private TimeSpan _idleTimeout;
        private bool _isDisposed;
        private readonly object _mutex = new();
        private readonly Pipe _pipe;
        private readonly Timer _idleTimeoutTimer;

        public void Dispose()
        {
            lock (_mutex)
            {
                if (_isDisposed)
                {
                    return;
                }
                _isDisposed = true;
            }

            _pipe.Writer.Complete();
            _pipe.Reader.Complete();
            _idleTimeoutTimer.Dispose();
        }

        internal SimpleNetworkConnectionReader(
            ISimpleNetworkConnection connection,
            MemoryPool<byte> pool,
            int minimumSegmentSize,
            Action? pingAction,
            Action abortAction)
        {
            _connection = connection;
            _pipe = new Pipe(new PipeOptions(
                pool: pool,
                minimumSegmentSize: minimumSegmentSize,
                pauseWriterThreshold: 0,
                writerScheduler: PipeScheduler.Inline));

            if (pingAction == null)
            {
                // Setup a timer to check if the connection is idle for longer than the idle timeout.
                _idleTimeoutTimer = new Timer(_ => abortAction());

                // Setup the defer idle timeout action.
                _deferIdleTimeout = () =>
                    {
                        lock (_mutex)
                        {
                            if (!_isDisposed)
                            {
                                _idleTimeoutTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
                            }
                        }
                    };
            }
            else
            {
                // Pings are sent every IdleTimeout / 2 after the idle timeout is deferred.
                bool sendPing = true;
                _idleTimeoutTimer = new Timer(
                    _ =>
                    {
                        Action? action;
                        lock (_mutex)
                        {
                            if (_isDisposed)
                            {
                                // Nothing to do.
                                action = null;
                            }
                            else if (sendPing)
                            {
                                // The next time the callback is called, the connection will be aborted if the idle
                                // timeout hasn't been deferred since.
                                _idleTimeoutTimer!.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
                                action = pingAction;
                                sendPing = false;
                            }
                            else
                            {
                                action = abortAction;
                            }
                        }

                        action?.Invoke();
                    });

                // Setup the defer idle timeout action.
                _deferIdleTimeout = () =>
                    {
                        lock (_mutex)
                        {
                            if (!_isDisposed)
                            {
                                // The next timer call will occur on idleTimeout / 2 and it will send a ping frame.
                                sendPing = true;
                                _idleTimeoutTimer.Change(_idleTimeout / 2, Timeout.InfiniteTimeSpan);
                            }
                        }
                    };
            }
        }

        internal void AdvanceTo(SequencePosition consumed) => _pipe.Reader.AdvanceTo(consumed);

        internal void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
            _pipe.Reader.AdvanceTo(consumed, examined);

        /// <summary>Writes <paramref name="byteCount"/> bytes read from this pipe reader or its underlying connection
        /// into <paramref name="bufferWriter"/>.</summary>
        internal ValueTask FillBufferWriterAsync(
            IBufferWriter<byte> bufferWriter,
            int byteCount,
            CancellationToken cancel)
        {
            if (byteCount == 0)
            {
                return default;
            }

            // If there's still data on the pipe reader, copy the data from the pipe reader synchronously.
            if (_pipe.Reader.TryRead(out ReadResult readResult))
            {
                Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled && !readResult.Buffer.IsEmpty);

                ReadOnlySequence<byte> buffer = readResult.Buffer;
                if (buffer.Length > byteCount)
                {
                    buffer = buffer.Slice(0, byteCount);
                }

                bufferWriter.Write(buffer);
                _pipe.Reader.AdvanceTo(buffer.End);

                byteCount -= (int)buffer.Length;

                if (byteCount == 0)
                {
                    return default;
                }
            }

            return ReadFromConnectionAsync(byteCount);

            // Read the remaining bytes directly from the connection into the buffer writer.
            async ValueTask ReadFromConnectionAsync(int byteCount)
            {
                do
                {
                    Memory<byte> buffer = bufferWriter.GetMemory();
                    if (buffer.Length > byteCount)
                    {
                        buffer = buffer[0..byteCount];
                    }

                    int read = await _connection.ReadAsync(buffer, cancel).ConfigureAwait(false);
                    bufferWriter.Advance(read);
                    byteCount -= read;

                    _deferIdleTimeout.Invoke();

                    if (byteCount > 0 && read == 0)
                    {
                        // The peer gracefully shut down the connection but returned less data than expected, it's
                        // considered as an error.
                        throw new ConnectionLostException("received less data than expected");
                    }
                }
                while (byteCount > 0);
            }
        }

        internal ValueTask<ReadOnlySequence<byte>> ReadAsync(CancellationToken cancel = default) =>
            ReadAtLeastAsync(minimumSize: 1, cancel);

        /// <summary>Reads and returns bytes from the underlying network connection. The returned buffer has always
        /// at least minimumSize bytes.</summary>
        internal async ValueTask<ReadOnlySequence<byte>> ReadAtLeastAsync(
            int minimumSize,
            CancellationToken cancel = default)
        {
            if (_pipe.Reader.TryRead(out ReadResult readResult))
            {
                Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled && !readResult.Buffer.IsEmpty);
                if (readResult.Buffer.Length >= minimumSize)
                {
                    return readResult.Buffer;
                }
                _pipe.Reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                minimumSize -= (int)readResult.Buffer.Length;
            }

            do
            {
                // Fill the pipe with data read from the connection.
                Memory<byte> buffer = _pipe.Writer.GetMemory();
                int read = await _connection.ReadAsync(buffer, cancel).ConfigureAwait(false);
                _pipe.Writer.Advance(read);
                minimumSize -= read;

                _deferIdleTimeout.Invoke();

                if (minimumSize > 0 && read == 0)
                {
                    // The peer gracefully shut down the connection but returned less data than expected, it's
                    // considered as an error.
                    throw new ConnectionLostException("received less data than expected");
                }
            }
            while (minimumSize > 0);

            _ = await _pipe.Writer.FlushAsync(cancel).ConfigureAwait(false);

            _pipe.Reader.TryRead(out readResult);
            Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled && !readResult.Buffer.IsEmpty);
            return readResult.Buffer;
        }

        internal void SetIdleTimeout(TimeSpan idleTimeout)
        {
            lock (_mutex)
            {
                _idleTimeout = idleTimeout;

                if (_idleTimeout == Timeout.InfiniteTimeSpan)
                {
                    _idleTimeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                }
                else
                {
                    _deferIdleTimeout();
                }
            }
        }

        internal bool TryRead(out ReadOnlySequence<byte> buffer)
        {
            if (_pipe.Reader.TryRead(out ReadResult readResult))
            {
                Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled && !readResult.Buffer.IsEmpty);
                buffer = readResult.Buffer;
                return true;
            }
            else
            {
                buffer = default;
                return false;
            }
        }
    }
}
