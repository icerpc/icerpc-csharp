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

        private SslStream? _sslStream;
        private BufferedStream? _writeStream;
        private readonly SingleStreamSocket _underlying;

        public override async ValueTask<SingleStreamSocket> AcceptAsync(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            if (authenticationOptions == null)
            {
                throw new InvalidOperationException("cannot accept TLS connection: no TLS authentication configured");
            }
            await AuthenticateAsync(async (SslStream sslStream) =>
            {
                await sslStream.AuthenticateAsServerAsync(authenticationOptions, cancel).ConfigureAwait(false);
            }).ConfigureAwait(false);
            return this;
        }

        public override async ValueTask<SingleStreamSocket> ConnectAsync(
            Endpoint endpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            CancellationToken cancel)
        {
            Debug.Assert(authenticationOptions != null);
            await AuthenticateAsync(async (SslStream sslStream) =>
            {
                SslClientAuthenticationOptions options;
                if (authenticationOptions.TargetHost == null)
                {
                    options = new SslClientAuthenticationOptions
                    {
                        AllowRenegotiation = authenticationOptions.AllowRenegotiation,
                        ApplicationProtocols = authenticationOptions.ApplicationProtocols,
                        CertificateRevocationCheckMode = authenticationOptions.CertificateRevocationCheckMode,
                        CipherSuitesPolicy = authenticationOptions.CipherSuitesPolicy,
                        ClientCertificates = authenticationOptions.ClientCertificates,
                        EnabledSslProtocols = authenticationOptions.EnabledSslProtocols,
                        EncryptionPolicy = authenticationOptions.EncryptionPolicy,
                        LocalCertificateSelectionCallback =
                            authenticationOptions.LocalCertificateSelectionCallback,
                        RemoteCertificateValidationCallback =
                            authenticationOptions.RemoteCertificateValidationCallback,
                        TargetHost = endpoint.Host
                    };
                }
                else
                {
                    options = authenticationOptions;
                }
                await sslStream.AuthenticateAsClientAsync(options, cancel).ConfigureAwait(false);
            }).ConfigureAwait(false);
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

        internal SslSocket(SingleStreamSocket underlying)
            : base(underlying.Logger) => _underlying = underlying;

        internal override IDisposable? StartScope(Endpoint endpoint) =>
            _underlying.StartScope(endpoint);

        private async ValueTask AuthenticateAsync(Func<SslStream, ValueTask> authenticate)
        {
            // This can only be created with a connected socket.
            _sslStream = new SslStream(new NetworkStream(_underlying.Socket!, false), false);
            try
            {
                await authenticate(_sslStream).ConfigureAwait(false);
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

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogTlsConnectionCreated(ToString(), new Dictionary<string, string>()
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
