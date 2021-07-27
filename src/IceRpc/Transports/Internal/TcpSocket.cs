// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    internal class TcpSocket : NetworkSocket
    {
        public override bool IsDatagram => false;
        public override bool? IsSecure => _tls;

        public override SslStream? SslStream => _sslStream;

        protected internal override Socket? Socket => _socket;

        // The MaxDataSize of the SSL implementation.
        private const int MaxSslDataSize = 16 * 1024;

        // See https://tools.ietf.org/html/rfc5246#appendix-A.4
        private const byte TlsHandshakeRecord = 0x16;

        private readonly EndPoint? _addr;
        private readonly Socket _socket;
        private SslStream? _sslStream;
        private bool? _tls;

        public override async ValueTask<Endpoint?> AcceptAsync(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            try
            {
                // On the server side, when accepting a new connection for Ice2 endpoint, the TCP socket checks
                // the first byte sent by the peer to figure out if the peer tries to establish a TLS connection.
                bool secure = false;
                if (endpoint.Protocol == Protocol.Ice2)
                {
                    // Peek one byte into the tcp stream to see if it contains the TLS handshake record
                    Memory<byte> buffer = new byte[1];
                    int received = await _socket.ReceiveAsync(buffer, SocketFlags.Peek, cancel).ConfigureAwait(false);
                    if (received == 0)
                    {
                        throw new ConnectionLostException();
                    }
                    Debug.Assert(received == 1);
                    secure = buffer.Span[0] == TlsHandshakeRecord;
                }

                // If a secure connection is needed, a new SslConnection is created and returned from this method.
                // The caller is responsible for using the returned SslConnection in place of this TcpConnection.
                if (_tls ?? secure)
                {
                    if (authenticationOptions == null)
                    {
                        throw new InvalidOperationException(
                            "cannot accept TLS connection: no TLS authentication configured");
                    }
                    await AuthenticateAsync(sslStream =>
                        sslStream.AuthenticateAsServerAsync(authenticationOptions, cancel)).ConfigureAwait(false);
                }

                ImmutableList<EndpointParam> localParams = endpoint.LocalParams;
                if (_tls == null && endpoint.Protocol == Protocol.Ice2)
                {
                    // the accepted endpoint gets a _tls parameter
                    localParams = localParams.Add(new EndpointParam("_tls", SslStream == null ? "false" : "true"));
                }

                var ipEndPoint = (IPEndPoint)_socket.RemoteEndPoint!;
                return endpoint with
                {
                    Host = ipEndPoint.Address.ToString(),
                    Port = checked((ushort)ipEndPoint.Port),
                    LocalParams = localParams
                };
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(cancel));
            }
        }

        public override async ValueTask<Endpoint> ConnectAsync(
            Endpoint endpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Debug.Assert(_addr != null);
            Debug.Assert(_tls != null);

            try
            {
                // Connect to the peer.
                await _socket.ConnectAsync(_addr, cancel).ConfigureAwait(false);

                if (authenticationOptions != null)
                {
                    await AuthenticateAsync(sslStream =>
                        sslStream.AuthenticateAsClientAsync(authenticationOptions, cancel)).ConfigureAwait(false);
                }

                var ipEndPoint = (IPEndPoint)_socket.LocalEndPoint!;
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

            // A remote endpoint with no _tls parameter is compatible with an established connection no matter its tls
            // disposition.
            return tls == null || tls == _tls;
        }

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
                    received = await _socket.ReceiveAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
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
                    await _socket.SendAsync(buffer, SocketFlags.None, cancel).ConfigureAwait(false);
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
                        await _socket.SendAsync(buffers.ToSegmentList(), SocketFlags.None).ConfigureAwait(false);
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
            _socket.Dispose();
            _sslStream?.Dispose();
        }

        protected override bool PrintMembers(StringBuilder builder)
        {
            if (base.PrintMembers(builder))
            {
                builder.Append(", ");
            }
            builder.Append("LocalEndPoint = ").Append(_socket.LocalEndPoint).Append(", ");
            builder.Append("RemoteEndPoint = ").Append(_socket.RemoteEndPoint);
            return true;
        }

        internal TcpSocket(Socket fd, ILogger logger, bool? tls, EndPoint? addr = null)
            : base(logger)
        {
            _addr = addr;

            // The socket is not connected if a client socket, it's connected otherwise.
            _socket = fd;
            _tls = tls;
        }

        private async Task AuthenticateAsync(Func<SslStream, Task> authenticate)
        {
            // This can only be created with a connected socket.
            _sslStream = new SslStream(new NetworkStream(_socket, false), false);
            _tls = true;

            try
            {
                await authenticate(_sslStream).ConfigureAwait(false);
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

            Logger.LogTlsAuthenticationSucceeded(_sslStream);
        }
    }
}
