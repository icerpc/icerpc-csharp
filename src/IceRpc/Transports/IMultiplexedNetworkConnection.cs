// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Security.Authentication;

namespace IceRpc.Transports
{
    /// <summary>Represents a network connection created by a multiplexed transport. The IceRPC core calls <see
    /// cref="ConnectAsync"/> before calling other methods.</summary>
    public interface IMultiplexedNetworkConnection : INetworkConnection
    {
        /// <summary>Enables or disables keep alive for the network connection. If enabled, the connection will ensure
        /// that the peer doesn't close the connection when it's idle.</summary>
        bool KeepAlive { get; set; }

        /// <summary>Aborts the connection. This will call <see cref="IMultiplexedStream.Abort"/> on each stream and
        /// prevent any new streams from being created or accepted.</summary>
        void Abort(Exception exception);

        /// <summary>Accepts a remote stream.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The remote stream.</return>
        ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel);

        /// <summary>Connects this network connection.</summary>
        /// <param name="idleTimeout">The idle timeout. If the connection is idle for a longer period than the idle
        /// timeout it will be closed.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The negotiated idle timeout and the <see cref="NetworkConnectionInformation"/>.</returns>
        /// <exception cref="ConnectFailedException">Thrown if the connection establishment to the per
        /// failed.</exception>
        /// <exception cref="ConnectionLostException">Thrown if the peer closed its side of the connection while the
        /// connection is being established.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the connection has been disposed.</exception>
        /// <exception cref="OperationCanceledException">Thrown if the cancellation token was canceled.</exception>
        /// <exception cref="TransportException">Thrown if an unexpected error was encountered.</exception>
        /// <remarks>A transport implementation might raise other exceptions. A network connection supporting SSL can
        /// for instance raise <see cref="AuthenticationException"/> if the authentication fails while the connection is
        /// being established.</remarks>
        Task<(TimeSpan, NetworkConnectionInformation)> ConnectAsync(TimeSpan idleTimeout, CancellationToken cancel);

        /// <summary>Creates a local stream.</summary>
        /// <param name="bidirectional"><c>True</c> to create a bidirectional stream, <c>false</c> otherwise.</param>
        /// <return>The local stream.</return>
        IMultiplexedStream CreateStream(bool bidirectional);

        /// <summary>Shuts down the connection.</summary>
        /// <param name="applicationErrorCode">The application error code transmitted to the peer.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        Task ShutdownAsync(ulong applicationErrorCode, CancellationToken cancel);
    }
}
