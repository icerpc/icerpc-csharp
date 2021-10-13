// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> for the coloc transport.</summary>
    public class ColocServerTransport : IServerTransport
    {
        private readonly SlicOptions _slicOptions;

        IListener IServerTransport.Listen(Endpoint endpoint) => new ColocListener(endpoint, _slicOptions);

        /// <summary>Constructs a colocated server transport.</summary>
        /// <param name="slicOptions">The transport options.</param>
        public ColocServerTransport(SlicOptions slicOptions) => _slicOptions = slicOptions;
    }
}
