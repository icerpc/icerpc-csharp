// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading.Tasks;

namespace IceRpc.Transports
{
    /// <summary>An listener listens and accepts server connection requests from clients.</summary>
    public interface IListener : IDisposable
    {
        /// <summary>The listening endpoint. The listener endpoint might be different from the endpoint used
        /// to create the listener if for example the binding of the server socket assigned a port.</summary>
        /// <return>The bound endpoint.</return>
        Endpoint Endpoint { get; }

        /// <summary>Accepts a new connection.</summary>
        /// <return>The accepted connection.</return>
        ValueTask<MultiStreamConnection> AcceptAsync();
    }
}
