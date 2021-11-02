// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

using static IceRpc.Transports.Internal.UdpUtils;

namespace IceRpc.Transports.Internal
{
    internal abstract class UdpNetworkConnection : ISimpleNetworkConnection
    {
        public bool IsSecure => false;

        public abstract TimeSpan LastActivity { get; }

        public void Dispose() => Socket.Close();

        public abstract Task<(ISimpleStream, NetworkConnectionInformation)> ConnectAsync(CancellationToken cancel);

        public abstract bool HasCompatibleParams(Endpoint remoteEndpoint);

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

        internal abstract Socket Socket { get; }

        /// <summary>Prints the fields/properties of this class using the Records format.</summary>
        /// <param name="builder">The string builder.</param>
        /// <returns><c>true</c>when members are appended to the builder; otherwise, <c>false</c>.</returns>
        private protected abstract bool PrintMembers(StringBuilder builder);
    }

    internal class UdpClientNetworkConnection : UdpNetworkConnection, ISimpleStream
    {
        public override TimeSpan LastActivity => TimeSpan.FromMilliseconds(_lastActivity);

        internal override Socket Socket { get; }
        private readonly EndPoint _addr;
        private readonly Endpoint _remoteEndpoint;
        private readonly TimeSpan _idleTimeout;
        private long _lastActivity = (long)Time.Elapsed.TotalMilliseconds;

        private readonly string? _multicastInterface;
        private readonly int _ttl;

        public override async Task<(ISimpleStream, NetworkConnectionInformation)> ConnectAsync(CancellationToken cancel)
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

        public override bool HasCompatibleParams(Endpoint remoteEndpoint)
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

        internal UdpClientNetworkConnection(Endpoint remoteEndpoint, UdpOptions options)
        {
            // udp is a special transport that requires "udp" endpoints.
            if (remoteEndpoint.Transport != TransportNames.Udp)
            {
                throw new ArgumentException($"cannot use UDP transport with endpoint '{remoteEndpoint}'",
                                            nameof(remoteEndpoint));
            }

            (bool _, _ttl, _multicastInterface) = remoteEndpoint.ParseUdpParams();

            _idleTimeout = options.IdleTimeout;
            _remoteEndpoint = remoteEndpoint;

            _addr = IPAddress.TryParse(remoteEndpoint.Host, out IPAddress? ipAddress) ?
                new IPEndPoint(ipAddress, remoteEndpoint.Port) :
                new DnsEndPoint(remoteEndpoint.Host, remoteEndpoint.Port);

            if (_multicastInterface == "*")
            {
                throw new NotSupportedException(
                    $"endpoint '{remoteEndpoint}' cannot use interface '*' to send datagrams");
            }

            Socket = ipAddress == null ?
                new Socket(SocketType.Dgram, ProtocolType.Udp) :
                new Socket(ipAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                if (_addr is IPEndPoint ipEndpoint && IsMulticast(ipEndpoint.Address))
                {
                    if (ipAddress?.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        Socket.DualMode = !options.IsIPv6Only;
                    }

                    // IP multicast socket options require a socket created with the correct address family.
                    if (_multicastInterface != null)
                    {
                        Debug.Assert(_multicastInterface.Length > 0);
                        if (ipAddress?.AddressFamily == AddressFamily.InterNetwork)
                        {
                            Socket.SetSocketOption(
                                SocketOptionLevel.IP,
                                SocketOptionName.MulticastInterface,
                                GetIPv4InterfaceAddress(_multicastInterface).GetAddressBytes());
                        }
                        else
                        {
                            Socket.SetSocketOption(
                                SocketOptionLevel.IPv6,
                                SocketOptionName.MulticastInterface,
                                GetIPv6InterfaceIndex(_multicastInterface));
                        }
                    }

                    if (_ttl != -1)
                    {
                        Socket.Ttl = (short)_ttl;
                    }
                }

                if (options.LocalEndPoint is IPEndPoint localEndPoint)
                {
                    Socket.Bind(localEndPoint);
                }

                if (options.ReceiveBufferSize is int receiveSize)
                {
                    Socket.ReceiveBufferSize = receiveSize;
                }
                if (options.SendBufferSize is int sendSize)
                {
                    Socket.SendBufferSize = sendSize;
                }
            }
            catch (SocketException ex)
            {
                Socket.Dispose();
                throw new TransportException(ex);
            }
        }

        private protected override bool PrintMembers(StringBuilder builder)
        {
            builder.Append("LocalEndPoint = ").Append(Socket.LocalEndPoint).Append(", ");
            builder.Append("RemoteEndPoint = ").Append(Socket.RemoteEndPoint);
            return true;
        }
    }

