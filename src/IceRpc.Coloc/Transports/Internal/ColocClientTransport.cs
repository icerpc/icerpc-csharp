// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

/// <summary>Implements <see cref="IDuplexClientTransport"/> for the coloc transport.</summary>
internal class ColocClientTransport : IDuplexClientTransport
{
    /// <inheritdoc/>
    public string Name => ColocTransport.Name;

    private readonly ConcurrentDictionary<Endpoint, ColocListener> _listeners;

    /// <inheritdoc/>
    public bool CheckParams(Endpoint endpoint) => ColocTransport.CheckParams(endpoint);

    /// <inheritdoc/>
    public IDuplexConnection CreateConnection(
        Endpoint endpoint,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        if (clientAuthenticationOptions is not null)
        {
            throw new NotSupportedException("cannot create a secure Coloc connection");
        }

        if (!CheckParams(endpoint))
        {
            throw new FormatException($"cannot create a Coloc connection to endpoint '{endpoint}'");
        }

        return new ColocConnection(endpoint with { Transport = Name }, endpoint => Connect(endpoint, options));
    }

    internal ColocClientTransport(ConcurrentDictionary<Endpoint, ColocListener> listeners) =>
        _listeners = listeners;

    private (PipeReader, PipeWriter) Connect(Endpoint endpoint, DuplexConnectionOptions options)
    {
        if (_listeners.TryGetValue(endpoint, out ColocListener? listener))
        {
            return listener.NewClientConnection(options);
        }
        else
        {
            throw new ConnectionRefusedException();
        }
    }
}
