// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Threading.Channels;

namespace IceRpc.Transports.Internal
{
    /// <summary>The colocated network connection class to exchange data within the same process. The implementation
    /// copies the send buffer into the receive buffer.</summary>
    internal class ColocNetworkConnection : ISimpleNetworkConnection, ISimpleStream
    {
        int ISimpleStream.DatagramMaxReceiveSize => throw new InvalidOperationException();

        bool ISimpleStream.IsDatagram => false;

        bool INetworkConnection.IsSecure => true;

        TimeSpan INetworkConnection.LastActivity => TimeSpan.Zero;

        private readonly Endpoint _endpoint;
        private readonly bool _isServer;
        private readonly ChannelReader<ReadOnlyMemory<byte>> _reader;
        private ReadOnlyMemory<byte> _receivedBuffer;
        private readonly ChannelWriter<ReadOnlyMemory<byte>> _writer;

        Task<(ISimpleStream, NetworkConnectionInformation)> ISimpleNetworkConnection.ConnectAsync(
            CancellationToken cancel) =>
            Task.FromResult<(ISimpleStream, NetworkConnectionInformation)>(
                (this, new NetworkConnectionInformation(_endpoint, _endpoint, TimeSpan.MaxValue, null)));

        void INetworkConnection.Close(Exception? exception) => _writer.TryComplete();

        bool INetworkConnection.HasCompatibleParams(Endpoint remoteEndpoint)
        {
            if (remoteEndpoint.Params.Count > 0)
            {
                throw new FormatException(
                    $"unknown parameter '{remoteEndpoint.Params[0].Name}' in endpoint '{remoteEndpoint}'");
            }
            return !_isServer;
        }

        async ValueTask<int> ISimpleStream.ReadAsync(Memory<byte> buffer, CancellationToken cancel)
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

        async ValueTask ISimpleStream.WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
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
            ChannelWriter<ReadOnlyMemory<byte>> writer,
            ChannelReader<ReadOnlyMemory<byte>> reader)
        {
            _endpoint = endpoint;
            _isServer = isServer;
            _reader = reader;
            _writer = writer;
        }
    }
}
