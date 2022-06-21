// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>The base interface for <see cref="IListener{T}"/>.</summary>
public interface IListener : IAsyncDisposable
{
    /// <summary>Gets the endpoint this listener is listening on. This endpoint can be different from the endpoint used
    /// to create the listener if for example the binding of the server socket assigned a port.</summary>
    /// <return>The bound endpoint.</return>
    Endpoint Endpoint { get; }
}

/// <summary>A listener listens for connection requests from clients. It creates a server network connection when it
/// accepts a connection from a client.</summary>
/// <typeparam name="T">The listener network connection type.</typeparam>
public interface IListener<T> : IListener where T : INetworkConnection
{
    /// <summary>Accepts a new connection.</summary>
    /// <returns>The accepted connection.</returns>
    Task<T> AcceptAsync();
}
