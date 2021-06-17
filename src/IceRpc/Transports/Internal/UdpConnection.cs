// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    internal sealed class UdpConnection : SingleStreamConnection
    {
        /// <inheritdoc/>
        public override ConnectionInformation ConnectionInformation =>
            _connectionInformation ??= new UdpConnectionInformation(_socket)
            {
                MulticastEndpoint = _multicastEndpoint
            };

        /// <inheritdoc/>
        public override int DatagramMaxReceiveSize { get; }

        /// <inheritdoc/>
        internal override Socket? NetworkSocket => _socket;

        // The maximum IP datagram size is 65535. Subtract 20 bytes for the IP header and 8 bytes for the UDP header
        // to get the maximum payload.
        private const int MaxPacketSize = 65535 - UdpOverhead;
        private const int UdpOverhead = 20 + 8;

        private readonly EndPoint? _addr;
        private UdpConnectionInformation? _connectionInformation;
        private readonly bool _incoming;
        private readonly IPEndPoint? _multicastEndpoint;
        private readonly Socket _socket;

        public override ValueTask<Endpoint?> AcceptAsync(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel) => new(null as Endpoint);

        public override ValueTask CloseAsync(long errorCode, CancellationToken cancel) => default;

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
                if (_incoming)
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
            catch (Exception ex) when (cancel.IsCancellationRequested)
            {
                throw new OperationCanceledException(null, ex, cancel);
            }
            catch (Exception ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException();
            }
            catch (Exception ex)
            {
                throw new TransportException(ex);
            }
        }

        public override async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
        {
            if (_incoming)
            {
                throw new TransportException("cannot send datagram with incoming connection");
            }

            try
            {
                await _socket.SendAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.MessageSize)
            {
                // Don't retry if the datagram can't be sent because its too large.
                throw new TransportException(ex);
            }
            catch (Exception ex) when (cancel.IsCancellationRequested)
            {
                throw new OperationCanceledException(null, ex, cancel);
            }
            catch (Exception ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException();
            }
            catch (Exception ex)
            {
                throw new TransportException(ex);
            }
        }

        public override ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel) =>
            SendAsync(buffers.ToSingleBuffer(), cancel);

        protected override void Dispose(bool disposing) => _socket.Dispose();

        // Only for use by UdpEndpoint.
        internal UdpConnection(Socket socket, ILogger logger, bool isIncoming, EndPoint? addr)
            : base(logger)
        {
            _socket = socket;
            _incoming = isIncoming;
            DatagramMaxReceiveSize = Math.Min(MaxPacketSize, _socket.ReceiveBufferSize - UdpOverhead);

            if (isIncoming)
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
