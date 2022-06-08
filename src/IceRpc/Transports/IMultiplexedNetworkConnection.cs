// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>Represents a network connection created by a multiplexed transport.</summary>
    public interface IMultiplexedNetworkConnection : INetworkConnection
    {
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
