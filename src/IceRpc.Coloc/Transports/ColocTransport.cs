// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Collections.Concurrent;

namespace IceRpc.Transports
{
    /// <summary>The Coloc transport class provides a client and server transport that can be used for in-process
    /// communications.</summary>
    public sealed class ColocTransport
    {
        /// <summary>The transport name.</summary>
        public const string Name = "coloc";

        /// <summary>The colocated client transport.</summary>
        public IClientTransport<ISimpleNetworkConnection> ClientTransport { get; }

        /// <summary>The colocated server transport.</summary>
        public IServerTransport<ISimpleNetworkConnection> ServerTransport { get; }

        /// <summary>Constructs a <see cref="ColocTransport"/>.</summary>
        public ColocTransport()
        {
            var listeners = new ConcurrentDictionary<Endpoint, ColocListener>();
            ClientTransport = new ColocClientTransport(listeners);
            ServerTransport = new ColocServerTransport(listeners);
        }
    }
}
