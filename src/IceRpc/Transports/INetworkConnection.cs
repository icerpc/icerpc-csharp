// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports
{
    /// <summary>The network connection information returned by <see
    /// cref="INetworkConnection.ConnectSingleStreamConnectionAsync"/> or <see
    /// cref="INetworkConnection.ConnectMultiStreamConnectionAsync"/></summary>
    public record struct NetworkConnectionInformation
    {
        /// <summary>The idle timeout.</summary>
        public TimeSpan IdleTimeout;

        /// <summary>The local endpoint.</summary>
        public readonly Endpoint LocalEndpoint;

        /// <summary>The remote endpoint.</summary>
        public readonly Endpoint RemoteEndpoint;

        /// <summary>The peer remote certificate if TLS is used for the connection, <c>null</c> otherwise.</summary>
        public readonly X509Certificate? RemoteCertificate;

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

    /// <summary>A network connection represents the low-level transport to exchange data as bytes. A network
    /// connection supports both exchanging data with an <see cref="ISingleStreamConnection"/> (for the Ice1
    /// protocol) or an <see cref="IMultiStreamConnection"/> (for the Ice2 protocol). A single-stream
    /// transport such as TCP or Coloc uses the Slic multi-stream connection implementation to provide
    /// multi-stream support.</summary>
    public interface INetworkConnection
    {
        /// <summary>Indicates whether or not this network connection is secure.</summary>
        /// <value><c>true</c> means the network connection is secure. <c>false</c> means the network
        /// connection transport is not secure. If the connection is not established, secure is always
        /// <c>false</c>.</value>
        bool IsSecure { get; }

        /// <summary>The time elapsed since the last activity of the connection.</summary>
        TimeSpan LastActivity { get; }

        /// <summary>Closes the network connection.</summary>
        /// <param name="exception">The reason of the connection closure.</param>
        void Close(Exception? exception = null);

        /// <summary>Connects this network connection and return a single-stream connection for single-stream
        /// communications over this network connection.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The <see cref="ISingleStreamConnection"/> and <see cref="NetworkConnectionInformation"/>.</returns>
        ValueTask<(ISingleStreamConnection, NetworkConnectionInformation)> ConnectSingleStreamConnectionAsync(
            CancellationToken cancel);

        /// <summary>Connects this network connection and return a multi-stream connection to allow
        /// multi-stream communications over this network connection.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The <see cref="IMultiStreamConnection"/> and <see cref="NetworkConnectionInformation"/>.</returns>
        ValueTask<(IMultiStreamConnection, NetworkConnectionInformation)> ConnectMultiStreamConnectionAsync(
            CancellationToken cancel);

        /// <summary>Checks if the parameters of the provided endpoint are compatible with this network
        /// connection. Compatible means a client could reuse this network connection instead of establishing
        /// a new network connection.</summary>
        /// <param name="remoteEndpoint">The endpoint to check.</param>
        /// <returns><c>true</c> when this connection is a client connection whose parameters are compatible
        /// with the parameters of the provided endpoint; otherwise, <c>false</c>.</returns>
        bool HasCompatibleParams(Endpoint remoteEndpoint);
    }
}
