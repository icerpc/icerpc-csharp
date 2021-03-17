// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    internal sealed class SslSocket : SingleStreamSocket
    {
        public override Socket? Socket => _underlying.Socket;
        public override SslStream? SslStream => _sslStream;

        private readonly Communicator _communicator;
        private readonly Server? _server;
        private SslStream? _sslStream;
        private BufferedStream? _writeStream;
        private readonly SingleStreamSocket _underlying;

        public override async ValueTask<SingleStreamSocket> AcceptAsync(Endpoint endpoint, CancellationToken cancel)
        {
            // The endpoint host is only use for client-side authentication.
            Debug.Assert(_server != null);
            await AuthenticateAsync(host: null, cancel).ConfigureAwait(false);
            return this;
        }

        public override async ValueTask<SingleStreamSocket> ConnectAsync(
            Endpoint endpoint,
            bool secure,
            CancellationToken cancel)
        {
            Debug.Assert(secure);
            await AuthenticateAsync(endpoint.Host, cancel).ConfigureAwait(false);
            return this;
        }

        public override ValueTask CloseAsync(Exception exception, CancellationToken cancel) =>
            // Implement TLS close_notify and call ShutdownAsync? This might be required for implementation
            // session resumption if we want to allow connection migration.
            _underlying.CloseAsync(exception, cancel);

        public override ValueTask<ArraySegment<byte>> ReceiveDatagramAsync(CancellationToken cancel) =>
            throw new InvalidOperationException("only supported by datagram transports");

        public override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (buffer.Length == 0)
            {
                throw new ArgumentException($"empty {nameof(buffer)}");
            }

            int received;
            try
            {
                received = await _sslStream!.ReadAsync(buffer, cancel).ConfigureAwait(false);
            }
            catch (IOException ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new TransportException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
            if (received == 0)
            {
                throw new ConnectionLostException(RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
            return received;
        }

        public override async ValueTask<int> SendAsync(IList<ArraySegment<byte>> buffer, CancellationToken cancel)
        {
            try
            {
                int sent = 0;
                foreach (ArraySegment<byte> segment in buffer)
                {
                    await _writeStream!.WriteAsync(segment, cancel).ConfigureAwait(false);
                    sent += segment.Count;
                }
                await _writeStream!.FlushAsync(cancel).ConfigureAwait(false);
                return sent;
            }
            catch (IOException ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new TransportException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
        }

        public override string ToString() => _underlying.ToString()!;

        protected override void Dispose(bool disposing)
        {
            _underlying.Dispose();

            _sslStream?.Dispose();

            try
            {
                _writeStream?.Dispose();
            }
            catch
            {
                // Ignore: the buffer flush which will fail since the underlying transport is closed.
            }
        }

        // Only for use by TcpEndpoint.
        internal SslSocket(Communicator communicator, SingleStreamSocket underlying)
        {
            _communicator = communicator;
            _underlying = underlying;
        }

        // Only for use by TcpEndpoint.
        internal SslSocket(Server server, SingleStreamSocket underlying)
        {
            _communicator = server.Communicator;
            _server = server;
            _underlying = underlying;
        }

        internal override IDisposable? StartScope(Endpoint endpoint) =>
            _underlying.StartScope(endpoint);

        private async ValueTask AuthenticateAsync(string? host, CancellationToken cancel)
        {
            // This can only be created with a connected socket.
            _sslStream = new SslStream(new NetworkStream(_underlying.Socket!, false), false);

            try
            {
                if (host == null)
                {
                    // Server-side connection
                    Debug.Assert(_server != null);
                    if (_server.AuthenticationOptions == null)
                    {
                        throw new InvalidOperationException(
                            "cannot accept a tls connection no tls configuration was provided");
                    }
                    await _sslStream.AuthenticateAsServerAsync(_server.AuthenticationOptions!, cancel).ConfigureAwait(false);
                }
                else
                {
                    // Client-side connection
                    var options = _communicator.AuthenticationOptions;
                    if (options == null)
                    {
                        options = new SslClientAuthenticationOptions()
                        {
                            TargetHost = host
                        };
                    }
                    else if (options.TargetHost == null)
                    {
                        options = new SslClientAuthenticationOptions
                        {
                            AllowRenegotiation = _communicator.AuthenticationOptions!.AllowRenegotiation,
                            ApplicationProtocols = _communicator.AuthenticationOptions!.ApplicationProtocols,
                            CertificateRevocationCheckMode = _communicator.AuthenticationOptions!.CertificateRevocationCheckMode,
                            CipherSuitesPolicy = _communicator.AuthenticationOptions!.CipherSuitesPolicy,
                            ClientCertificates = _communicator.AuthenticationOptions!.ClientCertificates,
                            EnabledSslProtocols = _communicator.AuthenticationOptions!.EnabledSslProtocols,
                            EncryptionPolicy = _communicator.AuthenticationOptions!.EncryptionPolicy,
                            LocalCertificateSelectionCallback =
                                _communicator.AuthenticationOptions!.LocalCertificateSelectionCallback,
                            RemoteCertificateValidationCallback =
                                _communicator.AuthenticationOptions!.RemoteCertificateValidationCallback,
                            TargetHost = host, // Host cannot be null
                        };
                    }
                    await _sslStream.AuthenticateAsClientAsync(options, cancel).ConfigureAwait(false);
                }
            }
            catch (IOException ex) when (ex.IsConnectionLost())
            {
                throw new ConnectionLostException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
            catch (IOException ex)
            {
                throw new TransportException(ex, RetryPolicy.AfterDelay(TimeSpan.Zero));
            }
            catch (AuthenticationException ex)
            {
                throw new TransportException(ex, RetryPolicy.OtherReplica);
            }

            if (_communicator.SecurityLogger.IsEnabled(LogLevel.Debug))
            {
                _communicator.SecurityLogger.LogTlsConnectionCreated(ToString(), new Dictionary<string, string>()
                    {
                        { "authenticated", $"{_sslStream.IsAuthenticated}" },
                        { "encrypted", $"{_sslStream.IsEncrypted}" },
                        { "signed", $"{_sslStream.IsSigned}" },
                        { "mutually authenticated", $"{_sslStream.IsMutuallyAuthenticated}" },
                        { "cipher", $"{_sslStream.NegotiatedCipherSuite}" },
                        { "protocol", $"{_sslStream.SslProtocol}" }
                    });
            }

            // Use a buffered stream for writes. This ensures that small requests which are composed of multiple
            // small buffers will be sent within a single SSL frame.
            _writeStream = new BufferedStream(_sslStream);
        }
    }
}
