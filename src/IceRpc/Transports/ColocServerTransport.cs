// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> for the coloc transport.</summary>
    public class ColocServerTransport : SimpleServerTransport
    {
        /// <summary>Constructs a colocated server transport.</summary>
        public ColocServerTransport() :
            base(new(), TimeSpan.MaxValue)
        {
        }

        /// <summary>Constructs a colocated server transport.</summary>
        /// <param name="slicOptions">The Slic options.</param>
        public ColocServerTransport(SlicOptions slicOptions) :
            base(slicOptions, TimeSpan.MaxValue)
        {
        }

        /// <inheritdoc/>
        protected override SimpleListener Listen(Endpoint endpoint) => new ColocListener(endpoint);
    }
}
