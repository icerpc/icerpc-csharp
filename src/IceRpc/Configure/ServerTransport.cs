// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

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

        (IListener?, INetworkConnection?) IServerTransport.Listen(Endpoint endpoint, ILoggerFactory loggerFactory)
        {
            _transports ??= _builder;
            if (_transports.TryGetValue(
                (endpoint.Transport, endpoint.Protocol),
                out IServerTransport? serverTransport))
            {
                return serverTransport.Listen(endpoint, loggerFactory);
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
            serverTransport.UseColoc(new());

        /// <summary>Adds the coloc server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport UseColoc(
            this ServerTransport serverTransport,
            SlicOptions options)
        {
            var colocServerTransport = new ColocServerTransport(options);
            serverTransport.Add(TransportNames.Coloc, Protocol.Ice2, colocServerTransport);
            serverTransport.Add(TransportNames.Coloc, Protocol.Ice1, colocServerTransport);
            return serverTransport;
        }

        /// <summary>Adds the ssl server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport UseSsl(
            this ServerTransport serverTransport,
            SslServerAuthenticationOptions authenticationOptions) =>
            serverTransport.Add(TransportNames.Ssl, Protocol.Ice1, new TcpServerTransport(authenticationOptions));

        /// <summary>Adds the ssl server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport UseSsl(
            this ServerTransport serverTransport,
            TcpOptions tcpOptions,
            SslServerAuthenticationOptions authenticationOptions) =>
            serverTransport.Add(TransportNames.Ssl,
                                Protocol.Ice1,
                                new TcpServerTransport(tcpOptions, new(), authenticationOptions));

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport UseTcp(this ServerTransport serverTransport) =>
            serverTransport.UseTcp(new TcpOptions(), new SlicOptions());

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="slicOptions">The Slic transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport UseTcp(
            this ServerTransport serverTransport,
            TcpOptions tcpOptions,
            SlicOptions slicOptions)
        {
            var tcpServerTransport = new TcpServerTransport(tcpOptions, slicOptions, null);
            serverTransport.Add(TransportNames.Tcp, Protocol.Ice2, tcpServerTransport);
            serverTransport.Add(TransportNames.Tcp, Protocol.Ice1, tcpServerTransport);
            return serverTransport;
        }

        /// <summary>Adds the tcp server transport with ssl support to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport UseTcp(
            this ServerTransport serverTransport,
            SslServerAuthenticationOptions authenticationOptions)
        {
            var tcpServerTransport = new TcpServerTransport(authenticationOptions);
            serverTransport.Add(TransportNames.Tcp, Protocol.Ice2, tcpServerTransport);
            serverTransport.Add(TransportNames.Tcp, Protocol.Ice1, tcpServerTransport);
            return serverTransport;
        }

        /// <summary>Adds the tcp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The transport being configured.</param>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="slicOptions">The Slic transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ServerTransport UseTcp(
            this ServerTransport serverTransport,
            TcpOptions tcpOptions,
            SlicOptions slicOptions,
            SslServerAuthenticationOptions authenticationOptions)
        {
            var tcpServerTransport = new TcpServerTransport(tcpOptions, slicOptions, authenticationOptions);
            serverTransport.Add(TransportNames.Tcp, Protocol.Ice2, tcpServerTransport);
            serverTransport.Add(TransportNames.Tcp, Protocol.Ice1, tcpServerTransport);
            return serverTransport;
        }

        /// <summary>Adds the udp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The server transport being configured.</param>
        /// <returns>The server transport being configured.</returns>
        public static ServerTransport UseUdp(this ServerTransport serverTransport) =>
            serverTransport.UseUdp(new UdpOptions());

        /// <summary>Adds the udp server transport to this composite server transport.</summary>
        /// <param name="serverTransport">The composite server transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The server transport being configured.</returns>
        public static ServerTransport UseUdp(this ServerTransport serverTransport, UdpOptions options) =>
            serverTransport.Add(TransportNames.Udp, Protocol.Ice1, new UdpServerTransport(options));
    }
}
