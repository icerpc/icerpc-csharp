// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>A composite client transport.</summary>
    public class CompositeClientTransport<T> : IClientTransport<T> where T : INetworkConnection
    {
        private IReadOnlyDictionary<string, IClientTransport<T>>? _transports;
        private readonly Dictionary<string, IClientTransport<T>> _builder = new();

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
            return this;
        }

        T IClientTransport<T>.CreateConnection(Endpoint remoteEndpoint, ILogger logger)
        {
            _transports ??= _builder;
            if (_transports.TryGetValue(remoteEndpoint.Transport,
                                        out IClientTransport<T>? clientTransport))
            {
                return clientTransport.CreateConnection(remoteEndpoint, logger);
            }
            else
            {
                throw new UnknownTransportException(remoteEndpoint.Transport);
            }
        }
    }
}
