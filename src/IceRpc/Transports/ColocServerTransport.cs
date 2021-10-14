// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> for the coloc transport.</summary>
    public class ColocServerTransport : IServerTransport
    {
        IListener IServerTransport.Listen(Endpoint endpoint) => new ColocListener(endpoint);

        /// <summary>Constructs a colocated server transport.</summary>
        public ColocServerTransport()
        {
        }
    }
}
