// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IceRpc.Transports.Internal
{
    internal class UdpServerNetworkConnection : ISimpleNetworkConnection, ISimpleStream
    {
        public int DatagramMaxReceiveSize { get; }
        bool ISimpleStream.IsDatagram => true;
        bool INetworkConnection.IsSecure => false;

        TimeSpan INetworkConnection.LastActivity => TimeSpan.FromMilliseconds(_lastActivity);

        internal Socket Socket { get; }
        private long _lastActivity = (long)Time.Elapsed.TotalMilliseconds;
        private readonly Endpoint _localEndpoint;

        void INetworkConnection.Close(Exception? exception) => Socket.Close();

        Task<(ISimpleStream, NetworkConnectionInformation)> ISimpleNetworkConnection.ConnectAsync(
            CancellationToken cancel) =>
            // The remote endpoint is set to an empty endpoint for a UDP server connection because the
            // socket accepts datagrams from "any" client since it's not connected to a specific client.
            Task.FromResult((this as ISimpleStream,
                             new NetworkConnectionInformation(localEndpoint: _localEndpoint,
                                                              remoteEndpoint: _localEndpoint with
                                                              {
                                                                 Host = "::0",
                                                                 Port = 0
                                                              },
                                                              TimeSpan.MaxValue, // TODO: returning Infinite doesn't work
                                                              remoteCertificate: null)));

        bool INetworkConnection.HasCompatibleParams(Endpoint remoteEndpoint) =>
            throw new NotSupportedException(
                $"{nameof(INetworkConnection.HasCompatibleParams)} is only supported by client connections.");

        async ValueTask<int> ISimpleStream.ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            try
            {
                var remoteAddress = new IPEndPoint(
                    Socket.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any,
                    0);

                SocketReceiveFromResult result = await Socket.ReceiveFromAsync(buffer,
                                                                               SocketFlags.None,
                                                                               remoteAddress,
                                                                               cancel).ConfigureAwait(false);

                Interlocked.Exchange(ref _lastActivity, (long)Time.Elapsed.TotalMilliseconds);
                return result.ReceivedBytes;
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(cancel));
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append(GetType().Name);
            builder.Append(" { ");
            if (PrintMembers(builder))
            {
                builder.Append(' ');
            }
            builder.Append('}');
            return builder.ToString();
        }

        ValueTask ISimpleStream.WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel) =>
            throw new TransportException("cannot write to a UDP server stream");

        internal UdpServerNetworkConnection(Socket socket, Endpoint localEndpoint)
        {
            DatagramMaxReceiveSize = Math.Min(UdpUtils.MaxPacketSize, socket.ReceiveBufferSize - UdpUtils.UdpOverhead);
            Socket = socket;
            _localEndpoint = localEndpoint;
        }

        private bool PrintMembers(StringBuilder builder)
        {
            builder.Append("LocalEndPoint = ").Append(Socket.LocalEndPoint);
            return true;
        }
    }
}
