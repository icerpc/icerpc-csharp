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

        serverAddress = serverAddress with { Transport = Name };

        var localPipe = new Pipe(new PipeOptions(pool: options.Pool, minimumSegmentSize: options.MinSegmentSize));
        return new ColocConnection(serverAddress, localPipe.Writer, ConnectAsync);

        Task<PipeReader> ConnectAsync()
        {
            if (_listeners.TryGetValue(serverAddress, out ColocListener? listener))
            {
                return listener.QueueReaderAsync(localPipe.Reader);
            }
            else
            {
                throw new TransportException(TransportErrorCode.ConnectionRefused);
            }
        }
    }

    internal ColocClientTransport(ConcurrentDictionary<ServerAddress, ColocListener> listeners) =>
        _listeners = listeners;
}
