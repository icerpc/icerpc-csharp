// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>Represents a transport connection created by a multiplexed transport.</summary>
public interface IMultiplexedConnection : ITransportConnection
{
    /// <summary>Accepts a remote stream.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The remote stream.</returns>
    ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken);

    /// <summary>Closes the connection. This method should only be called once.</summary>
    /// <param name="applicationErrorCode">The application error code to transmit to the peer.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the connection is closed.</returns>
    Task CloseAsync(ulong applicationErrorCode, CancellationToken cancellationToken);

    /// <summary>Creates a local stream. The creation might be delayed if the maximum number of unidirectional or
    /// bidirectional streams prevents creating the new stream.</summary>
    /// <param name="bidirectional"><see langword="true"/> to create a bidirectional stream, <see langword="false"/>
    /// otherwise.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The task that completes on the local stream is created.</returns>
    ValueTask<IMultiplexedStream> CreateStreamAsync(bool bidirectional, CancellationToken cancellationToken);
}
