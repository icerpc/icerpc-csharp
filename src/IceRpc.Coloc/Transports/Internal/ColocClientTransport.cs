// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

/// <summary>Implements <see cref="IClientTransport{ISimpleNetworkConnection}"/> for the coloc transport.</summary>
internal class ColocClientTransport : IClientTransport<ISimpleNetworkConnection>
{
    /// <inheritdoc/>
    public string Name => ColocTransport.Name;

    private readonly ConcurrentDictionary<Endpoint, ColocListener> _listeners;

    /// <inheritdoc/>
    public bool CheckParams(Endpoint endpoint) => ColocTransport.CheckParams(endpoint);

    /// <inheritdoc/>
    ISimpleNetworkConnection IClientTransport<ISimpleNetworkConnection>.CreateConnection(
        ref Endpoint endpoint,
        SslClientAuthenticationOptions? authenticationOptions,
        ILogger logger)
    {
        if (authenticationOptions is not null)
        {
            throw new NotSupportedException("cannot create a secure Coloc connection");
        }

        if (!CheckParams(endpoint))
        {
            throw new FormatException($"cannot create a Coloc connection to endpoint '{endpoint}'");
        }

        endpoint = endpoint.WithTransport(Name);

        return new ColocNetworkConnection(endpoint, Connect);
    }

    internal ColocClientTransport(ConcurrentDictionary<Endpoint, ColocListener> listeners) =>
        _listeners = listeners;

    private (PipeReader, PipeWriter) Connect(Endpoint endpoint)
    {
        if (_listeners.TryGetValue(endpoint, out ColocListener? listener))
        {
            return listener.NewClientConnection();
        }
        else
        {
            throw new ConnectionRefusedException();
        }
    }
}
