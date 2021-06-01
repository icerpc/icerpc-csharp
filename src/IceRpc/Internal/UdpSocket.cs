// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Internal
{
    internal sealed class UdpSocket : SingleStreamSocket, IUdpSocket
    {
        /// <inheritdoc/>
        public bool IsSecure => false;

        /// <inheritdoc/>
        public IPEndPoint? LocalEndPoint
        {
            get
            {
                try
                {
                    return _socket.LocalEndPoint as IPEndPoint;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <inheritdoc/>
        public IPEndPoint? RemoteEndPoint
        {
            get
            {
                try
                {
                    return _socket.RemoteEndPoint as IPEndPoint;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <inheritdoc/>
        public IPEndPoint? MulticastEndpoint { get; private set; }

        /// <inheritdoc/>
        public override ISocket Socket => this;

        /// <inheritdoc/>
        internal override Socket? NetworkSocket => _socket;

        // The maximum IP datagram size is 65535. Subtract 20 bytes for the IP header and 8 bytes for the UDP header
        // to get the maximum payload.
        private const int MaxPacketSize = 65535 - UdpOverhead;
        private const int UdpOverhead = 20 + 8;

        private readonly EndPoint? _addr;
        private readonly bool _incoming;
        private readonly int _rcvSize;
        private readonly Socket _socket;

        public override ValueTask<(SingleStreamSocket, Endpoint?)> AcceptAsync(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel) => new((this, null));

        public override ValueTask CloseAsync(Exception exception, CancellationToken cancel) => default;

        public override async ValueTask<(SingleStreamSocket, Endpoint)> ConnectAsync(
            Endpoint endpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Debug.Assert(_addr != null);
            try
            {
                await _socket.ConnectAsync(_addr, cancel).ConfigureAwait(false);
                return (this, ((UdpEndpoint)endpoint).Clone(_socket.LocalEndPoint!));
            }
            catch (Exception ex)
            {
                throw new ConnectFailedException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
        }

        public override ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel) =>
            throw new InvalidOperationException();

        public override async ValueTask<ArraySegment<byte>> ReceiveDatagramAsync(CancellationToken cancel)
        {
            int packetSize = Math.Min(MaxPacketSize, _rcvSize - UdpOverhead);
            ArraySegment<byte> buffer = new byte[packetSize];

            int received = 0;
            try
            {
                if (_incoming)
                {
                    EndPoint remoteAddress = new IPEndPoint(
                        _socket.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any,
                        0);

                    // TODO: Use the cancellable API once https://github.com/dotnet/runtime/issues/33418 is fixed
                    SocketReceiveFromResult result =
                        await _socket.ReceiveFromAsync(buffer,
                                                      SocketFlags.None,
                                                      remoteAddress).IceWaitAsync(cancel).ConfigureAwait(false);

                    received = result.ReceivedBytes;
                }
                else
                {
                    received = await _socket.ReceiveAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
                }
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.MessageSize)
            {
                // Ignore and return an empty buffer if the datagram is too large.
            }
            catch (Exception ex) when (cancel.IsCancellationRequested)
            {
                throw new OperationCanceledException(null, ex, cancel);
            }
            catch (Exception e)
            {
                if (e.IsConnectionLost())
                {
                    throw new ConnectionLostException(RetryPolicy.AfterDelay(TimeSpan.Zero));
                }
                throw new TransportException(e, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }

            return buffer.Slice(0, received);
        }

        public override ValueTask<int> SendAsync(IList<ArraySegment<byte>> buffer, CancellationToken cancel) =>
            throw new InvalidOperationException();

        public override async ValueTask<int> SendDatagramAsync(
            IList<ArraySegment<byte>> buffer,
            CancellationToken cancel)
        {
            if (_incoming)
            {
                throw new TransportException("cannot send datagram with incoming connection", RetryPolicy.NoRetry);
            }

            try
            {
                // TODO: Use cancellable API once https://github.com/dotnet/runtime/issues/33417 is fixed.
                return await _socket.SendAsync(buffer, SocketFlags.None).IceWaitAsync(cancel).ConfigureAwait(false);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.MessageSize)
            {
                // Don't retry if the datagram can't be sent because its too large.
                throw new TransportException(ex, RetryPolicy.NoRetry);
            }
            catch (Exception ex) when (cancel.IsCancellationRequested)
            {
                throw new OperationCanceledException(null, ex, cancel);
            }
            catch (Exception ex)
            {
                if (ex.IsConnectionLost())
                {
                    throw new ConnectionLostException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
                }
                throw new TransportException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
        }

        protected override void Dispose(bool disposing) => _socket.Dispose();

        // Only for use by UdpEndpoint.
        internal UdpSocket(Socket socket, ILogger logger, bool isIncoming, EndPoint? addr)
            : base(logger)
        {
            _socket = socket;
            _incoming = isIncoming;
            _rcvSize = _socket.ReceiveBufferSize;
            if (isIncoming)
            {
                MulticastEndpoint = addr as IPEndPoint;
            }
            else
            {
                _addr = addr!;
            }
        }
    }
}
