// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading.Tasks;

namespace IceRpc.Transports
{
    /// <summary>An acceptor listens and accepts incoming connection requests from clients. It creates a socket
    /// which is used to exchange data with the remote peer.</summary>
    public interface IAcceptor : IDisposable
    {
        /// <summary>The listening endpoint. The acceptor endpoint might be different from the endpoint used
        /// to create the acceptor if for example the binding of the server socket assigned a port.</summary>
        /// <return>The bound endpoint.</return>
        Endpoint Endpoint { get; }

        /// <summary>Accepts a new connection.</summary>
        /// <return>The accepted connection.</return>
        ValueTask<MultiStreamConnection> AcceptAsync();
    }
}
