// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>A composite server transport.</summary>
    public class CompositeServerTransport<T> : IServerTransport<T> where T : INetworkConnection
    {
        private IReadOnlyDictionary<string, IServerTransport<T>>? _transports;
        private readonly Dictionary<string, IServerTransport<T>> _builder = new();

        /// <summary>Adds a new server transport to this composite server transport.</summary>
        /// <param name="name">The transport name.</param>
        /// <param name="transport">The transport instance.</param>
        /// <returns>This transport.</returns>
        public CompositeServerTransport<T> Add(string name, IServerTransport<T> transport)
        {
            if (_transports != null)
            {
                throw new InvalidOperationException(
                    $"cannot call {nameof(Add)} after calling {nameof(IClientTransport<T>.CreateConnection)}");
            }
            _builder.Add(name, transport);
            return this;
        }

        IListener<T> IServerTransport<T>.Listen(Endpoint endpoint, ILoggerFactory loggerFactory)
        {
            _transports ??= _builder;
            if (_transports.TryGetValue(endpoint.Transport, out IServerTransport<T>? serverTransport))
            {
                return serverTransport.Listen(endpoint, loggerFactory);
            }
            else
            {
                throw new UnknownTransportException(endpoint.Transport);
            }
        }
    }
}
