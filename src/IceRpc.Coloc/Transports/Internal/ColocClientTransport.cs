// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

/// <summary>Implements <see cref="IDuplexClientTransport" /> for the coloc transport.</summary>
internal class ColocClientTransport : IDuplexClientTransport
{
    /// <inheritdoc/>
    public string Name => ColocTransport.Name;

    private readonly ConcurrentDictionary<ServerAddress, ColocListener> _listeners;
    private readonly ColocTransportOptions _options;

    /// <inheritdoc/>
    public bool CheckParams(ServerAddress serverAddress) => ColocTransport.CheckParams(serverAddress);

    /// <inheritdoc/>
    public IDuplexConnection CreateConnection(
        ServerAddress serverAddress,
        DuplexConnectionOptions duplexConnectionOptions,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        if (clientAuthenticationOptions is not null)
        {
            throw new NotSupportedException("The Coloc client transport does not support SSL.");
        }

        if ((serverAddress.Transport is string transport && transport != ColocTransport.Name) ||
            !CheckParams(serverAddress))
        {
            throw new ArgumentException(
                $"The server address contains parameters that are not valid for the Coloc client transport: '{serverAddress}'.",
                nameof(serverAddress));
        }

        serverAddress = serverAddress with { Transport = Name };

        var localPipe = new Pipe(new PipeOptions(
            pool: duplexConnectionOptions.Pool,
            minimumSegmentSize: duplexConnectionOptions.MinSegmentSize,
            pauseWriterThreshold: _options.PauseWriterThreshold,
            resumeWriterThreshold: _options.ResumeWriterThreshold));
        return new ClientColocConnection(serverAddress, localPipe, ConnectAsync);

        Task<PipeReader> ConnectAsync(PipeReader clientPipeReader, CancellationToken cancellationToken)
        {
            if (_listeners.TryGetValue(serverAddress, out ColocListener? listener) &&
                listener.TryQueueConnectAsync(
                    clientPipeReader,
                    cancellationToken,
                    out Task<PipeReader>? serverPipeReaderTask))
            {
                return serverPipeReaderTask;
            }
            else
            {
                throw new IceRpcException(IceRpcError.ConnectionRefused);
            }
        }
    }

    internal ColocClientTransport(
        ConcurrentDictionary<ServerAddress, ColocListener> listeners,
        ColocTransportOptions options)
    {
        _listeners = listeners;
        _options = options;
    }
}
