// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The colocated network connection class to exchange data within the same process. The implementation
    /// copies the send buffer into the receive buffer.</summary>
    internal class ColocNetworkConnection : ISimpleNetworkConnection
    {
        TimeSpan INetworkConnection.LastActivity => TimeSpan.Zero;

        private readonly Endpoint _endpoint;
        private readonly bool _isServer;
        private readonly PipeReader _reader;
        private int _state;
        private readonly PipeWriter _writer;

        public Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel) =>
            Task.FromResult(new NetworkConnectionInformation(_endpoint, _endpoint, TimeSpan.MaxValue, null));

        public async ValueTask DisposeAsync()
        {
            if (_state.TrySetFlag(State.Disposed))
            {
                if (_state.HasFlag(State.Reading))
                {
                    _reader.CancelPendingRead();
                }
                else
                {
                    await _reader.CompleteAsync(new ConnectionLostException()).ConfigureAwait(false);
                }

                if (_state.HasFlag(State.Writing))
                {
                    _writer.CancelPendingFlush();
                }
                else
                {
                    await _writer.CompleteAsync(new ConnectionLostException()).ConfigureAwait(false);
                }
            }
        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint)
        {
            if (remoteEndpoint.Params.Count > 1)
            {
                throw new FormatException($"unknown parameter in endpoint '{remoteEndpoint}'");
            }
            return !_isServer;
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (!_state.TrySetFlag(State.Reading))
            {
                throw new InvalidOperationException($"{nameof(ReadAsync)} is not thread safe");
            }

            try
            {
                if (_state.HasFlag(State.Disposed))
                {
                    throw new ObjectDisposedException($"{typeof(ColocNetworkConnection)}");
                }

                ReadResult readResult = await _reader.ReadAsync(cancel).ConfigureAwait(false);

                if (_state.HasFlag(State.Disposed))
                {
                    throw new ObjectDisposedException($"{typeof(ColocNetworkConnection)}");
                }

                Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled);

                // We could eventually add a CopyTo(this ReadOnlySequence<byte> src, Memory<byte> dest) extension method
                // if we need this in other places.
                int read;
                if (readResult.Buffer.IsSingleSegment)
                {
                    read = CopySegmentToMemory(readResult.Buffer.First, buffer);
                }
                else
                {
                    read = 0;
                    foreach (ReadOnlyMemory<byte> segment in readResult.Buffer)
                    {
                        read += CopySegmentToMemory(segment, buffer[read..]);
                        if (read == buffer.Length)
                        {
                            break;
                        }
                    }
                }
                _reader.AdvanceTo(readResult.Buffer.GetPosition(read));
                return read;
            }
            catch (ObjectDisposedException)
            {
                await _reader.CompleteAsync(new ConnectionLostException()).ConfigureAwait(false);
                throw;
            }
            finally
            {
                _state.ClearFlag(State.Reading);
            }

            static int CopySegmentToMemory(ReadOnlyMemory<byte> source, Memory<byte> destination)
            {
                if (source.Length > destination.Length)
                {
                    source[0..destination.Length].CopyTo(destination);
                    return destination.Length;
                }
                else
                {
                    source.CopyTo(destination);
                    return source.Length;
                }
            }
        }

        public async ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
            if (!_state.TrySetFlag(State.Writing))
            {
                throw new InvalidOperationException($"{nameof(WriteAsync)} is not thread safe");
            }

            try
            {
                if (_state.HasFlag(State.Disposed))
                {
                    throw new ObjectDisposedException($"{typeof(ColocNetworkConnection)}");
                }

                foreach (ReadOnlyMemory<byte> buffer in buffers)
                {
                    FlushResult flushResult = await _writer.WriteAsync(buffer, cancel).ConfigureAwait(false);

                    if (_state.HasFlag(State.Disposed))
                    {
                        throw new ObjectDisposedException($"{typeof(ColocNetworkConnection)}");
                    }

                    Debug.Assert(!flushResult.IsCompleted && !flushResult.IsCanceled);
                }
            }
            catch (ObjectDisposedException)
            {
                await _writer.CompleteAsync(new ConnectionLostException()).ConfigureAwait(false);
                throw;
            }
            finally
            {
                _state.ClearFlag(State.Writing);
            }
        }

        internal ColocNetworkConnection(Endpoint endpoint, bool isServer, PipeReader reader, PipeWriter writer)
        {
            _endpoint = endpoint;
            _isServer = isServer;
            _reader = reader;
            _writer = writer;
        }

        private enum State : int
        {
            Disposed = 1,
            Writing = 2,
            Reading = 4
        }
    }
}
