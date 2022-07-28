// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

/// <summary>Implements <see cref="IDuplexServerTransport"/> for the coloc transport.</summary>
internal class ColocServerTransport : IDuplexServerTransport
{
    /// <inheritdoc/>
    public IDuplexListener Listen(DuplexListenerOptions options)
    {
        if (options.ServerConnectionOptions.ServerAuthenticationOptions is not null)
        {
            throw new NotSupportedException("cannot create secure Coloc server");
        }

        if (!ColocTransport.CheckParams(options.Endpoint))
        {
            throw new FormatException($"cannot create a Coloc listener for endpoint '{options.Endpoint}'");
        }

        var listener = new ColocListener(options with { Endpoint = options.Endpoint.WithTransport(Name) });
        if (!_listeners.TryAdd(listener.Endpoint, listener))
        {
            throw new TransportException($"endpoint '{listener.Endpoint}' is already in use");
        }
        return listener;
    }

    /// <inheritdoc/>
    public string Name => ColocTransport.Name;

    private readonly ConcurrentDictionary<Endpoint, ColocListener> _listeners;

    internal ColocServerTransport(ConcurrentDictionary<Endpoint, ColocListener> listeners) =>
        _listeners = listeners;
}
