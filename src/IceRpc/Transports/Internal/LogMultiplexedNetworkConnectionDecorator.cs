// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc.Transports.Internal
{
    internal class LogMultiplexedNetworkConnectionDecorator :
        LogNetworkConnectionDecorator,
        IMultiplexedNetworkConnection
    {
        private readonly IMultiplexedNetworkConnection _decoratee;

        public async ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel) =>
            new LogMultiplexedStreamDecorator(
                await _decoratee.AcceptStreamAsync(cancel).ConfigureAwait(false),
                Logger);

        public IMultiplexedStream CreateStream(bool bidirectional) =>
            new LogMultiplexedStreamDecorator(_decoratee.CreateStream(bidirectional), Logger);

        internal static IMultiplexedNetworkConnection Decorate(
            IMultiplexedNetworkConnection decoratee,
            bool isServer,
            ILogger logger) =>
            new LogMultiplexedNetworkConnectionDecorator(decoratee, isServer, logger);

        private LogMultiplexedNetworkConnectionDecorator(
            IMultiplexedNetworkConnection decoratee,
            bool isServer,
            ILogger logger)
            : base(decoratee, isServer, logger) =>  _decoratee = decoratee;
    }

    internal sealed class LogMultiplexedStreamDecorator : IMultiplexedStream
    {
        public long Id => _decoratee.Id;
        public bool IsBidirectional => _decoratee.IsBidirectional;
        public bool IsStarted => _decoratee.IsStarted;

        public Action? ShutdownAction
        {
            get => _decoratee.ShutdownAction;
            set => _decoratee.ShutdownAction = value;
        }

        private readonly IMultiplexedStream _decoratee;
        private readonly ILogger _logger;

        public ReadOnlyMemory<byte> TransportHeader => _decoratee.TransportHeader;

        public void AbortRead(byte errorCode) => _decoratee.AbortRead(errorCode);

        public void AbortWrite(byte errorCode) => _decoratee.AbortWrite(errorCode);

        public Stream AsByteStream() => _decoratee.AsByteStream();

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            Debug.Assert(IsStarted);
            using IDisposable _ = _logger.StartMultiplexedStreamScope(Id);
            int received = await _decoratee.ReadAsync(buffer, cancel).ConfigureAwait(false);
            _logger.LogMultiplexedStreamRead(
                received,
                LogNetworkConnectionDecorator.ToHexString(buffer[0..received]));
            return received;
        }

        public async ValueTask WriteAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            using IDisposable? scope = IsStarted ? _logger.StartMultiplexedStreamScope(Id) : null;
            await _decoratee.WriteAsync(buffers, endStream, cancel).ConfigureAwait(false);

            // If the scope is null, we start it now:
            using IDisposable? _ = scope == null ? _logger.StartMultiplexedStreamScope(Id) : null;

            _logger.LogMultiplexedStreamWrite(
                buffers.GetByteCount(),
                LogNetworkConnectionDecorator.ToHexString(buffers));
        }

        public Task WaitForShutdownAsync(CancellationToken cancel) => _decoratee.WaitForShutdownAsync(cancel);

        public override string? ToString() => _decoratee.ToString();

        internal LogMultiplexedStreamDecorator(IMultiplexedStream decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
