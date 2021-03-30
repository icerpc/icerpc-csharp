// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    internal sealed class UdpSocket : SingleStreamSocket
    {
        public override Socket Socket { get; }
        public override SslStream? SslStream => null;

        internal IPEndPoint? MulticastAddress { get; private set; }

        // The maximum IP datagram size is 65535. Subtract 20 bytes for the IP header and 8 bytes for the UDP header
        // to get the maximum payload.
        private const int MaxPacketSize = 65535 - UdpOverhead;
        private const int UdpOverhead = 20 + 8;

        private readonly EndPoint? _addr;
        private readonly bool _incoming;
        private readonly int _rcvSize;

        public override ValueTask<SingleStreamSocket> AcceptAsync(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Debug.Assert(false);
            return new((SingleStreamSocket)null!);
        }

        public override ValueTask CloseAsync(Exception exception, CancellationToken cancel) => default;

        public override async ValueTask<SingleStreamSocket> ConnectAsync(
            Endpoint endpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Debug.Assert(_addr != null);
            try
            {
                await Socket.ConnectAsync(_addr, cancel).ConfigureAwait(false);
                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.LogStartSendingDatagrams(
                        endpoint.Transport,
                        Network.LocalAddrToString(Socket),
                        Network.RemoteAddrToString(Socket));
                }
                return this;
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
                        Socket.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any,
                        0);

                    // TODO: Use the cancellable API once https://github.com/dotnet/runtime/issues/33418 is fixed
                    SocketReceiveFromResult result =
                        await Socket.ReceiveFromAsync(buffer,
                                                      SocketFlags.None,
                                                      remoteAddress).WaitAsync(cancel).ConfigureAwait(false);

                    received = result.ReceivedBytes;
                }
                else
                {
                    received = await Socket.ReceiveAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
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
                return await Socket.SendAsync(buffer, SocketFlags.None).WaitAsync(cancel).ConfigureAwait(false);
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

        public override string ToString()
        {
            try
            {
                var sb = new StringBuilder();
                if (_incoming)
                {
                    sb.Append("local address = " + Network.LocalAddrToString(Network.GetLocalAddress(Socket)));
                }
                else
                {
                    sb.Append(Network.SocketToString(Socket));
                }

                if (MulticastAddress != null)
                {
                    sb.Append($"\nmulticast address = {MulticastAddress}");
                }
                return sb.ToString();
            }
            catch (ObjectDisposedException)
            {
                return "<closed>";
            }
        }

        protected override void Dispose(bool disposing) => Socket.Dispose();

        // Only for use by UdpEndpoint.
        internal UdpSocket(Socket socket, ILogger logger, bool isIncoming, EndPoint? addr)
            : base(logger)
        {
            Socket = socket;
            _incoming = isIncoming;
            _rcvSize = Socket.ReceiveBufferSize;
            if (isIncoming)
            {
                MulticastAddress = addr as IPEndPoint;
            }
            else
            {
                _addr = addr!;
            }
        }

        internal override IDisposable? StartScope(Endpoint endpoint)
        {
            // If any of the loggers is enabled we create the scope
            if (Logger.IsEnabled(LogLevel.Critical))
            {
                if (MulticastAddress != null)
                {
                    return Logger.StartMulticastSocketScope(
                        endpoint.Transport,
                        Network.LocalAddrToString(Socket),
                        MulticastAddress.ToString());
                }
                else
                {
                    return Logger.StartDatagramSocketScope(
                        endpoint.Transport,
                        Network.LocalAddrToString(Socket),
                        Network.RemoteAddrToString(Socket));
                }
            }
            return null;
        }
    }
}
