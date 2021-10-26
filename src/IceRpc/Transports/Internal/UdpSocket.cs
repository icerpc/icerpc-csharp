// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    internal class UdpClientNetworkConnection : ISimpleNetworkConnection, ISimpleStream
    {
        public int DatagramMaxReceiveSize { get; }
        bool ISimpleStream.IsDatagram => true;
        bool INetworkConnection.IsSecure => false;

        TimeSpan INetworkConnection.LastActivity => TimeSpan.FromMilliseconds(_lastActivity);

        internal Socket Socket { get; }
        private readonly EndPoint _addr;
        private readonly Endpoint _remoteEndpoint;
        private readonly TimeSpan _idleTimeout;
        private long _lastActivity = (long)Time.Elapsed.TotalMilliseconds;

        private readonly string? _multicastInterface;
        private readonly int _ttl;

        void INetworkConnection.Close(Exception? exception) => Socket.Close();

        async Task<(ISimpleStream, NetworkConnectionInformation)> ISimpleNetworkConnection.ConnectAsync(
            CancellationToken cancel)
        {
            try
            {
                await Socket.ConnectAsync(_addr, cancel).ConfigureAwait(false);
                var ipEndPoint = (IPEndPoint)Socket.LocalEndPoint!;

                return (this,
                        new NetworkConnectionInformation(
                            localEndpoint: _remoteEndpoint with
                                {
                                    Host = ipEndPoint.Address.ToString(),
                                    Port = checked((ushort)ipEndPoint.Port)
                                },
                            remoteEndpoint: _remoteEndpoint,
                            _idleTimeout,
                            remoteCertificate: null));
            }
            catch (Exception ex)
            {
                throw new ConnectFailedException(ex);
            }
        }

        bool INetworkConnection.HasCompatibleParams(Endpoint remoteEndpoint)
        {
            (_, int ttl, string? multicastInterface) = remoteEndpoint.ParseUdpParams();
            return ttl == _ttl && multicastInterface == _multicastInterface;
        }

        async ValueTask<int> ISimpleStream.ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            try
            {
                int received = await Socket.ReceiveAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
                Interlocked.Exchange(ref _lastActivity, (long)Time.Elapsed.TotalMilliseconds);
                return received;
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(cancel));
            }
        }

        async ValueTask ISimpleStream.WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
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
                    await Socket.SendAsync(writeBufferOwner.Memory[0..size],
                                           SocketFlags.None,
                                           cancel).ConfigureAwait(false);
                }
                Interlocked.Exchange(ref _lastActivity, (long)Time.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(cancel));
            }
        }

        internal UdpClientNetworkConnection(
            Socket socket,
            Endpoint remoteEndpoint,
            TimeSpan idleTimeout,
            EndPoint addr,
            int ttl,
            string? multicastInterface = null)
        {
            DatagramMaxReceiveSize = Math.Min(UdpUtils.MaxPacketSize, socket.ReceiveBufferSize - UdpUtils.UdpOverhead);
            Socket = socket;

            _addr = addr;
            _idleTimeout = idleTimeout;
            _multicastInterface = multicastInterface;
            _remoteEndpoint = remoteEndpoint;
            _ttl = ttl;
        }
    }

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
                                                              Timeout.InfiniteTimeSpan,
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

        ValueTask ISimpleStream.WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel) =>
            throw new TransportException("cannot write to a UDP server stream");

        internal UdpServerNetworkConnection(Socket socket, Endpoint localEndpoint)
        {
            DatagramMaxReceiveSize = Math.Min(UdpUtils.MaxPacketSize, socket.ReceiveBufferSize - UdpUtils.UdpOverhead);
            Socket = socket;
            _localEndpoint = localEndpoint;
        }
    }
}
