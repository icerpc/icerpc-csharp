// Copyright (c) ZeroC, Inc.

using System.Collections.Concurrent;
using System.Net.Security;

namespace IceRpc.Transports.Coloc.Internal;

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

        if ((serverAddress.Transport is string transport && transport != Name) ||
            !ColocTransport.CheckParams(serverAddress))
        {
            throw new ArgumentException(
                $"The server address '{serverAddress}' contains parameters that are not valid for the Coloc server transport.",
                nameof(serverAddress));
        }

        var listener = new ColocListener(
            serverAddress with { Transport = Name },
            onDispose: OnDispose,
            colocTransportOptions: _options,
            duplexConnectionOptions: options);

        if (!_listeners.TryAdd(listener.ServerAddress, listener))
        {
            // The listener was never published; release its resources before reporting the collision.
            listener.Dispose();
            throw new IceRpcException(IceRpcError.AddressInUse);
        }
        return listener;

        void OnDispose(ColocListener disposedListener) =>
            _listeners.TryRemove(new KeyValuePair<ServerAddress, ColocListener>(
                disposedListener.ServerAddress,
                disposedListener));
    }

    internal ColocServerTransport(
        ConcurrentDictionary<ServerAddress, ColocListener> listeners,
        ColocTransportOptions options)
    {
        _listeners = listeners;
        _options = options;
    }
}
