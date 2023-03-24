// Copyright (c) ZeroC, Inc.

using System.Collections.Concurrent;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

/// <summary>Implements <see cref="IDuplexServerTransport" /> for the coloc transport.</summary>
internal class ColocServerTransport : IDuplexServerTransport
{
    /// <inheritdoc/>
    public string Name => ColocTransport.Name;

    private readonly ConcurrentDictionary<ServerAddress, ColocListener> _listeners;
    private readonly ColocTransportOptions _options;

    /// <inheritdoc/>
    public IListener<IDuplexConnection> Listen(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        if (serverAuthenticationOptions is not null)
        {
            throw new NotSupportedException("The Coloc server transport does not support SSL.");
        }

        if (!ColocTransport.CheckParams(serverAddress))
        {
            throw new ArgumentException(
                $"The server address '{serverAddress}' contains parameters that are not valid for the Coloc transport.",
                nameof(serverAddress));
        }

        var listener = new ColocListener(
            serverAddress with { Transport = Name },
            colocTransportOptions: _options,
            duplexConnectionOptions: options);

        if (!_listeners.TryAdd(listener.ServerAddress, listener))
        {
            throw new IceRpcException(IceRpcError.AddressInUse);
        }
        return listener;
    }

    internal ColocServerTransport(
        ConcurrentDictionary<ServerAddress, ColocListener> listeners,
        ColocTransportOptions options)
    {
        _listeners = listeners;
        _options = options;
    }
}
