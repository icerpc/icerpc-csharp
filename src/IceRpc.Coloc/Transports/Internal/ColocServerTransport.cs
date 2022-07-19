// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

/// <summary>Implements <see cref="IDuplexServerTransport"/> for the coloc transport.</summary>
internal class ColocServerTransport : IDuplexServerTransport
{
    /// <inheritdoc/>
    public string Name => ColocTransport.Name;

    private readonly ConcurrentDictionary<Endpoint, ColocListener> _listeners;

    /// <inheritdoc/>
    IDuplexListener IDuplexServerTransport.Listen(
        Endpoint endpoint,
        SslServerAuthenticationOptions? authenticationOptions,
        ILogger logger)
    {
        if (authenticationOptions is not null)
        {
            throw new NotSupportedException("cannot create secure Coloc server");
        }

        if (!ColocTransport.CheckParams(endpoint))
        {
            throw new FormatException($"cannot create a Coloc listener for endpoint '{endpoint}'");
        }

        var listener = new ColocListener(endpoint.WithTransport(Name));
        if (!_listeners.TryAdd(listener.Endpoint, listener))
        {
            throw new TransportException($"endpoint '{listener.Endpoint}' is already in use");
        }
        return listener;
    }

    internal ColocServerTransport(ConcurrentDictionary<Endpoint, ColocListener> listeners) =>
        _listeners = listeners;
}
