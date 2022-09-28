// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

/// <summary>Implements <see cref="IDuplexServerTransport"/> for the coloc transport.</summary>
internal class ColocServerTransport : IDuplexServerTransport
{
    /// <inheritdoc/>
    public string Name => ColocTransport.Name;

    private readonly ConcurrentDictionary<ServerAddress, ColocListener> _listeners;

    /// <inheritdoc/>
    public IListener<IDuplexConnection> CreateListener(
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

        return new ColocListener(_listeners, serverAddress with { Transport = Name }, options);
    }

    internal ColocServerTransport(ConcurrentDictionary<ServerAddress, ColocListener> listeners) =>
        _listeners = listeners;
}
