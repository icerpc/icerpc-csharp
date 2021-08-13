// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Configure
{
    /// <summary>Builds a composite server transport.</summary>
    public class ServerTransport : IServerTransport
    {
        private IReadOnlyDictionary<(string, Protocol), IServerTransport>? _transports;
        private readonly Dictionary<(string, Protocol), IServerTransport> _builder = new();

        /// <summary>Adds a new server transport to this composite server transport.</summary>
        /// <param name="name">The transport name.</param>
        /// <param name="protocol">The Ice protocol supported by this transport.</param>
        /// <param name="transport">The transport instance.</param>
        /// <returns>This transport.</returns>
        public ServerTransport Add(string name, Protocol protocol, IServerTransport transport)
        {
            if (_transports != null)
            {
                throw new InvalidOperationException(
                    $"cannot call {nameof(Add)} after calling {nameof(IClientTransport.CreateConnection)}");
            }
            _builder.Add((name, protocol), transport);
            return this;
        }

        (IListener?, MultiStreamConnection?) IServerTransport.Listen(
            Endpoint endpoint,
            ServerConnectionOptions connectionOptions,
            ILoggerFactory loggerFactory)
        {
            _transports ??= _builder;
            if (_transports.TryGetValue(
                (endpoint.Transport, endpoint.Protocol),
                out IServerTransport? serverTransport))
            {
                return serverTransport.Listen(endpoint, connectionOptions, loggerFactory);
            }
            else
            {
                throw new UnknownTransportException(endpoint.Transport, endpoint.Protocol);
            }
        }
    }

    /// <summary>Extension methods for class <see cref="ServerTransport"/>.</summary>
    public static class ServerTransportExtensions
    {
        /// <summary>Adds the coloc server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport UseColoc(this ServerTransport serverTransport) =>
            serverTransport.Add(TransportNames.Coloc, Protocol.Ice2, new ColocServerTransport());

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport UseTcp(this ServerTransport serverTransport) =>
            serverTransport.UseTcp(new TcpOptions());

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport UseTcp(this ServerTransport serverTransport, TcpOptions options) =>
            serverTransport.Add(TransportNames.Tcp, Protocol.Ice2, new TcpServerTransport(options));
    }
}
