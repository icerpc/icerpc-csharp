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
        return new ClientColocConnection(serverAddress, localPipe, ConnectAsync);

        Task<PipeReader> ConnectAsync(PipeReader clientPipeReader, CancellationToken cancellationToken)
        {
            if (_listeners.TryGetValue(serverAddress, out ColocListener? listener))
            {
                var tcs = new TaskCompletionSource<PipeReader>();
                listener.QueueConnectAsync(
                    serverPipeReader =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            // Client-side Connection establishment was canceled.
                            return null;
                        }
                        else if (serverPipeReader is null)
                        {
                            // Listener is disposed.
                            tcs.SetException(new TransportException(TransportErrorCode.ConnectionRefused));
                            return null;
                        }
                        else
                        {
                            tcs.SetResult(serverPipeReader);
                            return clientPipeReader;
                        }
                    });
                return tcs.Task.WaitAsync(cancellationToken);
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
