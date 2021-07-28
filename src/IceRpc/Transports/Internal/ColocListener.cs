// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ColocChannelReader = System.Threading.Channels.ChannelReader<(long StreamId, object Frame, bool Fin)>;
using ColocChannelWriter = System.Threading.Channels.ChannelWriter<(long StreamId, object Frame, bool Fin)>;

namespace IceRpc.Transports.Internal
{
    /// <summary>The IListener implementation for the colocated transport.</summary>
    internal class ColocListener : IListener
    {
        public Endpoint Endpoint => _endpoint;

        /// <summary>A dictionary that keeps track of all coloc listeners.</summary>
        private static readonly IDictionary<Endpoint, ColocListener> _colocListenerDictionary =
            new ConcurrentDictionary<Endpoint, ColocListener>();

        private readonly Channel<(long, ColocChannelWriter, ColocChannelReader)> _channel;
        private readonly Endpoint _endpoint;
        private readonly ILogger _logger;
        // The next ID to assign to an accepted ColocatedSocket. This ID is used for tracing purpose only.
        private long _nextId;
        private readonly ServerConnectionOptions _options;

        public async ValueTask<MultiStreamConnection> AcceptAsync()
        {
            (long id, ColocChannelWriter writer, ColocChannelReader reader) =
                await _channel.Reader.ReadAsync().ConfigureAwait(false);

            return new ColocConnection(_endpoint, id, writer, reader, _options, _logger);
        }

        public void Dispose()
        {
            _channel.Writer.Complete();
            _colocListenerDictionary.Remove(_endpoint);
        }

        public override string ToString() => $"{base.ToString()} {_endpoint}";

        internal static bool TryGetValue(
            Endpoint endpoint,
            [NotNullWhen(returnValue: true)] out ColocListener? listener) =>
            _colocListenerDictionary.TryGetValue(endpoint, out listener);

        internal ColocListener(Endpoint endpoint, ServerConnectionOptions options, ILogger logger)
        {
            if (endpoint.Params.Count > 0)
            {
                throw new FormatException($"unknown parameter '{endpoint.Params[0].Name}' in endpoint '{endpoint}'");
            }

            _endpoint = endpoint;
            _logger = logger;
            _options = options;

            // There's always a single reader (the listener) but there might be several writers calling Write
            // concurrently if there are connection establishment attempts from multiple threads. Not allowing
            // synchronous continuations is safer as otherwise disposal of the listener could end up running
            // the continuation of AcceptAsync.
            _channel = Channel.CreateUnbounded<(long, ColocChannelWriter, ColocChannelReader)>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });

            if (!_colocListenerDictionary.TryAdd(_endpoint, this))
            {
                throw new TransportException($"endpoint '{endpoint}' is already in use");
            }
        }

        internal (ColocChannelReader, ColocChannelWriter, long) NewClientConnection()
        {
            var reader = Channel.CreateUnbounded<(long, object, bool)>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });

            var writer = Channel.CreateUnbounded<(long, object, bool)>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });

            long id = Interlocked.Increment(ref _nextId);

            if (!_channel.Writer.TryWrite((id, writer.Writer, reader.Reader)))
            {
                throw new ConnectionRefusedException();
            }

            return (writer.Reader, reader.Writer, id);
        }
    }
}
