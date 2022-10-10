// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

/// <summary>Implements <see cref="IDuplexServerTransport" /> for the coloc transport.</summary>
internal class ColocServerTransport : IDuplexServerTransport
{
    /// <inheritdoc/>
    public string Name => ColocTransport.Name;

    private readonly ConcurrentDictionary<ServerAddress, ColocListener> _listeners;

    /// <inheritdoc/>
    public IListener<IDuplexConnection> Listen(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        if (serverAuthenticationOptions is not null)
        {
            throw new NotSupportedException("cannot create secure Coloc server");
        }

        if (!ColocTransport.CheckParams(serverAddress))
        {
            throw new FormatException($"cannot create a Coloc listener for server address '{serverAddress}'");
        }

        var listener = new ColocListener(serverAddress with { Transport = Name }, options);
        if (!_listeners.TryAdd(listener.ServerAddress, listener))
        {
            throw new TransportException(TransportErrorCode.AddressInUse);
        }
        return listener;
    }

    internal ColocServerTransport(ConcurrentDictionary<ServerAddress, ColocListener> listeners) =>
        _listeners = listeners;
}
