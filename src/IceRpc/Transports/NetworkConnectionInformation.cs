// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports
{
    /// <summary>The network connection information returned on <see cref="INetworkConnection"/> connection
    /// establishment.</summary>
    public readonly record struct NetworkConnectionInformation
    {
        /// <summary>Gets the idle timeout.</summary>
        public TimeSpan IdleTimeout { get; init; }

        /// <summary>Gets the local endpoint.</summary>
        public EndPoint LocalEndPoint { get; }

        /// <summary>Gets the remote endpoint.</summary>
        public EndPoint RemoteEndPoint { get; }

        /// <summary>Gets the peer remote certificate if TLS is used for the connection, <c>null</c> otherwise.</summary>
        public X509Certificate? RemoteCertificate { get; }

        /// <summary>Constructs a new instance of <see cref="NetworkConnectionInformation"/>.</summary>
        /// <param name="localEndPoint">The local endpoint.</param>
        /// <param name="remoteEndPoint">The remote endpoint.</param>
        /// <param name="idleTimeout">The idle timeout.</param>
        /// <param name="remoteCertificate">The optional remote certificate.</param>
        public NetworkConnectionInformation(
            EndPoint localEndPoint,
            EndPoint remoteEndPoint,
            TimeSpan idleTimeout,
            X509Certificate? remoteCertificate)
        {
            LocalEndPoint = localEndPoint;
            RemoteEndPoint = remoteEndPoint;
            IdleTimeout = idleTimeout;
            RemoteCertificate = remoteCertificate;
        }
    }
}
