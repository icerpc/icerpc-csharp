// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net;

namespace IceRpc.Internal;

/// <summary>The base interface for protocol listeners.</summary>
public interface IProtocolListener : IDisposable
{
    /// <summary>Gets the server address of this listener. That's the address a client would connect to.</summary>
    ServerAddress ServerAddress { get; }

    /// <summary>Accepts a new connection.</summary>
    /// <returns>The accepted connection and the network address of the client.</returns>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<(IProtocolConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
        CancellationToken cancellationToken);

    /// <summary>Starts listening on the listener server address.</summary>
    Task ListenAsync(CancellationToken cancellationToken);
}
