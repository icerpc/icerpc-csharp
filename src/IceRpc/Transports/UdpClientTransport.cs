// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport{ISimpleNetworkConnection}"/> for the UDP transport.</summary>
    public class UdpClientTransport : IClientTransport<ISimpleNetworkConnection>
    {
        private readonly UdpOptions _options;

        /// <summary>Constructs a <see cref="UdpClientTransport"/> that use the default <see cref="UdpOptions"/>.
        /// </summary>
        public UdpClientTransport() => _options = new UdpOptions();

        /// <summary>Constructs a <see cref="UdpClientTransport"/> that use the given <see cref="UdpOptions"/>.
        /// </summary>
        public UdpClientTransport(UdpOptions options) => _options = options;

        ISimpleNetworkConnection IClientTransport<ISimpleNetworkConnection>.CreateConnection(
            Endpoint remoteEndpoint,
            ILoggerFactory loggerFactory) =>
            new UdpClientNetworkConnection(remoteEndpoint, _options);
    }
}
