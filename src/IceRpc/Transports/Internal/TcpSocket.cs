// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
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
    internal abstract class TcpSocket : NetworkSocket
    {
        public override bool IsDatagram => false;

        protected internal override Socket Socket { get; }

        // The MaxDataSize of the SSL implementation.
        private const int MaxSslDataSize = 16 * 1024;

        public override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
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
            return received;
        }

        public override async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
        {
            if (cancel.CanBeCanceled)
            {
                throw new NotSupportedException(
                    $"{nameof(SendAsync)} on a tcp connection does not support cancellation");
            }

            try
            {
                if (SslStream is SslStream sslStream)
                {
                    await sslStream.WriteAsync(buffer, cancel).ConfigureAwait(false);
                }
                else
                {
                    await Socket.SendAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
                }
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
            Debug.Assert(buffers.Length > 0);

            if (cancel.CanBeCanceled)
            {
                throw new NotSupportedException(
                    $"{nameof(SendAsync)} on a tcp connection does not support cancellation");
            }

            if (buffers.Length == 1)
            {
                await SendAsync(buffers.Span[0], cancel).ConfigureAwait(false);
            }
            else
            {
                if (SslStream == null)
                {
                    try
                    {
                        await Socket.SendAsync(buffers.ToSegmentList(), SocketFlags.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        throw ExceptionUtil.Throw(ex.ToTransportException(cancel));
                    }
                }
                else
                {
                    // Coalesce leading small buffers up to MaxSslDataSize. We assume buffers later on are large enough
                    // and don't need coalescing.
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
                        await SendAsync(writeBuffer, cancel).ConfigureAwait(false);
                    }

                    // Send the remaining buffers one by one
                    for (int i = index; i < buffers.Length; ++i)
                    {
                        await SendAsync(buffers.Span[i], cancel).ConfigureAwait(false);
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            Socket.Dispose();
            SslStream?.Dispose();
        }

        protected override bool PrintMembers(StringBuilder builder)
        {
            if (base.PrintMembers(builder))
            {
                builder.Append(", ");
            }
            builder.Append("LocalEndPoint = ").Append(Socket.LocalEndPoint).Append(", ");
            builder.Append("RemoteEndPoint = ").Append(Socket.RemoteEndPoint);
            return true;
        }

        internal TcpSocket(Socket fd, ILogger logger)
            : base(logger) =>
            // The socket is not connected if a client socket, it's connected otherwise.
            Socket = fd;
    }

    internal class TcpClientSocket : TcpSocket
    {
        private readonly EndPoint _addr;
        private readonly SslClientAuthenticationOptions? _authenticationOptions;

        public override async ValueTask<Endpoint> ConnectAsync(Endpoint endpoint, CancellationToken cancel)
        {
            bool? tls = endpoint.ParseTcpParams().Tls;

            if (tls == null)
            {
                // TODO: add ability to override this default tls=true through some options
                tls = true;
                endpoint = endpoint with
                {
                    Params = endpoint.Params.Add(new EndpointParam("tls", "true"))
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
                authenticationOptions.TargetHost ??= endpoint.Host;
                authenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol>
                    {
                        new SslApplicationProtocol(endpoint.Protocol.GetName())
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
                    SslStream = new SslStream(new NetworkStream(Socket, false), false);
                    try
                    {
                        await SslStream.AuthenticateAsClientAsync(authenticationOptions!, cancel).ConfigureAwait(false);
                    }
                    catch (AuthenticationException ex)
                    {
                        Logger.LogTlsAuthenticationFailed(ex);
                        throw new TransportException(ex);
                    }
                    catch (Exception ex)
                    {
                        throw ExceptionUtil.Throw(ex.ToTransportException(default));
                    }

                    Logger.LogTlsAuthenticationSucceeded(SslStream);
                }

                var ipEndPoint = (IPEndPoint)Socket.LocalEndPoint!;
                return endpoint with { Host = ipEndPoint.Address.ToString(), Port = checked((ushort)ipEndPoint.Port) };
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
            bool? tls = remoteEndpoint.ParseTcpParams().Tls;

            // A remote endpoint with no tls parameter is compatible with an established connection no matter
            // its tls disposition.
            return tls == null || tls == (SslStream != null);
        }

        internal TcpClientSocket(
            Socket fd,
            ILogger logger,
            SslClientAuthenticationOptions? authenticationOptions,
            EndPoint addr)
           : base(fd, logger)
        {
            _authenticationOptions = authenticationOptions;
            _addr = addr;
        }
    }

    internal class TcpServerSocket : TcpSocket
    {
        // See https://tools.ietf.org/html/rfc5246#appendix-A.4
        private const byte TlsHandshakeRecord = 0x16;

        private readonly SslServerAuthenticationOptions? _authenticationOptions;

        public override async ValueTask<Endpoint> ConnectAsync(Endpoint endpoint, CancellationToken cancel)
        {
            bool? tls = endpoint.ParseTcpParams().Tls;
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
                    SslStream = new SslStream(new NetworkStream(Socket, false), false);
                    try
                    {
                        await SslStream.AuthenticateAsServerAsync(_authenticationOptions, cancel).ConfigureAwait(false);
                    }
                    catch (AuthenticationException ex)
                    {
                        Logger.LogTlsAuthenticationFailed(ex);
                        throw new TransportException(ex);
                    }
                    catch (Exception ex)
                    {
                        throw ExceptionUtil.Throw(ex.ToTransportException(default));
                    }

                    Logger.LogTlsAuthenticationSucceeded(SslStream);
                }

                ImmutableList<EndpointParam> endpointParams = endpoint.Params;
                if (tls == null)
                {
                    // the accepted endpoint gets a tls parameter
                    endpointParams = endpointParams.Add(new EndpointParam("tls", SslStream == null ? "false" : "true"));
                }

                var ipEndPoint = (IPEndPoint)Socket.RemoteEndPoint!;
                return endpoint with
                {
                    Host = ipEndPoint.Address.ToString(),
                    Port = checked((ushort)ipEndPoint.Port),
                    Params = endpointParams
                };
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(cancel));
            }
        }

        public override bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            throw new NotSupportedException($"{nameof(HasCompatibleParams)} is only supported by client sockets.");

        internal TcpServerSocket(
            Socket fd,
            ILogger logger,
            SslServerAuthenticationOptions? authenticationOptions)
           : base(fd, logger) => _authenticationOptions = authenticationOptions;
    }
}
