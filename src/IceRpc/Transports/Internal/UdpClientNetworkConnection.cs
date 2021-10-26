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
            if (!EndpointComparer.ParameterLess.Equals(_remoteEndpoint, remoteEndpoint))
            {
                return false;
            }

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
}
