// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace IceRpc.Transports.Internal
{
    /// <summary>The IListener implementation for the colocated transport.</summary>
    internal class ColocListener : IListener
    {
        public Endpoint Endpoint { get; }

        /// <summary>A dictionary that keeps track of all coloc listeners.</summary>
        private static readonly IDictionary<Endpoint, ColocListener> _colocListenerDictionary =
            new ConcurrentDictionary<Endpoint, ColocListener>();

        private readonly Channel<(ChannelWriter<ReadOnlyMemory<byte>>, ChannelReader<ReadOnlyMemory<byte>>)> _channel;
        private readonly ILogger _logger;
        private readonly MultiStreamOptions _options;

        public async ValueTask<INetworkConnection> AcceptAsync()
        {
            (ChannelWriter<ReadOnlyMemory<byte>> writer, ChannelReader<ReadOnlyMemory<byte>> reader) =
                await _channel.Reader.ReadAsync().ConfigureAwait(false);

            return LogNetworkConnectionDecorator.Create(
                new ColocConnection(Endpoint, isServer: true, _options, writer, reader, _logger),
                _logger);
        }

        public void Dispose()
        {
            _channel.Writer.Complete();
            _colocListenerDictionary.Remove(Endpoint);
        }

        public override string ToString() => $"{base.ToString()} {Endpoint}";

        internal static bool TryGetValue(
            Endpoint endpoint,
            [NotNullWhen(returnValue: true)] out ColocListener? listener) =>
            _colocListenerDictionary.TryGetValue(endpoint, out listener);

        internal ColocListener(Endpoint endpoint, MultiStreamOptions options, ILogger logger)
        {
            if (endpoint.Params.Count > 0)
            {
                throw new FormatException($"unknown parameter '{endpoint.Params[0].Name}' in endpoint '{endpoint}'");
            }

            Endpoint = endpoint;
            _logger = logger;
            _options = options;

            // There's always a single reader (the listener) but there might be several writers calling Write
            // concurrently if there are connection establishment attempts from multiple threads. Not allowing
            // synchronous continuations is safer as otherwise disposal of the listener could end up running
            // the continuation of AcceptAsync.
            _channel = Channel.CreateUnbounded<(ChannelWriter<ReadOnlyMemory<byte>>,
                                                ChannelReader<ReadOnlyMemory<byte>>)>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });

            if (!_colocListenerDictionary.TryAdd(Endpoint, this))
            {
                throw new TransportException($"endpoint '{endpoint}' is already in use");
            }
        }

        internal (ChannelReader<ReadOnlyMemory<byte>>, ChannelWriter<ReadOnlyMemory<byte>>) NewClientConnection()
        {
            var reader = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });

            var writer = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });

            if (!_channel.Writer.TryWrite((writer.Writer, reader.Reader)))
            {
                throw new ConnectionRefusedException();
            }

            return (writer.Reader, reader.Writer);
        }
    }
}
