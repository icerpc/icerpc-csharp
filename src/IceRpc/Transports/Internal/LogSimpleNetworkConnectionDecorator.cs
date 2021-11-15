// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    internal class LogSimpleNetworkConnectionDecorator : LogNetworkConnectionDecorator, ISimpleNetworkConnection
    {
        private protected override INetworkConnection Decoratee => _decoratee;

        private readonly ISimpleNetworkConnection _decoratee;

        public virtual async Task<(ISimpleStream, NetworkConnectionInformation)> ConnectAsync(CancellationToken cancel)
        {
            ISimpleStream simpleStream;
            try
            {
                (simpleStream, Information) = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogConnectFailed(ex);
                throw;
            }

            Logger.LogConnect(Information.Value.LocalEndpoint, Information.Value.RemoteEndpoint);
            return (new LogSimpleStreamDecorator(simpleStream, Logger), Information.Value);
        }

        internal static ISimpleNetworkConnection Decorate(
            ISimpleNetworkConnection decoratee,
            bool isServer,
            ILogger logger) =>
            new LogSimpleNetworkConnectionDecorator(decoratee, isServer, logger);

        internal LogSimpleNetworkConnectionDecorator(
            ISimpleNetworkConnection decoratee,
            bool isServer,
            ILogger logger)
            : base(isServer, logger) => _decoratee = decoratee;
    }

    internal sealed class LogSimpleStreamDecorator : ISimpleStream
    {
        private readonly ISimpleStream _decoratee;
        private readonly ILogger _logger;

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = await _decoratee.ReadAsync(buffer, cancel).ConfigureAwait(false);
            _logger.LogSimpleStreamRead(received, LogNetworkConnectionDecorator.ToHexString(buffer[0..received]));
            return received;
        }

        public override string? ToString() => _decoratee.ToString();

        public async ValueTask WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
            await _decoratee.WriteAsync(buffers, cancel).ConfigureAwait(false);
            _logger.LogSimpleStreamWrite(buffers.GetByteCount(), LogNetworkConnectionDecorator.ToHexString(buffers));
        }

        internal LogSimpleStreamDecorator(ISimpleStream decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
