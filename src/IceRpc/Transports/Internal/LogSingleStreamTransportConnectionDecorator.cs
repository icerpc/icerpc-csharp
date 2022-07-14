// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal;

internal class LogSingleStreamTransportConnectionDecorator : LogTransportConnectionDecorator, ISingleStreamTransportConnection
{
    private readonly ISingleStreamTransportConnection _decoratee;

    public void Dispose()
    {
        _decoratee.Dispose();

        if (Information is TransportConnectionInformation connectionInformation)
        {
            using IDisposable scope = Logger.StartConnectionScope(connectionInformation, IsServer);
            Logger.LogTransportConnectionDispose();
        }
        // We don't emit a log when closing a connection that was not connected.
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
    {
        int received = await _decoratee.ReadAsync(buffer, cancel).ConfigureAwait(false);
        Logger.LogSingleStreamTransportConnectionRead(received, ToHexString(buffer[0..received]));
        return received;
    }

    public async Task ShutdownAsync(CancellationToken cancel)
    {
        await _decoratee.ShutdownAsync(cancel).ConfigureAwait(false);
        Logger.LogSingleStreamTransportConnectionShutdown();
    }

    public async ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
    {
        await _decoratee.WriteAsync(buffers, cancel).ConfigureAwait(false);
        int size = 0;
        foreach (ReadOnlyMemory<byte> buffer in buffers)
        {
            size += buffer.Length;
        }
        Logger.LogSingleStreamTransportConnectionWrite(size, ToHexString(buffers));
    }

    internal static ISingleStreamTransportConnection Decorate(
        ISingleStreamTransportConnection decoratee,
        Endpoint endpoint,
        bool isServer,
        ILogger logger) =>
        new LogSingleStreamTransportConnectionDecorator(decoratee, endpoint, isServer, logger);

    internal LogSingleStreamTransportConnectionDecorator(
        ISingleStreamTransportConnection decoratee,
        Endpoint endpoint,
        bool isServer,
        ILogger logger)
        : base(decoratee, endpoint, isServer, logger) => _decoratee = decoratee;
}
