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

    private readonly ConcurrentDictionary<ServerAddress, ColocListener> _listeners;

    /// <inheritdoc/>
    public bool CheckParams(ServerAddress serverAddress) => ColocTransport.CheckParams(serverAddress);

    /// <inheritdoc/>
    public IDuplexConnection CreateConnection(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        if (clientAuthenticationOptions is not null)
        {
            throw new NotSupportedException("cannot create a secure Coloc connection");
        }

        if (!CheckParams(serverAddress))
        {
            throw new FormatException($"cannot create a Coloc connection to server address '{serverAddress}'");
        }

        return new ColocConnection(
            serverAddress with { Transport = Name },
            serverAddress => Connect(serverAddress, options));
    }

    internal ColocClientTransport(ConcurrentDictionary<ServerAddress, ColocListener> listeners) =>
        _listeners = listeners;

    private (PipeReader, PipeWriter) Connect(ServerAddress serverAddress, DuplexConnectionOptions options)
    {
        if (_listeners.TryGetValue(serverAddress, out ColocListener? listener))
        {
            return listener.NewClientConnection(options);
        }
        else
        {
            throw new TransportException(TransportErrorCode.ConnectionRefused);
        }
    }
}
