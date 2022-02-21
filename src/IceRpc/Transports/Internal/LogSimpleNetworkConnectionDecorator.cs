// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    internal class LogSimpleNetworkConnectionDecorator : LogNetworkConnectionDecorator, ISimpleNetworkConnection
    {
        private readonly ISimpleNetworkConnection _decoratee;

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = await _decoratee.ReadAsync(buffer, cancel).ConfigureAwait(false);
            Logger.LogSimpleNetworkConnectionRead(received, ToHexString(buffer[0..received]));
            return received;
        }

        public async ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
            await _decoratee.WriteAsync(buffers, cancel).ConfigureAwait(false);
            int size = 0;
            foreach (ReadOnlyMemory<byte> buffer in buffers)
            {
                size += buffer.Length;
            }
            Logger.LogSimpleNetworkConnectionWrite(size, ToHexString(buffers));
        }

        internal static ISimpleNetworkConnection Decorate(
            ISimpleNetworkConnection decoratee,
            Endpoint endpoint,
            bool isServer,
            ILogger logger) =>
            new LogSimpleNetworkConnectionDecorator(decoratee, endpoint, isServer, logger);

        internal LogSimpleNetworkConnectionDecorator(
            ISimpleNetworkConnection decoratee,
            Endpoint endpoint,
            bool isServer,
            ILogger logger)
            : base(decoratee, endpoint, isServer, logger) => _decoratee = decoratee;
    }
}
