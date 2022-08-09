// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>The base interface for listeners.</summary>
public interface IListener : IDisposable
{
    /// <summary>Gets the server address of this listener. That's the address a client would connect to.</summary>
    ServerAddress ServerAddress { get; }
}

/// <summary>A listener listens for connection requests from clients.</summary>
/// <typeparam name="T">The transport connection type.</typeparam>
public interface IListener<T> : IListener
{
    /// <summary>Accepts a new transport connection.</summary>
    /// <returns>The accepted transport connection.</returns>
    Task<T> AcceptAsync();
}
