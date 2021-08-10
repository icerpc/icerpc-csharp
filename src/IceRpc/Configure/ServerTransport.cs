// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System;

namespace IceRpc.Configure
{
    /// <summary>Builds a composite server transport.</summary>
    public class ServerTransport : IServerTransport
    {
        private IReadOnlyDictionary<string, IServerTransport>? _transports;
        private readonly Dictionary<string, IServerTransport> _builder = new();

        /// <summary>Adds a new server transport to this composite server transport.</summary>
        /// <param name="name">The transport name.</param>
        /// <param name="transport">The transport instance.</param>
        public void Add(string name, IServerTransport transport)
        {
            if (_transports != null)
            {
                throw new InvalidOperationException(
                    $"cannot call {nameof(Add)} after calling {nameof(IClientTransport.CreateConnection)}");
            }
            _builder.Add(name, transport);
        }

        (IListener?, MultiStreamConnection?) IServerTransport.Listen(
            Endpoint endpoint,
            ServerConnectionOptions connectionOptions,
            ILoggerFactory loggerFactory)
        {
            _transports = _builder;
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

    /// <summary>Extension methods for class <see cref="ServerTransport"/>.</summary>
    public static class ServerTransportExtensions
    {
        /// <summary>Adds the coloc server transport to this composite server transport.</summary>
        /// <param name="compositeTransport">The composite server transport being configured.</param>
        /// <returns>The composite server transport.</returns>
        public static ServerTransport UseColoc(this ServerTransport compositeTransport)
        {
            compositeTransport.Add(TransportNames.Coloc, new ColocServerTransport());
            return compositeTransport;
        }

        /// <summary>Adds the ssl server transport to this composite server transport.</summary>
        /// <param name="compositeTransport">The composite server transport being configured.</param>
        /// <returns>The composite server transport.</returns>
        public static ServerTransport UseSsl(this ServerTransport compositeTransport)
        {
            compositeTransport.Add(TransportNames.Ssl, new TcpServerTransport());
            return compositeTransport;
        }

        /// <summary>Adds the ssl server transport to this composite server transport.</summary>
        /// <param name="compositeTransport">The composite server transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The composite server transport.</returns>
        public static ServerTransport UseSsl(this ServerTransport compositeTransport, TcpOptions options)
        {
            compositeTransport.Add(TransportNames.Ssl, new TcpServerTransport(options));
            return compositeTransport;
        }

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="compositeTransport">The composite server transport being configured.</param>
        /// <returns>The composite server transport.</returns>
        public static ServerTransport UseTcp(this ServerTransport compositeTransport)
        {
            compositeTransport.Add(TransportNames.Tcp, new TcpServerTransport());
            return compositeTransport;
        }

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="compositeTransport">The composite server transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The composite server transport.</returns>
        public static ServerTransport UseTcp(this ServerTransport compositeTransport, TcpOptions options)
        {
            compositeTransport.Add(TransportNames.Tcp, new TcpServerTransport(options));
            return compositeTransport;
        }

        /// <summary>Adds the udp server transport to this composite server transport.</summary>
        /// <param name="compositeTransport">The composite server transport being configured.</param>
        /// <returns>The composite server transport.</returns>
        public static ServerTransport UseUdp(this ServerTransport compositeTransport)
        {
            compositeTransport.Add(TransportNames.Udp, new UdpServerTransport());
            return compositeTransport;
        }

        /// <summary>Adds the udp server transport to this composite server transport.</summary>
        /// <param name="compositeTransport">The composite server transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The composite server transport.</returns>
        public static ServerTransport UseUdp(this ServerTransport compositeTransport, UdpOptions options)
        {
            compositeTransport.Add(TransportNames.Udp, new UdpServerTransport(options));
            return compositeTransport;
        }
    }
}
