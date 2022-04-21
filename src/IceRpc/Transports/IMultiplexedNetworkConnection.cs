// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>Represents a network connection created by a multiplexed transport. The IceRPC core calls <see
    /// cref="INetworkConnection.ConnectAsync"/> before calling other methods.</summary>
    public interface IMultiplexedNetworkConnection : INetworkConnection
    {
        /// <summary>Accepts a remote stream.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The remote stream.</return>
        ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel);

        /// <summary>Closes the connection.</summary>
        /// <param name="applicationErrorCode">The application error code transmitted to the peer.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        // TODO: should we do like for streams and instead use an exception and a mapping from exception to error code?
        ValueTask CloseAsync(int applicationErrorCode, CancellationToken cancel);

        /// <summary>Creates a local stream.</summary>
        /// <param name="bidirectional"><c>True</c> to create a bidirectional stream, <c>false</c> otherwise.</param>
        /// <return>The local stream.</return>
        IMultiplexedStream CreateStream(bool bidirectional);
    }
}
