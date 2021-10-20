// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Threading.Channels;
using NUnit.Framework;

namespace IceRpc.Tests
{
    public sealed class CustomSimpleListener : SimpleListener
    {
        private readonly ChannelReader<ReadOnlyMemory<byte>> _reader;
        private readonly ChannelWriter<ReadOnlyMemory<byte>> _writer;
        private TaskCompletionSource? _waitTaskCompletionSource;

        public override Endpoint Endpoint { get; }

        public override async Task<SimpleNetworkConnection> AcceptAsync()
        {
            if (_waitTaskCompletionSource != null)
            {
                await _waitTaskCompletionSource.Task;
            }
            SimpleNetworkConnection networkConnection = await Task.FromResult<SimpleNetworkConnection>(
                new CustomSimpleNetworkConnection(_reader, _writer, Endpoint));
            _waitTaskCompletionSource = new TaskCompletionSource();
            return networkConnection;
        }

        public override void Dispose() => _waitTaskCompletionSource?.SetResult();

        internal CustomSimpleListener(
            ChannelReader<ReadOnlyMemory<byte>> reader,
            ChannelWriter<ReadOnlyMemory<byte>> writer,
            Endpoint endpoint)
        {
            Endpoint = endpoint;
            _reader = reader;
            _writer = writer;
        }
    }

    public sealed class CustomSimpleNetworkConnection : SimpleNetworkConnection
    {
        private readonly Endpoint _endpoint;
        private readonly ChannelReader<ReadOnlyMemory<byte>> _reader;
        private readonly ChannelWriter<ReadOnlyMemory<byte>> _writer;

        public override bool IsSecure => true;

        public override TimeSpan LastActivity => TimeSpan.MaxValue;

        public override Task<(ISimpleStream, NetworkConnectionInformation)> ConnectAsync(CancellationToken cancel) =>
            Task.FromResult<(ISimpleStream, NetworkConnectionInformation)>(
                (new CustomSimpleStream(_reader, _writer),
                 new NetworkConnectionInformation(_endpoint, _endpoint, TimeSpan.MaxValue, null)));

        public override void Close(Exception? exception = null) => _writer.TryComplete();

        public override bool HasCompatibleParams(Endpoint remoteEndpoint) => true;

        internal CustomSimpleNetworkConnection(
            ChannelReader<ReadOnlyMemory<byte>> reader,
            ChannelWriter<ReadOnlyMemory<byte>> writer,
            Endpoint endpoint)
        {
            _reader = reader;
            _writer = writer;
            _endpoint = endpoint;
        }
    }

    public sealed class CustomSimpleServerTransport : SimpleServerTransport
    {
        private readonly ChannelReader<ReadOnlyMemory<byte>> _reader;
        private readonly ChannelWriter<ReadOnlyMemory<byte>> _writer;

        protected override SimpleListener Listen(Endpoint endpoint) =>
            new CustomSimpleListener(_reader, _writer, endpoint);

        internal CustomSimpleServerTransport(
            ChannelReader<ReadOnlyMemory<byte>> reader,
            ChannelWriter<ReadOnlyMemory<byte>> writer) :
            base(new SlicOptions(), idleTimeout: TimeSpan.MaxValue)
        {
            _reader = reader;
            _writer = writer;
        }
    }

    public sealed class CustomSimpleClientTransport : SimpleClientTransport
    {
        private readonly ChannelReader<ReadOnlyMemory<byte>> _reader;
        private readonly ChannelWriter<ReadOnlyMemory<byte>> _writer;

        protected override SimpleNetworkConnection CreateConnection(Endpoint remoteEndpoint) =>
            new CustomSimpleNetworkConnection(_reader, _writer, remoteEndpoint);

        internal CustomSimpleClientTransport(
            ChannelReader<ReadOnlyMemory<byte>> reader,
            ChannelWriter<ReadOnlyMemory<byte>> writer) :
            base(new SlicOptions(), idleTimeout: TimeSpan.MaxValue)
        {
            _reader = reader;
            _writer = writer;
        }
    }

    class CustomSimpleStream : ISimpleStream
    {
        private readonly ChannelReader<ReadOnlyMemory<byte>> _reader;
        private readonly ChannelWriter<ReadOnlyMemory<byte>> _writer;
        private ReadOnlyMemory<byte> _readBuffer;

        public int DatagramMaxReceiveSize => throw new NotImplementedException();

        public bool IsDatagram => false;

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (_readBuffer.IsEmpty)
            {
                try
                {
                    _readBuffer = await _reader.ReadAsync(cancel).ConfigureAwait(false);
                }
                catch
                {
                    throw new ConnectionLostException();
                }
            }

            if (_readBuffer.Length <= buffer.Length)
            {
                _readBuffer.CopyTo(buffer);
                int read = _readBuffer.Length;
                _readBuffer = ReadOnlyMemory<byte>.Empty;
                return read;
            }
            else
            {
                _readBuffer[..buffer.Length].CopyTo(buffer);
                _readBuffer = _readBuffer[buffer.Length..];
                return buffer.Length;
            }
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
            foreach (ReadOnlyMemory<byte> buffer in buffers.ToArray())
            {
                try
                {
                    await _writer.WriteAsync(buffer, cancel).ConfigureAwait(false);
                }
                catch
                {
                    throw new ConnectionLostException();
                }
            }
        }

        internal CustomSimpleStream(
            ChannelReader<ReadOnlyMemory<byte>> reader,
            ChannelWriter<ReadOnlyMemory<byte>> writer)
        {
            _reader = reader;
            _writer = writer;
        }
    }

    public class CustomSimpleClientTransportTests
    {
        [Timeout(5000)]
        [TestCase("ice+custom://foo")]
        [TestCase("custom -h foo")]
        public async Task CustomSimpleClientTransport_IcePingAsync(string endpoint)
        {
            var clientChannel = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(
                new UnboundedChannelOptions()
                {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });

            var serverChannel = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(
                new UnboundedChannelOptions()
                {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });

            await using var server = new Server
            {
                ServerTransport = new CustomSimpleServerTransport(
                    clientChannel.Reader,
                    serverChannel.Writer).UseLoggerFactory(LogAttributeLoggerFactory.Instance),
                Endpoint = endpoint,
                Dispatcher = new InlineDispatcher(
                    (request, cancel) => new(OutgoingResponse.ForPayload(
                        request,
                        ReadOnlyMemory<ReadOnlyMemory<byte>>.Empty)))
            };

            server.Listen();

            await using var connection = new Connection
            {
                ClientTransport = new CustomSimpleClientTransport(
                    serverChannel.Reader,
                    clientChannel.Writer).UseLoggerFactory(LogAttributeLoggerFactory.Instance),
                RemoteEndpoint = server.Endpoint
            };

            var prx = ServicePrx.FromConnection(connection);
            await prx.IcePingAsync();
        }

    }
}
