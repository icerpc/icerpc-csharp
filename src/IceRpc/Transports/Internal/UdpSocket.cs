// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    internal sealed class UdpSocket : NetworkSocket
    {
        internal override int DatagramMaxReceiveSize { get; }
        internal override bool IsDatagram => true;

        // The maximum IP datagram size is 65535. Subtract 20 bytes for the IP header and 8 bytes for the UDP header
        // to get the maximum payload.
        private const int MaxPacketSize = 65535 - UdpOverhead;
        private const int UdpOverhead = 20 + 8;

        private readonly EndPoint? _addr;
        private readonly bool _isServer;
        private readonly IPEndPoint? _multicastEndpoint;
        private readonly string? _multicastInterface;
        private readonly int _ttl;

        internal override async ValueTask<Endpoint> ConnectAsync(Endpoint endpoint, CancellationToken cancel)
        {
            if (_isServer)
            {
                // The remote endpoint is set to an empty endpoint for a UDP server connection because the
                // socket accepts datagrams from "any" client since it's not connected to a specific client.
                return endpoint with
                {
                    Host = "::0",
                    Port = 0
                };
            }
            else
            {
                Debug.Assert(_addr != null);
                try
                {
                    await Socket.ConnectAsync(_addr, cancel).ConfigureAwait(false);
                    var ipEndPoint = (IPEndPoint)Socket.LocalEndPoint!;
                    return endpoint with
                    {
                        Host = ipEndPoint.Address.ToString(),
                        Port = checked((ushort)ipEndPoint.Port)
                    };
                }
                catch (Exception ex)
                {
                    throw new ConnectFailedException(ex);
                }
            }

        }

        internal override bool HasCompatibleParams(Endpoint remoteEndpoint)
        {
            (_, int ttl, string? multicastInterface) = remoteEndpoint.ParseUdpParams();
            return ttl == _ttl && multicastInterface == _multicastInterface;
        }

        internal override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            try
            {
                int received;
                if (_isServer)
                {
                    EndPoint remoteAddress = new IPEndPoint(
                        Socket.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any,
                        0);

                    SocketReceiveFromResult result =
                        await Socket.ReceiveFromAsync(buffer,
                                                      SocketFlags.None,
                                                      remoteAddress,
                                                      cancel).ConfigureAwait(false);

                    received = result.ReceivedBytes;
                }
                else
                {
                    received = await Socket.ReceiveAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
                }
                return received;
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(cancel));
            }
        }

        internal override async ValueTask SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel)
        {
            if (_isServer)
            {
                throw new TransportException("cannot send datagram with server connection");
            }

            try
            {
                if (buffers.Length == 1)
                {
                    await Socket.SendAsync(buffers.Span[0], SocketFlags.None, cancel).ConfigureAwait(false);
                }
                else
                {
                    // Coalesce all buffers into a singled rented buffer.
                    int size = buffers.GetByteCount();
                    using IMemoryOwner<byte> writeBufferOwner = MemoryPool<byte>.Shared.Rent(size);
                    buffers.CopyTo(writeBufferOwner.Memory);
                    await Socket.SendAsync(
                        writeBufferOwner.Memory[0..size],
                        SocketFlags.None,
                        cancel).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(cancel));
            }
        }

        // Only for use by UdpEndpoint.
        internal UdpSocket(
            Socket socket,
            bool isServer,
            EndPoint? addr,
            int ttl = -1,
            string? multicastInterface = null)
            : base(socket)
        {
            _isServer = isServer;
            _ttl = ttl;
            _multicastInterface = multicastInterface;

            DatagramMaxReceiveSize = Math.Min(MaxPacketSize, socket.ReceiveBufferSize - UdpOverhead);

            if (isServer)
            {
                _multicastEndpoint = addr as IPEndPoint;
            }
            else
            {
                _addr = addr!;
            }
        }
    }
}
