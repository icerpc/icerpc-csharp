// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace IceRpc.Transports.Internal
{
    internal abstract class TcpNetworkConnection : INetworkConnection, ISimpleStream
    {
        int ISimpleStream.DatagramMaxReceiveSize => throw new InvalidOperationException();
        bool ISimpleStream.IsDatagram => false;
        bool INetworkConnection.IsSecure => SslStream != null;

        TimeSpan INetworkConnection.LastActivity => TimeSpan.FromMilliseconds(_lastActivity);

        internal Socket Socket { get; }
        private protected SslStream? SslStream { get; set; }

        // The MaxDataSize of the SSL implementation.
        private const int MaxSslDataSize = 16 * 1024;

        private long _lastActivity = (long)Time.Elapsed.TotalMilliseconds;

        void INetworkConnection.Close(Exception? exception)
        {
            SslStream?.Dispose();
            Socket.Dispose();
        }

        public abstract bool HasCompatibleParams(Endpoint remoteEndpoint);

        async ValueTask<int> ISimpleStream.ReadAsync(Memory<byte> buffer, CancellationToken cancel)
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
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(cancel));
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

        async ValueTask ISimpleStream.WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
            Debug.Assert(buffers.Length > 0);

            try
            {
                if (SslStream is SslStream sslStream)
                {
                    if (buffers.Length == 1)
                    {
                        await sslStream.WriteAsync(buffers.Span[0], cancel).ConfigureAwait(false);
                    }
                    else
                    {
                        // Coalesce leading small buffers up to MaxSslDataSize. We assume buffers later on are
                        // large enough and don't need coalescing.
                        int index = 0;
                        int writeBufferSize = 0;
                        do
                        {
                            ReadOnlyMemory<byte> buffer = buffers.Span[index];
                            if (writeBufferSize + buffer.Length < MaxSslDataSize)
                            {
                                index++;
                                writeBufferSize += buffer.Length;
                            }
                            else
                            {
                                break; // while
                            }
                        } while (index < buffers.Length);

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
                                ReadOnlyMemory<byte> buffer = buffers.Span[index];
                                buffer.CopyTo(writeBuffer[offset..]);
                                offset += buffer.Length;
                            }
                            // Send the "coalesced" initial buffers
                            await sslStream.WriteAsync(writeBuffer, cancel).ConfigureAwait(false);
                        }

                        // Send the remaining buffers one by one
                        for (int i = index; i < buffers.Length; ++i)
                        {
                            await sslStream.WriteAsync(buffers.Span[i], cancel).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    if (buffers.Length == 1)
                    {
                        await Socket.SendAsync(buffers.Span[0], SocketFlags.None, cancel).ConfigureAwait(false);
                    }
                    else
                    {
                        await Socket.SendAsync(
                            buffers.ToSegmentList(),
                            SocketFlags.None).WaitAsync(cancel).ConfigureAwait(false);
                    }
                }

                // TODO: should we update _lastActivity when an exception is thrown?
                Interlocked.Exchange(ref _lastActivity, (long)Time.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(cancel));
            }
        }

        private protected TcpNetworkConnection(Socket socket) => Socket = socket;

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

    internal class TcpClientNetworkConnection : TcpNetworkConnection, ISimpleNetworkConnection
    {
        private readonly EndPoint _addr;
        private readonly SslClientAuthenticationOptions? _authenticationOptions;
        private readonly Endpoint _remoteEndpoint;
        private readonly TimeSpan _idleTimeout;

        async Task<(ISimpleStream, NetworkConnectionInformation)> ISimpleNetworkConnection.ConnectAsync(
            CancellationToken cancel)
        {
            bool? tls = _remoteEndpoint.ParseTcpParams().Tls;

            Endpoint remoteEndpoint = _remoteEndpoint;

            if (tls == null)
            {
                // TODO: add ability to override this default tls=true through some options
                tls = true;
                remoteEndpoint = remoteEndpoint with
                {
                    Params = remoteEndpoint.Params.Add(new EndpointParam("tls", "true"))
                };
            }

            SslClientAuthenticationOptions? authenticationOptions = null;
            if (tls == true)
            {
                // Add the endpoint protocol to the SSL application protocols (used by TLS ALPN) and set the
                // TargetHost to the endpoint host. On the client side, the application doesn't necessarily
                // need to provide authentication options if it relies on system certificates and doesn't
                // specific specific certificate validation so it's fine for _authenticationOptions to be
                // null.
                authenticationOptions = _authenticationOptions?.Clone() ?? new();
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

                if (tls == true)
                {
                    // This can only be created with a connected socket.
                    SslStream = new SslStream(new System.Net.Sockets.NetworkStream(Socket, false), false);
                    try
                    {
                        await SslStream.AuthenticateAsClientAsync(authenticationOptions!, cancel).ConfigureAwait(false);
                    }
                    catch (AuthenticationException ex)
                    {
                        throw new TransportException(ex);
                    }
                    catch (Exception ex)
                    {
                        throw ExceptionUtil.Throw(ex.ToTransportException(default));
                    }
                }

                var ipEndPoint = (IPEndPoint)Socket.LocalEndPoint!;

                return (this,
                        new NetworkConnectionInformation(
                            localEndpoint: remoteEndpoint with
                                {
                                    Host = ipEndPoint.Address.ToString(),
                                    Port = checked((ushort)ipEndPoint.Port)
                                },
                            remoteEndpoint: remoteEndpoint,
                            _idleTimeout,
                            SslStream?.RemoteCertificate));
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                throw new ConnectionRefusedException(ex);
            }
            catch (SocketException ex)
            {
                throw new ConnectFailedException(ex);
            }
            catch (Exception ex) when (cancel.IsCancellationRequested)
            {
                throw new OperationCanceledException(null, ex, cancel);
            }
            catch (Exception ex)
            {
                throw new TransportException(ex);
            }
        }

        public override bool HasCompatibleParams(Endpoint remoteEndpoint)
        {
            if (!EndpointComparer.ParameterLess.Equals(_remoteEndpoint, remoteEndpoint))
            {
                return false;
            }

            bool? tls = remoteEndpoint.ParseTcpParams().Tls;

            // A remote endpoint with no tls parameter is compatible with an established connection no matter
            // its tls disposition.
            return tls == null || tls == (SslStream != null);
        }

        internal TcpClientNetworkConnection(
            Socket socket,
            Endpoint remoteEndpoint,
            TimeSpan idleTimeout,
            SslClientAuthenticationOptions? authenticationOptions,
            EndPoint addr)
           : base(socket)
        {
            _addr = addr;
            _authenticationOptions = authenticationOptions;
            _idleTimeout = idleTimeout;
            _remoteEndpoint = remoteEndpoint;
        }
    }

    internal class TcpServerNetworkConnection : TcpNetworkConnection, ISimpleNetworkConnection
    {
        // See https://tools.ietf.org/html/rfc5246#appendix-A.4
        private const byte TlsHandshakeRecord = 0x16;
        private readonly SslServerAuthenticationOptions? _authenticationOptions;

        private readonly TimeSpan _idleTimeout;
        private readonly Endpoint _localEndpoint;

        async Task<(ISimpleStream, NetworkConnectionInformation)> ISimpleNetworkConnection.ConnectAsync(
            CancellationToken cancel)
        {
            // TODO: why are we doing this parsing for every single accepted connection?
            bool? tls = _localEndpoint.ParseTcpParams().Tls;
            try
            {
                bool secure;
                if (tls == false)
                {
                    // Don't establish a secure connection is the tls param is explicitly set to false.
                    secure = false;
                }
                else if (_authenticationOptions != null)
                {
                    // On the server side, if the tls parameter is not set, the TCP socket checks the first
                    // byte sent by the peer to figure out if the peer tries to establish a TLS connection.
                    if (tls == null)
                    {
                        // Peek one byte into the tcp stream to see if it contains the TLS handshake record
                        Memory<byte> buffer = new byte[1];
                        if (await Socket.ReceiveAsync(buffer, SocketFlags.Peek, cancel).ConfigureAwait(false) == 0)
                        {
                            throw new ConnectionLostException();
                        }
                        secure = buffer.Span[0] == TlsHandshakeRecord;
                    }
                    else
                    {
                        // Otherwise, assume a secure connection.
                        secure = true;
                    }
                }
                else
                {
                    // Authentication options are not set and the tls param is not explicitly set to false, we
                    // throw because we can't establish a secure connection without authentication options.
                    throw new InvalidOperationException(
                        "cannot establish TLS connection: no TLS authentication options configured");
                }

                // If a secure connection is needed, create and authentication the SslStream.
                if (secure)
                {
                    Debug.Assert(_authenticationOptions != null);

                    // This can only be created with a connected socket.
                    SslStream = new SslStream(new System.Net.Sockets.NetworkStream(Socket, false), false);
                    try
                    {
                        await SslStream.AuthenticateAsServerAsync(_authenticationOptions, cancel).ConfigureAwait(false);
                    }
                    catch (AuthenticationException ex)
                    {
                        throw new TransportException(ex);
                    }
                    catch (Exception ex)
                    {
                        throw ExceptionUtil.Throw(ex.ToTransportException(default));
                    }
                }

                ImmutableList<EndpointParam> endpointParams = _localEndpoint.Params;
                if (tls == null)
                {
                    // the accepted endpoint gets a tls parameter
                    endpointParams = endpointParams.Add(new EndpointParam("tls", SslStream == null ? "false" : "true"));
                }

                var ipEndPoint = (IPEndPoint)Socket.RemoteEndPoint!;

                return (this,
                        new NetworkConnectionInformation(
                            localEndpoint: _localEndpoint,
                            remoteEndpoint: _localEndpoint with
                                {
                                    Host = ipEndPoint.Address.ToString(),
                                    Port = checked((ushort)ipEndPoint.Port),
                                    Params = endpointParams
                                },
                            _idleTimeout,
                            SslStream?.RemoteCertificate));
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(cancel));
            }
        }

        public override bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            throw new NotSupportedException($"{nameof(HasCompatibleParams)} is only supported by client connections.");

        internal TcpServerNetworkConnection(
            Socket socket,
            Endpoint localEndpoint,
            TimeSpan idleTimeout,
            SslServerAuthenticationOptions? authenticationOptions)
           : base(socket)
        {
           _authenticationOptions = authenticationOptions;
           _idleTimeout = idleTimeout;
           _localEndpoint = localEndpoint;
        }
    }
}
