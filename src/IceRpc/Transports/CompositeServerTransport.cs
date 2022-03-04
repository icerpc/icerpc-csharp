// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports
{
    /// <summary>A composite server transport.</summary>
    public class CompositeServerTransport<T> : IServerTransport<T> where T : INetworkConnection
    {
        /// <inheritdoc/>
        public string Name => TransportNames.Composite;

        private readonly Dictionary<string, IServerTransport<T>> _builder = new();
        private string? _defaultTransport; // the name of the first transport added to _transports
        private IReadOnlyDictionary<string, IServerTransport<T>>? _transports;

        /// <summary>Adds a new server transport to this composite server transport with the specifed name.</summary>
        /// <param name="name">The transport name.</param>
        /// <param name="transport">The transport instance.</param>
        /// <returns>This composite transport.</returns>
        public CompositeServerTransport<T> Add(string name, IServerTransport<T> transport)
        {
            if (_transports != null)
            {
                throw new InvalidOperationException(
                    $"cannot call {nameof(Add)} after calling {nameof(IClientTransport<T>.CreateConnection)}");
            }

            _builder.Add(name, transport);
            _defaultTransport ??= name;
            return this;
        }

        /// <summary>Adds a new server transport to this composite server transport.</summary>
        /// <param name="transport">The transport instance.</param>
        /// <returns>This composite transport.</returns>
        public CompositeServerTransport<T> Add(IServerTransport<T> transport) => Add(transport.Name, transport);

        IListener<T> IServerTransport<T>.Listen(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            ILogger logger)
        {
            _transports ??= _builder;

            if (!endpoint.Params.TryGetValue("transport", out string? endpointTransport))
            {
                endpointTransport = _defaultTransport ?? throw new InvalidOperationException("no transport configured");
            }

            return _transports.TryGetValue(endpointTransport, out IServerTransport<T>? serverTransport) ?
                serverTransport.Listen(endpoint, authenticationOptions, logger) :
                throw new UnknownTransportException(endpointTransport);
        }
    }
}
