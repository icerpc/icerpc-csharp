// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports
{
    /// <summary>Provides information about a coloc connection.</summary>

    public class ColocConnectionInformation : ConnectionInformation
    {
        /// <summary>The Id of the colocated connection.</summary>
        public long Id { get; internal init; }

        /// <inheritdoc/>
        public override bool IsSecure => true;

        /// <inheritdoc/>
        public override string ToString() => $"ID={Id}";
    }

    /// <summary>Provides information about an IP connection.</summary>
    public abstract class IPConnectionInformation : ConnectionInformation
    {
        /// <summary>The connection local IP-endpoint or null if it is not available.</summary>
        public IPEndPoint? LocalEndPoint
        {
            get
            {
                try
                {
                    return _socket.LocalEndPoint as IPEndPoint;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>The connection remote IP-endpoint or null if it is not available.</summary>
        public IPEndPoint? RemoteEndPoint
        {
            get
            {
                try
                {
                    return _socket.RemoteEndPoint as IPEndPoint;
                }
                catch
                {
                    return null;
                }
            }
        }
        private readonly Socket _socket;

        /// <inheritdoc/>
        public override string ToString()
        {
            string localEndPoint = LocalEndPoint?.ToString() ?? "undefined";
            string remoteEndPoint = RemoteEndPoint?.ToString() ?? "undefined";
            return $"LocalEndpoint={localEndPoint}, RemoteEndpoint={remoteEndPoint}, IsSecure={IsSecure}";
        }

        /// <summary>Constructs an IP connection information.</summary>
        protected IPConnectionInformation(Socket socket) => _socket = socket;
    }

    /// <summary>Provides information about a TCP connection.</summary>
    /// TODO: should we simply return the SslStream?
    public class TcpConnectionInformation : IPConnectionInformation
    {
        /// <summary>Gets a Boolean value that indicates whether the certificate revocation list is checked during the
        /// certificate validation process.</summary>
        public bool CheckCertRevocationStatus => _sslStream?.CheckCertRevocationStatus ?? false;

        /// <summary>Gets a Boolean value that indicates whether this SslStream uses data encryption.</summary>
        // TODO: fix comment an explain difference with IsSecure, if any.
        public bool IsEncrypted => _sslStream?.IsEncrypted ?? false;

        /// <summary>Gets a Boolean value that indicates whether both server and client have been authenticated.
        /// </summary>
        public bool IsMutuallyAuthenticated => _sslStream?.IsMutuallyAuthenticated ?? false;

        /// <summary>Gets a Boolean value that indicates whether the data sent using this stream is signed.</summary>
        public bool IsSigned => _sslStream?.IsSigned ?? false;

        /// <inheritdoc/>
        public override bool IsSecure => _sslStream != null;

        /// <summary>Gets the certificate used to authenticate the local endpoint or null if no certificate was
        /// supplied.</summary>
        public X509Certificate? LocalCertificate => _sslStream?.LocalCertificate;

        /// <summary>The negotiated application protocol in TLS handshake.</summary>
        public SslApplicationProtocol? NegotiatedApplicationProtocol => _sslStream?.NegotiatedApplicationProtocol;

        /// <summary>Gets the cipher suite which was negotiated for this connection.</summary>
        public TlsCipherSuite? NegotiatedCipherSuite => _sslStream?.NegotiatedCipherSuite;

        /// <summary>Gets the certificate used to authenticate the remote endpoint or null if no certificate was
        /// supplied.</summary>
        public X509Certificate? RemoteCertificate => _sslStream?.RemoteCertificate;

        /// <summary>Gets a value that indicates the security protocol used to authenticate this connection or
        /// null if the connection is not secure.</summary>
        public SslProtocols? SslProtocol => _sslStream?.SslProtocol;

        private readonly SslStream? _sslStream;

        /// <summary>Constructs a TCP connection information.</summary>
        protected internal TcpConnectionInformation(Socket socket, SslStream? sslStream)
            : base(socket) => _sslStream = sslStream;
    }

    /// <summary>Provides information about a UDP connection.</summary>
    public class UdpConnectionInformation : IPConnectionInformation
    {
        /// <inheritdoc/>
        public override bool IsSecure => false;

        /// <summary>The multicast IP-endpoint for a multicast connection otherwise null.</summary>
        // TODO: fix description. Is this only for incoming connections???
        public IPEndPoint? MulticastEndpoint { get; internal init; }

        /// <summary>Constructs a UDP connection information.</summary>
        internal UdpConnectionInformation(Socket socket)
            : base(socket)
        {
        }
    }

    /// <summary>Provides information about a WebSocket connection.</summary>
    public class WSConnectionInformation : TcpConnectionInformation
    {
        /// <summary>The HTTP headers for the WebSocket connection.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; internal init; } =
            ImmutableDictionary<string, string>.Empty;

        /// <summary>Constructs a UDP connection information.</summary>
        internal WSConnectionInformation(Socket socket, SslStream? sslStream)
            : base(socket, sslStream)
        {
        }
    }
}
