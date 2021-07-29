// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace IceRpc.Transports
{
    /// <summary>Builds a composite server transports.</summary>
    public class ServerTransportBuilder : Dictionary<string, IServerTransport>
    {
        /// <summary>Builds a new server transport.</summary>
        /// <returns>The new server transport.</returns>
        public IServerTransport Build() => new CompositeServerTransport(this);

        /// <summary>Implements <see cref="IServerTransport"/> using other server transport implementations.</summary>
        private class CompositeServerTransport : IServerTransport
        {
            private readonly IReadOnlyDictionary<string, IServerTransport> _transports;

            internal CompositeServerTransport(IReadOnlyDictionary<string, IServerTransport> transports) =>
                _transports = transports;

            /// <inheritdoc/>
            public (IListener?, MultiStreamConnection?) Listen(
                Endpoint endpoint,
                ServerConnectionOptions connectionOptions,
                ILoggerFactory loggerFactory)
            {
                if (_transports.TryGetValue(endpoint.Transport, out IServerTransport? serverTransport))
                {
                    return serverTransport.Listen(endpoint, connectionOptions, loggerFactory);
                }
                else
                {
                    throw new UnknownTransportException(endpoint.Transport);
                }
            }
        }
    }

    /// <summary>Extension methods for class <see cref="ServerTransportBuilder"/>.</summary>
    public static class ServerTransportBuilderExtensions
    {
        /// <summary>Adds the coloc server transport to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <returns>The builder.</returns>
        public static ServerTransportBuilder AddColoc(this ServerTransportBuilder builder)
        {
            builder.Add(TransportNames.Coloc, new ColocServerTransport());
            return builder;
        }

        /// <summary>Adds the ssl server transport to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <returns>The builder.</returns>
        public static ServerTransportBuilder AddSsl(this ServerTransportBuilder builder)
        {
            builder.Add(TransportNames.Ssl, new TcpServerTransport());
            return builder;
        }

        /// <summary>Adds the ssl server transport to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The builder.</returns>
        public static ServerTransportBuilder AddSsl(this ServerTransportBuilder builder, TcpOptions options)
        {
            builder.Add(TransportNames.Ssl, new TcpServerTransport(options));
            return builder;
        }

        /// <summary>Adds the tcp server transport to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <returns>The builder.</returns>
        public static ServerTransportBuilder AddTcp(this ServerTransportBuilder builder)
        {
            builder.Add(TransportNames.Tcp, new TcpServerTransport());
            return builder;
        }

        /// <summary>Adds the tcp server transport to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The builder.</returns>
        public static ServerTransportBuilder AddTcp(this ServerTransportBuilder builder, TcpOptions options)
        {
            builder.Add(TransportNames.Tcp, new TcpServerTransport(options));
            return builder;
        }

        /// <summary>Adds the udp server transport to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <returns>The builder.</returns>
        public static ServerTransportBuilder AddUdp(this ServerTransportBuilder builder)
        {
            builder.Add(TransportNames.Udp, new UdpServerTransport());
            return builder;
        }

        /// <summary>Adds the udp server transport to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The builder.</returns>
        public static ServerTransportBuilder AddUdp(this ServerTransportBuilder builder, UdpOptions options)
        {
            builder.Add(TransportNames.Udp, new UdpServerTransport(options));
            return builder;
        }
    }
}