    internal class UdpServerNetworkConnection : UdpNetworkConnection, ISimpleStream
    {
        public override TimeSpan LastActivity => TimeSpan.FromMilliseconds(_lastActivity);

        internal Endpoint LocalEndpoint { get; }

        internal override Socket Socket { get; }
        private long _lastActivity = (long)Time.Elapsed.TotalMilliseconds;

        private readonly IPEndPoint? _multicastAddress;

        private readonly EndPoint _remoteAddress;

        public override Task<(ISimpleStream, NetworkConnectionInformation)> ConnectAsync(CancellationToken cancel) =>
            // The remote endpoint is set to an empty endpoint for a UDP server connection because the
            // socket accepts datagrams from "any" client since it's not connected to a specific client.
            Task.FromResult((this as ISimpleStream,
                             new NetworkConnectionInformation(localEndpoint: LocalEndpoint,
                                                              remoteEndpoint: LocalEndpoint with
                                                              {
                                                                 Host = "::0",
                                                                 Port = 0
                                                              },
                                                              TimeSpan.MaxValue, // TODO: returning Infinite doesn't work
                                                              remoteCertificate: null)));

        public override bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            throw new NotSupportedException(
                $"{nameof(INetworkConnection.HasCompatibleParams)} is only supported by client connections.");

        async ValueTask<int> ISimpleStream.ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            try
            {
                SocketReceiveFromResult result = await Socket.ReceiveFromAsync(buffer,
                                                                               SocketFlags.None,
                                                                               _remoteAddress,
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

        internal UdpServerNetworkConnection(Endpoint endpoint, UdpOptions options)
        {
            // udp is a special transport that requires "udp" endpoints.
            if (endpoint.Transport != TransportNames.Udp)
            {
                throw new ArgumentException($"cannot use UDP transport with endpoint '{endpoint}'",
                                            nameof(endpoint));
            }

            if (!IPAddress.TryParse(endpoint.Host, out IPAddress? ipAddress))
            {
                throw new NotSupportedException(
                    $"endpoint '{endpoint}' cannot accept datagrams because it has a DNS name");
            }

            string? multicastInterface = endpoint.ParseUdpParams().MulticastInterface;

            _multicastAddress = null;
            Socket = new Socket(ipAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    // TODO: Don't enable DualMode sockets on macOS, https://github.com/dotnet/corefx/issues/31182
                    Socket.DualMode = !(OperatingSystem.IsMacOS() || options.IsIPv6Only);

                    _remoteAddress = new IPEndPoint(IPAddress.IPv6Any, 0);
                }
                else
                {
                    _remoteAddress = new IPEndPoint(IPAddress.Any, 0);
                }

                Socket.ExclusiveAddressUse = true;

                if (options.ReceiveBufferSize is int receiveSize)
                {
                    Socket.ReceiveBufferSize = receiveSize;
                }
                if (options.SendBufferSize is int sendSize)
                {
                    Socket.SendBufferSize = sendSize;
                }

                var addr = new IPEndPoint(ipAddress, endpoint.Port);
                if (IsMulticast(ipAddress))
                {
                    _multicastAddress = addr;

                    Socket.ExclusiveAddressUse = false;
                    Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                    if (OperatingSystem.IsWindows())
                    {
                        // Windows does not allow binding to the multicast address itself so we bind to the wildcard
                        // instead. As a result, bidirectional connection won't work because the source address won't
                        // be the multicast address and the client will therefore reject the datagram.
                        addr = new IPEndPoint(
                            addr.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any,
                            addr.Port);
                    }
                }

                Socket.Bind(addr);

                int port = ((IPEndPoint)Socket.LocalEndPoint!).Port;

                if (_multicastAddress != null)
                {
                    _multicastAddress.Port = port;
                    SetMulticastGroup(Socket, multicastInterface, _multicastAddress.Address);
                }

                LocalEndpoint = endpoint with { Port = checked((ushort)port) };
            }
            catch (SocketException ex)
            {
                Socket.Dispose();
                throw new TransportException(ex);
            }
            catch
            {
                Socket.Dispose();
                throw;
            }
        }

        private protected override bool PrintMembers(StringBuilder builder)
        {
            builder.Append("LocalEndPoint = ").Append(Socket.LocalEndPoint);
            if (_multicastAddress is IPEndPoint multicastAddress)
            {
                builder.Append(", MulticastAddress = ").Append(multicastAddress);
            }
            return true;
        }
    }
}
