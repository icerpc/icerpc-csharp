// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports
{
    /// <summary>A composite client transport.</summary>
    public class CompositeClientTransport<T> : IClientTransport<T> where T : INetworkConnection
    {
        private readonly Dictionary<string, IClientTransport<T>> _builder = new();
        private IReadOnlyDictionary<string, IClientTransport<T>>? _transports;

        private string? _defaultTransport; // the name of the first transport added to _transports

        /// <summary>Adds a new client transport to this composite client transport.</summary>
        /// <param name="name">The transport name.</param>
        /// <param name="transport">The transport instance.</param>
        /// <returns>This transport.</returns>
        public CompositeClientTransport<T> Add(string name, IClientTransport<T> transport)
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

        T IClientTransport<T>.CreateConnection(
            Endpoint remoteEndpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            ILogger logger)
        {
            _transports ??= _builder;

            if (!remoteEndpoint.Params.TryGetValue("transport", out string? endpointTransport))
            {
                endpointTransport = _defaultTransport ?? throw new InvalidOperationException("no transport configured");
            }

            if (_transports.TryGetValue(endpointTransport, out IClientTransport<T>? clientTransport))
            {
                return clientTransport.CreateConnection(remoteEndpoint, authenticationOptions, logger);
            }
            else
            {
                throw new UnknownTransportException(endpointTransport);
            }
        }
    }
}
