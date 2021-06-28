// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    internal sealed class UdpSocket : NetworkSocket
    {
        public override int DatagramMaxReceiveSize { get; }
        protected internal override Socket? Socket => _socket;

        // The maximum IP datagram size is 65535. Subtract 20 bytes for the IP header and 8 bytes for the UDP header
        // to get the maximum payload.
        private const int MaxPacketSize = 65535 - UdpOverhead;
        private const int UdpOverhead = 20 + 8;

        private readonly EndPoint? _addr;
        private readonly bool _isServer;
        private readonly IPEndPoint? _multicastEndpoint;
        private readonly Socket _socket;

        public override ValueTask<Endpoint?> AcceptAsync(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel) => new(null as Endpoint);

        public override async ValueTask<Endpoint> ConnectAsync(
            Endpoint endpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Debug.Assert(_addr != null);
            try
            {
                await _socket.ConnectAsync(_addr, cancel).ConfigureAwait(false);
                return ((UdpEndpoint)endpoint).Clone(_socket.LocalEndPoint!);
            }
            catch (Exception ex)
            {
                throw new ConnectFailedException(ex);
            }
        }

        public override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            try
            {
                int received;
                if (_isServer)
                {
                    EndPoint remoteAddress = new IPEndPoint(
                        _socket.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any,
                        0);

                    SocketReceiveFromResult result =
                        await _socket.ReceiveFromAsync(buffer,
                                                       SocketFlags.None,
                                                       remoteAddress,
                                                       cancel).ConfigureAwait(false);

                    received = result.ReceivedBytes;
                }
                else
                {
                    received = await _socket.ReceiveAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
                }
                return received;
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(cancel));
            }
        }

        public override async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
        {
            if (_isServer)
            {
                throw new TransportException("cannot send datagram with server connection");
            }

            try
            {
                await _socket.SendAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(cancel));
            }
        }

        public override async ValueTask SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel)
        {
            if (buffers.Length == 1)
            {
                await SendAsync(buffers.Span[0], cancel).ConfigureAwait(false);
            }
            else
            {
                // Coalesce all buffers into a singled rented buffer.
                int size = buffers.GetByteCount();
                using IMemoryOwner<byte> writeBufferOwner = MemoryPool<byte>.Shared.Rent(size);
                buffers.CopyTo(writeBufferOwner.Memory);
                await SendAsync(writeBufferOwner.Memory[0..size], cancel).ConfigureAwait(false);
            }
        }

        protected override void Dispose(bool disposing) => _socket.Dispose();

        // Only for use by UdpEndpoint.
        internal UdpSocket(Socket socket, ILogger logger, bool isServer, EndPoint? addr)
            : base(logger)
        {
            _socket = socket;
            _isServer = isServer;
            DatagramMaxReceiveSize = Math.Min(MaxPacketSize, _socket.ReceiveBufferSize - UdpOverhead);

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
