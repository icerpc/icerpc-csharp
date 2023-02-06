// Copyright (c) ZeroC, Inc.

using System.Net;

namespace IceRpc.Transports;

/// <summary>The base interface for listeners.</summary>
public interface IListener : IAsyncDisposable
{
    /// <summary>Gets the server address of this listener. That's the address a client would connect to.</summary>
    ServerAddress ServerAddress { get; }
}

/// <summary>A listener listens for connection requests from clients.</summary>
/// <typeparam name="T">The connection type.</typeparam>
public interface IListener<T> : IListener
{
    /// <summary>Accepts a new connection.</summary>
    /// <returns>The accepted connection and the network address of the client.</returns>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    Task<(T Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(CancellationToken cancellationToken);
}
