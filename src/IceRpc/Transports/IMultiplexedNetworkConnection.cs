// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports;

/// <summary>Represents a network connection created by a multiplexed transport.</summary>
public interface IMultiplexedNetworkConnection : INetworkConnection, IAsyncDisposable
{
    /// <summary>Accepts a remote stream.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <return>The remote stream.</return>
    ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel);

    /// <summary>Creates a local stream.</summary>
    /// <param name="bidirectional"><c>True</c> to create a bidirectional stream, <c>false</c> otherwise.</param>
    /// <return>The local stream.</return>
    IMultiplexedStream CreateStream(bool bidirectional);

    /// <summary>Shuts down the connection.</summary>
    /// <param name="completeException">The exception provided to the stream <see cref="IDuplexPipe"/>.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    Task ShutdownAsync(Exception completeException, CancellationToken cancel);
}
