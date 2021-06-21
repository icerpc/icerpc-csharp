// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    internal class TcpConnection : SingleStreamConnection
    {
        /// <inheritdoc/>
        public override ConnectionInformation ConnectionInformation =>
            _connectionInformation ??= new TcpConnectionInformation(_socket, SslStream);

        /// <inheritdoc/>
        internal override Socket? NetworkSocket => _socket;

        internal SslStream? SslStream { get; private set; }

        // The MaxDataSize of the SSL implementation.
        private const int MaxSslDataSize = 16 * 1024;

        // See https://tools.ietf.org/html/rfc5246#appendix-A.4
        private const byte TlsHandshakeRecord = 0x16;

        private readonly EndPoint? _addr;
        private TcpConnectionInformation? _connectionInformation;
        private readonly Socket _socket;

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
                if (endpoint.IsSecure ?? secure)
                {
                    if (authenticationOptions == null)
                    {
                        throw new InvalidOperationException(
                            "cannot accept TLS connection: no TLS authentication configured");
                    }
                    await AuthenticateAsync(sslStream =>
                        sslStream.AuthenticateAsServerAsync(authenticationOptions, cancel)).ConfigureAwait(false);
                }
                return ((TcpEndpoint)endpoint).Clone(_socket.RemoteEndPoint!);
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(cancel));
            }
        }

        // We can't shutdown the socket write side because it might block.
        public override ValueTask CloseAsync(long errorCode, CancellationToken cancel) => default;

        public override async ValueTask<Endpoint> ConnectAsync(
            Endpoint endpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Debug.Assert(_addr != null);

            try
            {
                // Connect to the peer.
                await _socket.ConnectAsync(_addr, cancel).ConfigureAwait(false);

                if (authenticationOptions != null)
                {
                    await AuthenticateAsync(sslStream =>
                        sslStream.AuthenticateAsClientAsync(authenticationOptions, cancel)).ConfigureAwait(false);
                }
                return ((TcpEndpoint)endpoint).Clone(_socket.LocalEndPoint!);
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
                        // There is no point copying only the first buffer into another buffer
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
            SslStream?.Dispose();
        }

        internal TcpConnection(Socket fd, ILogger logger, EndPoint? addr = null)
            : base(logger)
        {
            _addr = addr;

            // The socket is not connected if a client socket, it's connected otherwise.
            _socket = fd;
        }

        private async Task AuthenticateAsync(Func<SslStream, Task> authenticate)
        {
            // This can only be created with a connected socket.
            SslStream = new SslStream(new NetworkStream(_socket, false), false);
            try
            {
                await authenticate(SslStream).ConfigureAwait(false);
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
    }
}
