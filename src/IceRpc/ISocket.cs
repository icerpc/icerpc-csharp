// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc
{
    /// <summary>The <c>ISocket</c> base interface. Interfaces extending this information provide information
    /// on the socket used by a connection.</summary>
    public interface ISocket
    {
        /// <summary><c>true</c> if the socket uses encryption <c>false</c> otherwise.</summary>
        bool IsSecure { get; }
    }

    public interface IIpSocket : ISocket
    {
        /// <summary>The socket local IP-endpoint or null if it is not available.</summary>
        IPEndPoint? LocalEndPoint { get; }

        /// <summary>The socket remote IP-endpoint or null if it is not available.</summary>
        IPEndPoint? RemoteEndPoint { get; }

        string? ToString()
        {
            try
            {
                string localEndPoint = LocalEndPoint?.ToString() ?? "undefined";
                string remoteEndPoint = RemoteEndPoint?.ToString() ?? "undefined";
                return $"{GetType().Name}(LocalEndpoint={localEndPoint}, RemoteEndpoint={remoteEndPoint})";
            }
            catch (SocketException)
            {
                return $"{GetType().Name}(not connected)";
            }
            catch (ObjectDisposedException)
            {
                return $"{GetType().Name}(closed)";
            }
        }
    }

    /// <summary>The ITcpSocket interface provides properties for a Tcp socket.</summary>
    public interface ITcpSocket : IIpSocket
    {
        /// <summary>Gets a Boolean value that indicates whether the certificate revocation list is checked during the
        /// certificate validation process.</summary>
        bool CheckCertRevocationStatus { get; }

        /// <summary>Gets a Boolean value that indicates whether this SslStream uses data encryption.</summary>
        bool IsEncrypted { get; }

        /// <summary>Gets a Boolean value that indicates whether both server and client have been authenticated.
        /// </summary>
        bool IsMutuallyAuthenticated { get; }

        /// <summary>Gets a Boolean value that indicates whether the data sent using this stream is signed.</summary>
        bool IsSigned { get; }

        /// <summary>Gets the certificate used to authenticate the local endpoint or null if no certificate was
        /// supplied.</summary>
        X509Certificate? LocalCertificate { get; }

        /// <summary>The negotiated application protocol in TLS handshake.</summary>
        SslApplicationProtocol? NegotiatedApplicationProtocol { get; }

        /// <summary>Gets the cipher suite which was negotiated for this connection.</summary>
        TlsCipherSuite? NegotiatedCipherSuite { get; }

        /// <summary>Gets the certificate used to authenticate the remote endpoint or null if no certificate was
        /// supplied.</summary>
        X509Certificate? RemoteCertificate { get; }

        /// <summary>Gets a value that indicates the security protocol used to authenticate this connection or
        /// null if the connection is not secure.</summary>
        SslProtocols? SslProtocol { get; }
    }

    /// <summary>The UdpSocket interface provides properties for a UDP socket.</summary>
    interface IUdpSocket : IIpSocket
    {
        /// <summary>The multicast IP-endpoint for a multicast connection otherwise null.</summary>
        IPEndPoint? MulticastEndpoint { get; }
    }

    /// <summary>The WSSocket interface provides properties for a WebSocket socket.</summary>
    interface IWSSocket : ITcpSocket
    {
        /// <summary>The HTTP headers for the WebSocket socket.</summary>
        IReadOnlyDictionary<string, string> Headers { get; }
    }
}
