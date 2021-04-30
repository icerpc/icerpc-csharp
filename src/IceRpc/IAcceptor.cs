// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>An acceptor listens and accepts incoming connection requests from clients. It creates a connection
    /// which is used to exchange data with the remote peer.</summary>
    public interface IAcceptor : IDisposable
    {
        /// <summary>The listening endpoint. The acceptor endpoint might be different from the endpoint used
        /// to create the acceptor if for example the binding of server socket assigned a port.</summary>
        /// <return>The bound endpoint.</return>
        Endpoint Endpoint { get; }

        /// <summary>Accepts a new connection.</summary>
        /// <return>The accepted connection.</return>
        ValueTask<Connection> AcceptAsync();

        IDisposable? StartScope(Server server) => server.Logger.StartAcceptorScope(server, this);
    }
}
