// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net;

namespace IceRpc.Transports;

/// <summary>The base interface for listeners.</summary>
public interface IListener : IDisposable
{
    /// <summary>Gets the server address of this listener. That's the address a client would connect to.</summary>
    ServerAddress ServerAddress { get; }
}

/// <summary>A listener listens for connection requests from clients.</summary>
/// <typeparam name="T">The connection type.</typeparam>
public interface IListener<T> : IListener
{
    /// <summary>Accepts a new connection.</summary>
    /// <returns>The accepted connection and the network address of the client, or (null, null) after the listener has
    /// been terminated indicating that no more connections will be accepted.</returns>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<(T Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(CancellationToken cancellationToken);
}
