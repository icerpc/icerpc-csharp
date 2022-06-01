// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>Represents a network connection created by a multiplexed transport. The IceRPC core calls <see
    /// cref="INetworkConnection.ConnectAsync"/> before calling other methods.</summary>
    public interface IMultiplexedNetworkConnection : INetworkConnection
    {
        // TODO: Remove once the idle timeout is implemented by Slic (Quic supports it as well). See #906.

        /// <summary>Gets the time elapsed since the last activity of the connection.</summary>
        TimeSpan LastActivity { get; }

        /// <summary>Aborts the connection. This will call <see cref="IMultiplexedStream.Abort"/> on each stream and
        /// prevent any new streams from being created or accepted.</summary>
        void Abort(Exception exception);

        /// <summary>Accepts a remote stream.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The remote stream.</return>
        ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel);

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
