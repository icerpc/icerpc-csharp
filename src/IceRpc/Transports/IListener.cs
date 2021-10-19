// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>A listener listens for connection requests from clients. It creates a server network connection when it
    /// accepts a connection from a client.</summary>
    public interface IListener : IDisposable
    {
        /// <summary>The endpoint this listener is listening on. This endpoint can be different from the endpoint used
        /// to create the listener. For example, the binding a TCP server socket can assign a port.</summary>
        /// <return>The bound endpoint.</return>
        Endpoint Endpoint { get; }

        /// <summary>Accepts a new connection.</summary>
        /// <return>The accepted connection.</return>
        Task<INetworkConnection> AcceptAsync();
    }
}
