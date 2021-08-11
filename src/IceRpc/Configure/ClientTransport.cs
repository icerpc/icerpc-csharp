// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Configure
{
    /// <summary>A composite client transport.</summary>
    public class ClientTransport : IClientTransport
    {
        private IReadOnlyDictionary<string, IClientTransport>? _transports;
        private readonly Dictionary<string, IClientTransport> _builder = new();

        /// <summary>Adds a new client transport to this composite client transport.</summary>
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
            _transports ??= _builder;
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

    /// <summary>Extension methods for class <see cref="ClientTransport"/>.</summary>
    public static class ClientTransportExtensions
    {
        /// <summary>Adds the coloc client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseColoc(this ClientTransport clientTransport)
        {
            clientTransport.Add(TransportNames.Coloc, new ColocClientTransport());
            return clientTransport;
        }

        /// <summary>Adds the ssl client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseSsl(this ClientTransport clientTransport)
        {
            clientTransport.Add(TransportNames.Ssl, new TcpClientTransport());
            return clientTransport;
        }

        /// <summary>Adds the ssl client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseSsl(this ClientTransport clientTransport, TcpOptions options)
        {
            clientTransport.Add(TransportNames.Ssl, new TcpClientTransport(options));
            return clientTransport;
        }

        /// <summary>Adds the tcp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseTcp(this ClientTransport clientTransport)
        {
            clientTransport.Add(TransportNames.Tcp, new TcpClientTransport());
            return clientTransport;
        }

        /// <summary>Adds the tcp client transport to this composite client transport.</summary>
        /// <param name="clientTransport">The transport being configured.</param>
        /// <param name="options">The transport options.</param>
        /// <returns>The transport being configured.</returns>
        public static ClientTransport UseTcp(this ClientTransport clientTransport, TcpOptions options)
        {
            clientTransport.Add(TransportNames.Tcp, new TcpClientTransport(options));
            return clientTransport;
        }
    }
}
