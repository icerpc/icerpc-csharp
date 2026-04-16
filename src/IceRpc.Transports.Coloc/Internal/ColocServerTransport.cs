// Copyright (c) ZeroC, Inc.

using System.Collections.Concurrent;
using System.Net.Security;

namespace IceRpc.Transports.Coloc.Internal;

/// <summary>Implements <see cref="IDuplexServerTransport" /> for the coloc transport.</summary>
internal class ColocServerTransport : IDuplexServerTransport
{
    /// <inheritdoc/>
    public string DefaultName => ColocTransport.Name;

    private readonly ConcurrentDictionary<(string Host, ushort Port), ColocListener> _listeners;
    private readonly ColocTransportOptions _options;

    /// <inheritdoc/>
    public IListener<IDuplexConnection> Listen(
        TransportAddress transportAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        if (serverAuthenticationOptions is not null)
        {
            throw new NotSupportedException("The Coloc server transport does not support SSL.");
        }

        if (transportAddress.TransportName is string name && name != DefaultName)
        {
            throw new NotSupportedException($"The Coloc server transport does not support transport '{name}'.");
        }

        if (transportAddress.Params.Count > 0)
        {
            throw new ArgumentException(
                "The transport address contains parameters that are not valid for the Coloc server transport.",
                nameof(transportAddress));
        }

        var key = (transportAddress.Host, transportAddress.Port);
        ColocListener? listener = null;
        listener = new ColocListener(
            transportAddress,
            onDispose: () => _listeners.TryRemove(
                new KeyValuePair<(string, ushort), ColocListener>(key, listener!)),
            colocTransportOptions: _options,
            duplexConnectionOptions: options);

        if (!_listeners.TryAdd(key, listener))
        {
            throw new IceRpcException(IceRpcError.AddressInUse);
        }
        return listener;
    }

    internal ColocServerTransport(
        ConcurrentDictionary<(string Host, ushort Port), ColocListener> listeners,
        ColocTransportOptions options)
    {
        _listeners = listeners;
        _options = options;
    }
}
