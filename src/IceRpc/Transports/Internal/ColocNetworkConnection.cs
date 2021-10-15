// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Threading.Channels;

namespace IceRpc.Transports.Internal
{
    /// <summary>The colocated network connection class to exchange data within the same process. The
    /// implementation copies the send buffer into the receive buffer.</summary>
    internal class ColocNetworkConnection : INetworkConnection, INetworkStream
    {
        public int DatagramMaxReceiveSize => throw new InvalidOperationException();

        public bool IsDatagram => false;

        public bool IsSecure => true;

        public TimeSpan LastActivity => TimeSpan.Zero;

        private readonly Endpoint _endpoint;
        private readonly bool _isServer;
        private readonly SlicOptions _slicOptions;
        private readonly ChannelReader<ReadOnlyMemory<byte>> _reader;
        private ReadOnlyMemory<byte> _receivedBuffer;
        private SlicMultiplexedNetworkStreamFactory? _slicConnection;
        private readonly ChannelWriter<ReadOnlyMemory<byte>> _writer;

        public void Close(Exception? exception = null)
        {
            _slicConnection?.Dispose();
            _writer.TryComplete(); // Dispose might be called multiple times
        }

        public async ValueTask<(IMultiplexedNetworkStreamFactory, NetworkConnectionInformation)> ConnectMultiStreamConnectionAsync(
            CancellationToken cancel)
        {
            (INetworkStream singleStreamConnection, NetworkConnectionInformation information) =
                 await ConnectSingleStreamConnectionAsync(cancel).ConfigureAwait(false);

            // Multi-stream support for a colocated connection is provided by Slic.
            _slicConnection ??= await NetworkConnection.CreateSlicConnectionAsync(
                singleStreamConnection,
                _isServer,
                TimeSpan.MaxValue,
                _slicOptions,
                cancel).ConfigureAwait(false);
            return (_slicConnection, information);
        }

        public ValueTask<(INetworkStream, NetworkConnectionInformation)> ConnectSingleStreamConnectionAsync(
            CancellationToken cancel) =>
                // TODO: support idle timeout for colocated connections?
                new((this, new NetworkConnectionInformation(_endpoint, _endpoint, TimeSpan.MaxValue, null)));

        public bool HasCompatibleParams(Endpoint remoteEndpoint)
        {
            if (remoteEndpoint.Params.Count > 0)
            {
                throw new FormatException(
                    $"unknown parameter '{remoteEndpoint.Params[0].Name}' in endpoint '{remoteEndpoint}'");
            }
            return !_isServer;
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (_receivedBuffer.Length == 0)
            {
                try
                {
                    _receivedBuffer = await _reader.ReadAsync(cancel).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ChannelClosedException exception)
                {
                    throw new ConnectionLostException(exception);
                }
                catch (Exception exception)
                {
                    throw new TransportException(exception);
                }
            }

            if (_receivedBuffer.Length > buffer.Length)
            {
                _receivedBuffer[0..buffer.Length].CopyTo(buffer);
                _receivedBuffer = _receivedBuffer[buffer.Length..];
                return buffer.Length;
            }
            else
            {
                int received = _receivedBuffer.Length;
                _receivedBuffer.CopyTo(buffer[0..received]);
                _receivedBuffer = ReadOnlyMemory<byte>.Empty;
                return received;
            }
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
            try
            {
                await _writer.WriteAsync(buffers.ToSingleBuffer(), cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ChannelClosedException exception)
            {
                throw new ConnectionLostException(exception);
            }
            catch (Exception exception)
            {
                throw new TransportException(exception);
            }
        }

        internal ColocNetworkConnection(
            Endpoint endpoint,
            bool isServer,
            SlicOptions slicOptions,
            ChannelWriter<ReadOnlyMemory<byte>> writer,
            ChannelReader<ReadOnlyMemory<byte>> reader)
        {
            _endpoint = endpoint;
            _isServer = isServer;
            _slicOptions = slicOptions;
            _reader = reader;
            _writer = writer;
        }
    }
}
