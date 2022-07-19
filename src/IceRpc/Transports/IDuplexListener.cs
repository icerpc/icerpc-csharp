// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>A listener listens for connection requests from clients. It creates a server duplex connection when it
/// accepts a connection from a client.</summary>
public interface IDuplexListener : IDisposable
{
    /// <summary>Gets the endpoint this listener is listening on. This endpoint can be different from the endpoint used
    /// to create the listener if for example the binding of the server socket assigned a port.</summary>
    /// <return>The bound endpoint.</return>
    Endpoint Endpoint { get; }

    /// <summary>Accepts a new duplex connection.</summary>
    /// <returns>The accepted duplex connection.</returns>
    Task<IDuplexConnection> AcceptAsync();
}
