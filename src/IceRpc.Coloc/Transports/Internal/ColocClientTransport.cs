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
        Endpoint remoteEndpoint,
        SslClientAuthenticationOptions? authenticationOptions,
        ILogger logger)
    {
        if (authenticationOptions != null)
        {
            throw new NotSupportedException("cannot create a secure Coloc connection");
        }

        if (!CheckParams(remoteEndpoint))
        {
            throw new FormatException($"cannot create a Coloc connection to endpoint '{remoteEndpoint}'");
        }

        remoteEndpoint = remoteEndpoint.WithTransport(Name);

        return new ColocNetworkConnection(remoteEndpoint, Connect);
    }

    internal ColocClientTransport(ConcurrentDictionary<Endpoint, ColocListener> listeners) =>
        _listeners = listeners;

    private (PipeReader, PipeWriter) Connect(Endpoint remoteEndpoint)
    {
        if (_listeners.TryGetValue(remoteEndpoint, out ColocListener? listener))
        {
            return listener.NewClientConnection();
        }
        else
        {
            throw new ConnectionRefusedException();
        }
    }
}
