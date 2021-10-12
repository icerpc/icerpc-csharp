// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports
{
    /// <summary>The network connection information returned by <see
    /// cref="INetworkConnection.ConnectSingleStreamConnectionAsync"/> or <see
    /// cref="INetworkConnection.ConnectMultiStreamConnectionAsync"/></summary>
    public readonly record struct NetworkConnectionInformation
    {
        /// <summary>The idle timeout.</summary>
        public TimeSpan IdleTimeout { get; init; }

        /// <summary>The local endpoint.</summary>
        public Endpoint LocalEndpoint { get; }

        /// <summary>The remote endpoint.</summary>
        public Endpoint RemoteEndpoint { get; }

        /// <summary>The peer remote certificate if TLS is used for the connection, <c>null</c> otherwise.</summary>
        public X509Certificate? RemoteCertificate { get; }

        /// <summary>Constructs a new instance of <see cref="NetworkConnectionInformation"/>.</summary>
        /// <param name="localEndpoint">The local endpoint.</param>
        /// <param name="remoteEndpoint">The remote endpoint.</param>
        /// <param name="idleTimeout">The idle timeout.</param>
        /// <param name="remoteCertificate">The optional remote certificate.</param>
        public NetworkConnectionInformation(
            Endpoint localEndpoint,
            Endpoint remoteEndpoint,
            TimeSpan idleTimeout,
            X509Certificate? remoteCertificate)
        {
            LocalEndpoint = localEndpoint;
            RemoteEndpoint = remoteEndpoint;
            IdleTimeout = idleTimeout;
            RemoteCertificate = remoteCertificate;
        }
    }
}
