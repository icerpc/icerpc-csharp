// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Net.Security;

namespace IceRpc
{
    public sealed class SlicOptions
    {
        private int? _streamBufferMaxSize;
        internal int PacketMaxSize { get; set; } = 32 * 1024;
        internal int StreamBufferMaxSize
        {
             get => _streamBufferMaxSize ?? 2 * PacketMaxSize;
             set => _streamBufferMaxSize = value;
        }

        public SlicOptions Copy() => (SlicOptions)MemberwiseClone();
    };

    public sealed class SocketOptions
    {
        public int BidirectionalStreamMaxCount { get; set; } = 100;
        public int TcpBackLog { get; set; } = 511;
        public int? TcpReceiveBufferSize { get; set; }
        public int? TcpSendBufferSize { get; set; }
        public int? UdpReceiveBufferSize { get; set; }
        public int? UdpSendBufferSize { get; set; }
        public int UnidirectionalStreamMaxCount { get; set; } = 100;

        public SocketOptions Copy() => (SocketOptions)MemberwiseClone();
    }

    public abstract class ConnectionOptions
    {
        public SlicOptions Slic { get; set; } = new SlicOptions();

        public SocketOptions Socket { get; set; } = new SocketOptions();

        public TimeSpan CloseTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(60);

        public int IncomingFrameMaxSize { get; set; } = 1024 * 1024;

        public bool KeepAlive { get; set; }

        public ILogger? ProtocolLogger { get; set; }

        public ILogger? TransportLogger { get; set; }

        protected ConnectionOptions Copy()
        {
            var options = (ConnectionOptions)MemberwiseClone();
            options.Slic = Slic.Copy();
            options.Socket = Socket.Copy();
            return options;
        }
    };

    public sealed class ClientConnectionOptions : ConnectionOptions
    {
        public SslClientAuthenticationOptions? Authentication { get; set; }

        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public object? Label { get; set; }

        /// <summary>Indicates under what conditions this server accepts non-secure connections.</summary>
        // TODO: fix default
        public NonSecure PreferNonSecure { get; set; } = NonSecure.Always;

        public new ClientConnectionOptions Copy()
        {
            ClientConnectionOptions options = (ClientConnectionOptions)base.Copy();
            options.Authentication = Authentication == null ? null : new SslClientAuthenticationOptions()
            {
                AllowRenegotiation = Authentication.AllowRenegotiation,
                ApplicationProtocols = Authentication.ApplicationProtocols,
                CertificateRevocationCheckMode = Authentication.CertificateRevocationCheckMode,
                CipherSuitesPolicy = Authentication.CipherSuitesPolicy,
                ClientCertificates = Authentication.ClientCertificates,
                EnabledSslProtocols = Authentication.EnabledSslProtocols,
                EncryptionPolicy = Authentication.EncryptionPolicy,
                LocalCertificateSelectionCallback = Authentication.LocalCertificateSelectionCallback,
                RemoteCertificateValidationCallback = Authentication.RemoteCertificateValidationCallback,
                TargetHost = Authentication.TargetHost
            };
            return options;
        }
    };

    public sealed class ServerConnectionOptions : ConnectionOptions
    {
        public SslServerAuthenticationOptions? Authentication { get; set; }

        /// <summary>Indicates under what circumstances this server accepts non-secure incoming connections.
        /// </summary>
        // TODO: fix default
        public NonSecure AcceptNonSecure { get; set; } = NonSecure.Always;

        public TimeSpan AcceptTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public new ServerConnectionOptions Copy()
        {
            ServerConnectionOptions options = (ServerConnectionOptions)base.Copy();
            options.Authentication = Authentication == null ? null : new SslServerAuthenticationOptions()
            {
                AllowRenegotiation = Authentication.AllowRenegotiation,
                ApplicationProtocols = Authentication.ApplicationProtocols,
                CertificateRevocationCheckMode = Authentication.CertificateRevocationCheckMode,
                CipherSuitesPolicy = Authentication.CipherSuitesPolicy,
                ClientCertificateRequired = Authentication.ClientCertificateRequired,
                EnabledSslProtocols = Authentication.EnabledSslProtocols,
                EncryptionPolicy = Authentication.EncryptionPolicy,
                RemoteCertificateValidationCallback = Authentication.RemoteCertificateValidationCallback,
                ServerCertificate = Authentication.ServerCertificate,
                ServerCertificateContext = Authentication.ServerCertificateContext,
                ServerCertificateSelectionCallback = Authentication.ServerCertificateSelectionCallback
            };
            return options;
        }
    }
}
