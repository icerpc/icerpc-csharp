// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace IceRpc.Transports
{
    /// <summary>Builds a composite client transport.</summary>
    public class ClientTransportBuilder : Dictionary<string, IClientTransport>
    {
        /// <summary>Builds a new client transport.</summary>
        /// <returns>The new client transport.</returns>
        public IClientTransport Build() => new CompositeClientTransport(this);

        /// <summary>Implements <see cref="IClientTransport"/> using other client transport implementations.</summary>
        private class CompositeClientTransport : IClientTransport
        {
            private readonly IReadOnlyDictionary<string, IClientTransport> _transports;

            internal CompositeClientTransport(IReadOnlyDictionary<string, IClientTransport> transports) =>
                _transports = transports;

            MultiStreamConnection IClientTransport.CreateConnection(
                Endpoint remoteEndpoint,
                ClientConnectionOptions connectionOptions,
                ILoggerFactory loggerFactory)
            {
                if (_transports.TryGetValue(remoteEndpoint.Transport, out IClientTransport? clientTransport))
                {
                    return clientTransport.CreateConnection(remoteEndpoint, connectionOptions, loggerFactory);
                }
                else
                {
                    throw new UnknownTransportException(remoteEndpoint.Transport);
                }
            }
        }
    }

    /// <summary>Extension methods for class <see cref="ClientTransportBuilder"/>.</summary>
    public static class ClientTransportBuilderExtensions
    {
        /// <summary>Adds the coloc client transport to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <returns>The builder.</returns>
        public static ClientTransportBuilder AddColoc(this ClientTransportBuilder builder)
        {
            builder.Add(TransportNames.Coloc, new ColocClientTransport());
            return builder;
        }

        /// <summary>Adds the ssl client transport to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <returns>The builder.</returns>
        public static ClientTransportBuilder AddSsl(this ClientTransportBuilder builder)
        {
            builder.Add(TransportNames.Ssl, new TcpClientTransport());
            return builder;
        }

        /// <summary>Adds the ssl client transport to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The builder.</returns>
        public static ClientTransportBuilder AddSsl(this ClientTransportBuilder builder, TcpOptions options)
        {
            builder.Add(TransportNames.Ssl, new TcpClientTransport(options));
            return builder;
        }

        /// <summary>Adds the tcp client transport to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <returns>The builder.</returns>
        public static ClientTransportBuilder AddTcp(this ClientTransportBuilder builder)
        {
            builder.Add(TransportNames.Tcp, new TcpClientTransport());
            return builder;
        }

        /// <summary>Adds the tcp client transport to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The builder.</returns>
        public static ClientTransportBuilder AddTcp(this ClientTransportBuilder builder, TcpOptions options)
        {
            builder.Add(TransportNames.Tcp, new TcpClientTransport(options));
            return builder;
        }

        /// <summary>Adds the udp client transport to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <returns>The builder.</returns>
        public static ClientTransportBuilder AddUdp(this ClientTransportBuilder builder)
        {
            builder.Add(TransportNames.Udp, new UdpClientTransport());
            return builder;
        }

        /// <summary>Adds the udp client transport to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The builder.</returns>
        public static ClientTransportBuilder AddUdp(this ClientTransportBuilder builder, UdpOptions options)
        {
            builder.Add(TransportNames.Udp, new UdpClientTransport(options));
            return builder;
        }
    }
}
