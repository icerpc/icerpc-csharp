// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace IceRpc.Transports.Internal
{
    /// <summary>The listener implementation for the colocated transport.</summary>
    internal class ColocListener : IListener<ISimpleNetworkConnection>
    {
        public Endpoint Endpoint { get; }

        /// <summary>A dictionary that keeps track of all coloc listeners.</summary>
        private static readonly IDictionary<Endpoint, ColocListener> _colocListenerDictionary =
            new ConcurrentDictionary<Endpoint, ColocListener>();

        private readonly Channel<(ChannelWriter<ReadOnlyMemory<byte>>, ChannelReader<ReadOnlyMemory<byte>>)> _channel;

        public async Task<ISimpleNetworkConnection> AcceptAsync()
        {
            (ChannelWriter<ReadOnlyMemory<byte>> writer, ChannelReader<ReadOnlyMemory<byte>> reader) =
                await _channel.Reader.ReadAsync().ConfigureAwait(false);
            return new ColocNetworkConnection(Endpoint, isServer: true, writer, reader);
        }

        public override string ToString() => $"{base.ToString()} {Endpoint}";

        internal static bool TryGetValue(
            Endpoint endpoint,
            [NotNullWhen(returnValue: true)] out ColocListener? listener) =>
            _colocListenerDictionary.TryGetValue(endpoint, out listener);

        public ValueTask DisposeAsync()
        {
            _channel.Writer.Complete();
            _colocListenerDictionary.Remove(Endpoint);
            return new();
        }

        internal ColocListener(Endpoint endpoint)
        {
            if (endpoint.Params.Count > 0)
            {
                throw new FormatException($"unknown parameter '{endpoint.Params[0].Name}' in endpoint '{endpoint}'");
            }

            Endpoint = endpoint;

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
            // We use a capacity of 100 buffers for the channels. This is mostly useful for the Ice1 protocol
            // which doesn't provide any flow control or limits the number of invocations on the client side.
            // If the Ice1 server can't dispatch more invocations, the colloc transport will eventually
            // prevent the client to send further requests once the channel is full.

            var reader = Channel.CreateBounded<ReadOnlyMemory<byte>>(
                new BoundedChannelOptions(capacity: 100)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });

            var writer = Channel.CreateBounded<ReadOnlyMemory<byte>>(
                new BoundedChannelOptions(capacity: 100)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
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
