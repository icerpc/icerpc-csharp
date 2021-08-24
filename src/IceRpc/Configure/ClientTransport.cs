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
            MultiStreamOptions options) =>
            clientTransport.Add(TransportNames.Coloc, Protocol.Ice2, new ColocClientTransport(options));

        /// <summary>Adds the tcp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseTcp(this ClientTransport clientTransport) =>
            clientTransport.UseTcp(new TcpOptions());

        /// <summary>Adds the tcp client transport with ssl support to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseTcp(
            this ClientTransport clientTransport,
            SslClientAuthenticationOptions authenticationOptions) =>
            clientTransport.UseTcp(new TcpOptions(), authenticationOptions);

        /// <summary>Adds the tcp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseTcp(this ClientTransport clientTransport, TcpOptions options) =>
            clientTransport.Add(TransportNames.Tcp, Protocol.Ice2, new TcpClientTransport(options));

        /// <summary>Adds the tcp client transport with ssl support to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseTcp(
            this ClientTransport clientTransport,
            TcpOptions options,
            SslClientAuthenticationOptions authenticationOptions) =>
            clientTransport.Add(TransportNames.Tcp,
                                Protocol.Ice2,
                                new TcpClientTransport(options, authenticationOptions));
    }
}
