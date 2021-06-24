// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading.Tasks;

namespace IceRpc.Transports
{
    /// <summary>A listener listens for connection requests from clients. It creates a server connection when it accepts
    /// a connection from a client.</summary>
    public interface IListener : IDisposable
    {
        /// <summary>The endpoint this listener is listening on. This endpoint can be different from the endpoint used
        /// to create the listener if for example the binding of the server socket assigned a port.</summary>
        /// <return>The bound endpoint.</return>
        Endpoint ListenerEndpoint { get; }

        /// <summary>Accepts a new connection.</summary>
        /// <return>The accepted connection.</return>
        ValueTask<MultiStreamConnection> AcceptAsync();
    }
}
