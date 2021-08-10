// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace IceRpc.Configure
{
    /// <summary>A composite client transport.</summary>
    public class ClientTransport : IClientTransport
    {
        private IReadOnlyDictionary<string, IClientTransport>? _transports;
        private readonly Dictionary<string, IClientTransport> _builder = new();

        /// <summary>Adds a new client transport to this composite clien transport.</summary>
        /// <param name="name">The transport name.</param>
        /// <param name="transport">The transport instance.</param>
        public void Add(string name, IClientTransport transport)
        {
            if (_transports != null)
            {
                throw new InvalidOperationException(
                    $"cannot call {nameof(Add)} after calling {nameof(IClientTransport.CreateConnection)}");
            }
            _builder.Add(name, transport);
        }

        MultiStreamConnection IClientTransport.CreateConnection(
            Endpoint remoteEndpoint,
            ClientConnectionOptions connectionOptions,
            ILoggerFactory loggerFactory)
        {
            _transports ??=  _builder;
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

    /// <summary>Extension methods for class <see cref="ClientTransportBuilder"/>.</summary>
    public static class ClientTransportExtensions
    {
        /// <summary>Adds the coloc client transport to this composite client transport.</summary>
        /// <param name="compositeTransport">The composite client transport being configured.</param>
        /// <returns>The builder.</returns>
        public static ClientTransport UseColoc(this ClientTransport compositeTransport)
        {
            compositeTransport.Add(TransportNames.Coloc, new ColocClientTransport());
            return compositeTransport;
        }

        /// <summary>Adds the ssl client transport to this composite client transport.</summary>
        /// <param name="compositeTransport">The composite client transport being configured.</param>
        /// <returns>The builder.</returns>
        public static ClientTransport UseSsl(this ClientTransport compositeTransport)
        {
            compositeTransport.Add(TransportNames.Ssl, new TcpClientTransport());
            return compositeTransport;
        }

        /// <summary>Adds the ssl client transport to this composite client transport.</summary>
        /// <param name="compositeTransport">The composite client transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The builder.</returns>
        public static ClientTransport UseSsl(this ClientTransport compositeTransport, TcpOptions options)
        {
            compositeTransport.Add(TransportNames.Ssl, new TcpClientTransport(options));
            return compositeTransport;
        }

        /// <summary>Adds the tcp client transport to this composite client transport.</summary>
        /// <param name="compositeTransport">The composite client transport being configured.</param>
        /// <returns>The builder.</returns>
        public static ClientTransport UseTcp(this ClientTransport compositeTransport)
        {
            compositeTransport.Add(TransportNames.Tcp, new TcpClientTransport());
            return compositeTransport;
        }

        /// <summary>Adds the tcp client transport to this composite client transport.</summary>
        /// <param name="compositeTransport">The composite client transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The builder.</returns>
        public static ClientTransport UseTcp(this ClientTransport compositeTransport, TcpOptions options)
        {
            compositeTransport.Add(TransportNames.Tcp, new TcpClientTransport(options));
            return compositeTransport;
        }

        /// <summary>Adds the udp client transport to this composite client transport.</summary>
        /// <param name="compositeTransport">The composite client transport being configured.</param>
        /// <returns>The builder.</returns>
        public static ClientTransport UseUdp(this ClientTransport compositeTransport)
        {
            compositeTransport.Add(TransportNames.Udp, new UdpClientTransport());
            return compositeTransport;
        }

        /// <summary>Adds the udp client transport to this composite client transport.</summary>
        /// <param name="compositeTransport">The composite client transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The builder.</returns>
        public static ClientTransport UseUdp(this ClientTransport compositeTransport, UdpOptions options)
        {
            compositeTransport.Add(TransportNames.Udp, new UdpClientTransport(options));
            return compositeTransport;
        }
    }
}
