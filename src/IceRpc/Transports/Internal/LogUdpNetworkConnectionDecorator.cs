// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>The log decorator installed by the UDP transports.</summary>
    internal class LogUdpNetworkConnectionDecorator : ISimpleNetworkConnection
    {
        PipeReader IDuplexPipe.Input => _decoratee.Input;
        PipeWriter IDuplexPipe.Output => _decoratee.Output;
        bool INetworkConnection.IsSecure => _decoratee.IsSecure;
        TimeSpan INetworkConnection.LastActivity => _decoratee.LastActivity;

        private readonly ILogger _logger;
        private readonly UdpNetworkConnection _decoratee;

        ValueTask IAsyncDisposable.DisposeAsync() => _decoratee.DisposeAsync();

        public async Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel)
        {
            NetworkConnectionInformation result = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);

            _logger.LogUdpConnect(_decoratee.Socket.ReceiveBufferSize, _decoratee.Socket.SendBufferSize);

            return result;
        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            _decoratee.HasCompatibleParams(remoteEndpoint);

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel) =>
            _decoratee.ReadAsync(buffer, cancel);

        public override string? ToString() => _decoratee.ToString();

        public ValueTask WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel) =>
            _decoratee.WriteAsync(buffers, cancel);

        internal LogUdpNetworkConnectionDecorator(UdpNetworkConnection decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
