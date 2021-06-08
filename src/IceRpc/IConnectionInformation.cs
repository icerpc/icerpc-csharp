// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc
{
    /// <summary>The <c>IConnectionInformation</c> base interface. Interfaces extending this interface provide information
    /// on the connection used by a connection.</summary>
    public interface IConnectionInformation
    {
        /// <summary><c>true</c> if the connection uses encryption, <c>false</c> otherwise.</summary>
        bool IsSecure { get; }

        /// <summary>The description of the connection.</summary>
        string Description => $"IsSecure={IsSecure}";
    }

    /// <summary>The IColocConnectionInformation interface provides information for a coloc connection.</summary>

    public interface IColocConnectionInformation : IConnectionInformation
    {
        /// <summary>The Id of the colocated connection.</summary>
        long Id { get; }

        /// <inheritdoc/>
        bool IConnectionInformation.IsSecure => true;

        /// <inheritdoc/>
        string IConnectionInformation.Description => $"ID={Id}";
    }

    /// <summary>The ITcpConnectionInformation interface provides properties for an IP connection.</summary>
    public interface IIPConnectionInformation : IConnectionInformation
    {
        /// <summary>The connection local IP-endpoint or null if it is not available.</summary>
        IPEndPoint? LocalEndPoint { get; }

        /// <summary>The connection remote IP-endpoint or null if it is not available.</summary>
        IPEndPoint? RemoteEndPoint { get; }

        /// <inheritdoc/>
        string IConnectionInformation.Description
        {
            get
            {
                string localEndPoint;
                string remoteEndPoint;
                try
                {
                    localEndPoint = LocalEndPoint?.ToString() ?? "undefined";
                    remoteEndPoint = RemoteEndPoint?.ToString() ?? "undefined";
                }
                catch (SocketException)
                {
                    localEndPoint = "<not connected>";
                    remoteEndPoint = "<not connected>";
                }
                catch
                {
                    localEndPoint = "<closed>";
                    remoteEndPoint = "<closed>";
                }
                return $"LocalEndpoint={localEndPoint}, RemoteEndpoint={remoteEndPoint}, IsSecure={IsSecure}";
            }
        }
    }

    /// <summary>The ITcpConnectionInformation interface provides properties for a TCP connection.</summary>
    public interface ITcpConnectionInformation : IIPConnectionInformation
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

    /// <summary>The UdpConnection interface provides properties for a UDP connection.</summary>
    interface IUdpConnectionInformation : IIPConnectionInformation
    {
        /// <summary>The multicast IP-endpoint for a multicast connection otherwise null.</summary>
        IPEndPoint? MulticastEndpoint { get; }
    }

    /// <summary>The WSConnection interface provides properties for a WebSocket connection.</summary>
    interface IWSConnectionInformation : ITcpConnectionInformation
    {
        /// <summary>The HTTP headers for the WebSocket connection.</summary>
        IReadOnlyDictionary<string, string> Headers { get; }
    }
}
