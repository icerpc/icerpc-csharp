// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Configure
{
    /// <summary>A composite client transport.</summary>
    public class ClientTransport : IClientTransport
    {
        private IReadOnlyDictionary<(string, Protocol), IClientTransport>? _transports;
        private readonly Dictionary<(string, Protocol), IClientTransport> _builder = new();

        /// <summary>Adds a new client transport to this composite client transport.</summary>
        /// <param name="name">The transport name.</param>
        /// <param name="protocol">The Ice protocol supported by this transport.</param>
        /// <param name="transport">The transport instance.</param>
        /// <returns>This transport.</returns>
        public ClientTransport Add(string name, Protocol protocol, IClientTransport transport)
        {
            if (_transports != null)
            {
                throw new InvalidOperationException(
                    $"cannot call {nameof(Add)} after calling {nameof(IClientTransport.CreateConnection)}");
            }
            _builder.Add((name, protocol), transport);
            return this;
        }

        MultiStreamConnection IClientTransport.CreateConnection(Endpoint remoteEndpoint, ILoggerFactory loggerFactory)
        {
            _transports ??= _builder;
            if (_transports.TryGetValue(
                (remoteEndpoint.Transport, remoteEndpoint.Protocol),
                out IClientTransport? clientTransport))
            {
                return clientTransport.CreateConnection(remoteEndpoint, loggerFactory);
            }
            else
            {
                throw new UnknownTransportException(remoteEndpoint.Transport, remoteEndpoint.Protocol);
            }
        }
    }

    /// <summary>Extension methods for class <see cref="ClientTransport"/>.</summary>
    public static class ClientTransportExtensions
    {
        /// <summary>Adds the coloc client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseColoc(this ClientTransport clientTransport) =>
            clientTransport.UseColoc(new());

        /// <summary>Adds the coloc client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseColoc(
            this ClientTransport clientTransport,
            MultiStreamOptions options)
        {
            var colocClientTransport = new ColocClientTransport(options);
            clientTransport.Add(TransportNames.Coloc, Protocol.Ice2, colocClientTransport);
            clientTransport.Add(TransportNames.Coloc, Protocol.Ice1, colocClientTransport);
            return clientTransport;
        }

        /// <summary>Adds the ssl client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseSsl(
            this ClientTransport clientTransport,
            SslClientAuthenticationOptions authenticationOptions) =>
            clientTransport.Add(TransportNames.Ssl, Protocol.Ice1, new TcpClientTransport(authenticationOptions));

        /// <summary>Adds the ssl client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseSsl(
            this ClientTransport clientTransport,
            TcpOptions tcpOptions,
            SslClientAuthenticationOptions authenticationOptions) =>
            clientTransport.Add(TransportNames.Ssl,
                                Protocol.Ice1,
                                new TcpClientTransport(tcpOptions, new SlicOptions(), authenticationOptions));

        /// <summary>Adds the tcp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseTcp(this ClientTransport clientTransport) =>
            clientTransport.UseTcp(new TcpOptions(), new SlicOptions());

        /// <summary>Adds the tcp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="slicOptions">The Slic transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseTcp(
            this ClientTransport clientTransport,
            TcpOptions tcpOptions,
            SlicOptions slicOptions)
        {
            var tcpClientTransport = new TcpClientTransport(tcpOptions, slicOptions, null);
            clientTransport.Add(TransportNames.Tcp, Protocol.Ice2, tcpClientTransport);
            clientTransport.Add(TransportNames.Tcp, Protocol.Ice1, tcpClientTransport);
            return clientTransport;
        }

        /// <summary>Adds the tcp client transport with ssl support to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseTcp(
            this ClientTransport clientTransport,
            SslClientAuthenticationOptions authenticationOptions)
        {
            var tcpClientTransport = new TcpClientTransport(authenticationOptions);
            clientTransport.Add(TransportNames.Tcp, Protocol.Ice2, tcpClientTransport);
            clientTransport.Add(TransportNames.Tcp, Protocol.Ice1, tcpClientTransport);
            return clientTransport;
        }

        /// <summary>Adds the tcp client transport with ssl support to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="slicOptions">The Slic transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseTcp(
            this ClientTransport clientTransport,
            TcpOptions tcpOptions,
            SlicOptions slicOptions,
            SslClientAuthenticationOptions authenticationOptions)
        {
            var tcpClientTransport = new TcpClientTransport(tcpOptions, slicOptions, authenticationOptions);
            clientTransport.Add(TransportNames.Tcp, Protocol.Ice2, tcpClientTransport);
            clientTransport.Add(TransportNames.Tcp, Protocol.Ice1, tcpClientTransport);
            return clientTransport;
        }

        /// <summary>Adds the udp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The client transport being configured.</param>
        /// <returns>The client transport being configured.</returns>
        public static ClientTransport UseUdp(this ClientTransport clientTransport) =>
            clientTransport.UseUdp(new UdpOptions());

        /// <summary>Adds the udp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The client transport being configured.</param>
        /// <param name="udpOptions">The UDP transport options.</param>
        /// <returns>The client transport being configured.</returns>
        public static ClientTransport UseUdp(this ClientTransport clientTransport, UdpOptions udpOptions) =>
            clientTransport.Add(TransportNames.Udp, Protocol.Ice1, new UdpClientTransport(udpOptions));
    }
}
