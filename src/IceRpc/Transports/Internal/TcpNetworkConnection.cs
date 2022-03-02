// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace IceRpc.Transports.Internal
{
    internal abstract class TcpNetworkConnection : ISimpleNetworkConnection
    {
        public bool IsSecure => SslStream != null;

        public TimeSpan LastActivity => TimeSpan.FromMilliseconds(_lastActivity);

        internal abstract Socket Socket { get; }
        internal abstract SslStream? SslStream { get; }

        // The MaxDataSize of the SSL implementation.
        private const int MaxSslDataSize = 16 * 1024;
        private long _lastActivity = (long)Time.Elapsed.TotalMilliseconds;
        private readonly List<ArraySegment<byte>> _segments = new();

        public abstract Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel);

        public async ValueTask DisposeAsync()
        {
            if (SslStream is SslStream sslStream)
            {
                await sslStream.DisposeAsync().ConfigureAwait(false);
            }

            // TODO: Write a test case to check why this is necessary to prevent a hang with the Retry_GracefulClose
            // test. Calling Close should be sufficient but for some reasons with this test the peer doesn't detect the
            // socket closure and hangs.
            try
            {
                Socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
                // Ignore, the socket might already be disposed or it might not be connected.
            }
            finally
            {
                Socket.Close();
            }
        }

        public abstract bool HasCompatibleParams(Endpoint remoteEndpoint);

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (buffer.Length == 0)
            {
                throw new ArgumentException($"empty {nameof(buffer)}");
            }

            int received;
            try
            {
                if (SslStream is SslStream sslStream)
                {
                    received = await sslStream.ReadAsync(buffer, cancel).ConfigureAwait(false);
                }
                else
                {
                    received = await Socket.ReceiveAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
                }
            }
            catch (SocketException ex)
            {
                throw ex.ToTransportException(cancel);
            }
            catch (IOException ex)
            {
                throw ex.ToTransportException(cancel);
            }

            if (received == 0)
            {
                throw new ConnectionLostException();
            }

            Interlocked.Exchange(ref _lastActivity, (long)Time.Elapsed.TotalMilliseconds);
            return received;
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

        public async ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
            Debug.Assert(buffers.Count > 0);

            try
            {
                if (SslStream is SslStream sslStream)
                {
                    if (buffers.Count == 1)
                    {
                        await sslStream.WriteAsync(buffers[0], cancel).ConfigureAwait(false);
                    }
                    else
                    {
                        // Coalesce leading small buffers up to MaxSslDataSize. We assume buffers later on are
                        // large enough and don't need coalescing.
                        int index = 0;
                        int writeBufferSize = 0;
                        do
                        {
                            ReadOnlyMemory<byte> buffer = buffers[index];
                            if (writeBufferSize + buffer.Length < MaxSslDataSize)
                            {
                                index++;
                                writeBufferSize += buffer.Length;
                            }
                            else
                            {
                                break; // while
                            }
                        } while (index < buffers.Count);

                        if (index == 1)
                        {
                            // There is no point copying only the first buffer into another buffer.
                            index = 0;
                        }
                        else if (writeBufferSize > 0)
                        {
                            using IMemoryOwner<byte> writeBufferOwner = MemoryPool<byte>.Shared.Rent(writeBufferSize);
                            Memory<byte> writeBuffer = writeBufferOwner.Memory[0..writeBufferSize];
                            int offset = 0;
                            for (int i = 0; i < index; ++i)
                            {
                                ReadOnlyMemory<byte> buffer = buffers[i];
                                buffer.CopyTo(writeBuffer[offset..]);
                                offset += buffer.Length;
                            }

                            // Send the "coalesced" initial buffers
                            await sslStream.WriteAsync(writeBuffer, cancel).ConfigureAwait(false);
                        }

                        // Send the remaining buffers one by one
                        for (int i = index; i < buffers.Count; ++i)
                        {
                            await sslStream.WriteAsync(buffers[i], cancel).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    if (buffers.Count == 1)
                    {
                        await Socket.SendAsync(buffers[0], SocketFlags.None, cancel).ConfigureAwait(false);
                    }
                    else
                    {
                        _segments.Clear();
                        foreach (ReadOnlyMemory<byte> memory in buffers)
                        {
                            if (MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
                            {
                                _segments.Add(segment);
                            }
                            else
                            {
                                throw new ArgumentException(
                                    $"{nameof(buffers)} are not backed by arrays",
                                    nameof(buffers));
                            }
                        }
                        await Socket.SendAsync(_segments, SocketFlags.None).WaitAsync(cancel).ConfigureAwait(false);
                    }
                }

                // TODO: should we update _lastActivity when an exception is thrown?
                Interlocked.Exchange(ref _lastActivity, (long)Time.Elapsed.TotalMilliseconds);
            }
            catch (SocketException ex)
            {
                throw ex.ToTransportException(cancel);
            }
            catch (IOException ex)
            {
                throw ex.ToTransportException(cancel);
            }
        }

        /// <summary>Prints the fields/properties of this class using the Records format.</summary>
        /// <param name="builder">The string builder.</param>
        /// <returns><c>true</c>when members are appended to the builder; otherwise, <c>false</c>.</returns>
        private protected virtual bool PrintMembers(StringBuilder builder)
        {
            builder.Append("LocalEndPoint = ").Append(Socket.LocalEndPoint).Append(", ");
            builder.Append("RemoteEndPoint = ").Append(Socket.RemoteEndPoint);
            return true;
        }
    }

    internal class TcpClientNetworkConnection : TcpNetworkConnection
    {
        internal override Socket Socket { get; }
        internal override SslStream? SslStream => _sslStream;

        private readonly EndPoint _addr;
        private readonly SslClientAuthenticationOptions? _authenticationOptions;

        private bool _connected;
        private readonly TimeSpan _idleTimeout;

        private readonly Endpoint _remoteEndpoint;
        private SslStream? _sslStream;

        public override async Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel)
        {
            Debug.Assert(!_connected);
            _connected = true;

            Endpoint remoteEndpoint = _remoteEndpoint;

            SslClientAuthenticationOptions? authenticationOptions = null;
            if (_authenticationOptions != null)
            {
                // Add the endpoint protocol to the SSL application protocols (used by TLS ALPN) and set the
                // TargetHost to the endpoint host. On the client side, the application doesn't necessarily
                // need to provide authentication options if it relies on system certificates and doesn't
                // specific certificate validation so it's fine for _authenticationOptions to be
                // null.
                authenticationOptions = _authenticationOptions.Clone();
                authenticationOptions.TargetHost ??= remoteEndpoint.Host;
                authenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol>
                {
                    new SslApplicationProtocol(remoteEndpoint.Protocol.Name)
                };
            }

            try
            {
                Debug.Assert(Socket != null);

                // Connect to the peer.
                await Socket.ConnectAsync(_addr, cancel).ConfigureAwait(false);

                if (authenticationOptions != null)
                {
                    // This can only be created with a connected socket.
                    _sslStream = new SslStream(new System.Net.Sockets.NetworkStream(Socket, false), false);
                    await _sslStream.AuthenticateAsClientAsync(authenticationOptions, cancel).ConfigureAwait(false);
                }

                var ipEndPoint = (IPEndPoint)Socket.LocalEndPoint!;

                return new NetworkConnectionInformation(
                    localEndpoint: remoteEndpoint with
                    {
                        Host = ipEndPoint.Address.ToString(),
                        Port = checked((ushort)ipEndPoint.Port)
                    },
                    remoteEndpoint: remoteEndpoint,
                    _idleTimeout,
                    _sslStream?.RemoteCertificate);
            }
            catch (SocketException ex)
            {
                throw ex.ToConnectFailedException(cancel);
            }
            catch (IOException ex)
            {
                throw ex.ToConnectFailedException(cancel);
            }
        }

        public override bool HasCompatibleParams(Endpoint remoteEndpoint)
        {
            if (!EndpointComparer.ParameterLess.Equals(_remoteEndpoint, remoteEndpoint))
            {
                return false;
            }

            _ = remoteEndpoint.ParseTcpParams(); // check remote endpoint

            return true;
        }

        internal TcpClientNetworkConnection(
            Endpoint remoteEndpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            TcpClientTransportOptions options)
        {
            _ = remoteEndpoint.ParseTcpParams(); // sanity check

            if (remoteEndpoint.Params.TryGetValue("transport", out string? endpointTransport))
            {
                if (endpointTransport == TransportNames.Ssl)
                {
                    // With ssl, we always "turn on" SSL
                    authenticationOptions ??= new SslClientAuthenticationOptions();
                }
            }
            else
            {
                remoteEndpoint = remoteEndpoint with
                {
                    Params = remoteEndpoint.Params.Add("transport", TransportNames.Tcp)
                };
            }

            _remoteEndpoint = remoteEndpoint;

            _addr = IPAddress.TryParse(_remoteEndpoint.Host, out IPAddress? ipAddress) ?
                new IPEndPoint(ipAddress, _remoteEndpoint.Port) :
                new DnsEndPoint(_remoteEndpoint.Host, _remoteEndpoint.Port);

            // We still specify the address family for the socket if an address is set to ensure an IPv4 socket is
            // created if the address is an IPv4 address.
            Socket = ipAddress == null ?
                new Socket(SocketType.Stream, ProtocolType.Tcp) :
                new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                if (ipAddress?.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    Socket.DualMode = !options.IsIPv6Only;
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

                Socket.NoDelay = true;
            }
            catch (SocketException ex)
            {
                Socket.Dispose();
                throw new TransportException(ex);
            }

            _authenticationOptions = authenticationOptions;
            _idleTimeout = options.IdleTimeout;
        }
    }

    internal class TcpServerNetworkConnection : TcpNetworkConnection
    {
        internal override Socket Socket { get; }

        internal override SslStream? SslStream => _sslStream;

        private readonly SslServerAuthenticationOptions? _authenticationOptions;
        private bool _connected;
        private readonly TimeSpan _idleTimeout;
        private readonly Endpoint _localEndpoint;
        private SslStream? _sslStream;

        public override async Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel)
        {
            Debug.Assert(!_connected);
            _connected = true;

            try
            {
                if (_authenticationOptions != null)
                {
                    // This can only be created with a connected socket.
                    _sslStream = new SslStream(new System.Net.Sockets.NetworkStream(Socket, false), false);
                    await _sslStream.AuthenticateAsServerAsync(_authenticationOptions, cancel).ConfigureAwait(false);
                }

                var ipEndPoint = (IPEndPoint)Socket.RemoteEndPoint!;

                return new NetworkConnectionInformation(
                    localEndpoint: _localEndpoint,
                    remoteEndpoint: _localEndpoint with
                    {
                        Host = ipEndPoint.Address.ToString(),
                        Port = checked((ushort)ipEndPoint.Port),
                        Params = _localEndpoint.Params
                    },
                    _idleTimeout,
                    _sslStream?.RemoteCertificate);
            }
            catch (SocketException ex)
            {
                throw ex.ToConnectFailedException(cancel);
            }
            catch (IOException ex)
            {
                throw ex.ToConnectFailedException(cancel);
            }
        }

        public override bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            throw new NotSupportedException($"{nameof(HasCompatibleParams)} is only supported by client connections.");

        internal TcpServerNetworkConnection(
            Socket socket,
            Endpoint localEndpoint,
            TimeSpan idleTimeout,
            SslServerAuthenticationOptions? authenticationOptions)
        {
            Socket = socket;
            _authenticationOptions = authenticationOptions;
            _idleTimeout = idleTimeout;
            _localEndpoint = localEndpoint;
        }
    }
}
