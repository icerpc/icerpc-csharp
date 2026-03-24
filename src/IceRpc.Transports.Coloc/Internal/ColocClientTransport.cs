// Copyright (c) ZeroC, Inc.

using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Security;

namespace IceRpc.Transports.Coloc.Internal;

/// <summary>Implements <see cref="IDuplexClientTransport" /> for the coloc transport.</summary>
internal class ColocClientTransport : IDuplexClientTransport
{
    /// <inheritdoc/>
    public string Name => ColocTransport.Name;

    private readonly ConcurrentDictionary<(string Host, ushort Port), ColocListener> _listeners;
    private readonly ColocTransportOptions _options;

    /// <inheritdoc/>
    public IDuplexConnection CreateConnection(
        TransportAddress transportAddress,
        DuplexConnectionOptions duplexConnectionOptions,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        if (clientAuthenticationOptions is not null)
        {
            throw new NotSupportedException("The Coloc client transport does not support SSL.");
        }

        if (transportAddress.Name is string name && name != Name)
        {
            throw new NotSupportedException($"The Coloc client transport does not support transport '{name}'.");
        }

        if (transportAddress.Params.Count > 0)
        {
            throw new ArgumentException(
                "The transport address contains parameters that are not valid for the Coloc client transport.",
                nameof(transportAddress));
        }

        var localPipe = new Pipe(new PipeOptions(
            pool: duplexConnectionOptions.Pool,
            minimumSegmentSize: duplexConnectionOptions.MinSegmentSize,
            pauseWriterThreshold: _options.PauseWriterThreshold,
            resumeWriterThreshold: _options.ResumeWriterThreshold,
            useSynchronizationContext: false));
        return new ClientColocConnection(transportAddress, localPipe, ConnectAsync);

        // The client connection connect operation calls this method to queue a connection establishment request with
        // the listener. The returned task is completed once the listener accepts the connection establishment request.
        Task<PipeReader> ConnectAsync(PipeReader clientPipeReader, CancellationToken cancellationToken)
        {
            if (_listeners.TryGetValue((transportAddress.Host, transportAddress.Port), out ColocListener? listener) &&
                listener.TryQueueConnect(
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
        ConcurrentDictionary<(string Host, ushort Port), ColocListener> listeners,
        ColocTransportOptions options)
    {
        _listeners = listeners;
        _options = options;
    }
}
