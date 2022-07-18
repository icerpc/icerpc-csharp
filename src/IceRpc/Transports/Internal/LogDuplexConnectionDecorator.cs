// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal;

internal class LogDuplexConnectionDecorator : LogTransportConnectionDecorator, IDuplexConnection
{
    private readonly IDuplexConnection _decoratee;

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
        Logger.LogDuplexConnectionRead(received, ToHexString(buffer[0..received]));
        return received;
    }

    public async Task ShutdownAsync(CancellationToken cancel)
    {
        await _decoratee.ShutdownAsync(cancel).ConfigureAwait(false);
        Logger.LogDuplexConnectionShutdown();
    }

    public async ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
    {
        await _decoratee.WriteAsync(buffers, cancel).ConfigureAwait(false);
        int size = 0;
        foreach (ReadOnlyMemory<byte> buffer in buffers)
        {
            size += buffer.Length;
        }
        Logger.LogDuplexConnectionWrite(size, ToHexString(buffers));
    }

    internal static IDuplexConnection Decorate(
        IDuplexConnection decoratee,
        Endpoint endpoint,
        bool isServer,
        ILogger logger) =>
        new LogDuplexConnectionDecorator(decoratee, endpoint, isServer, logger);

    internal LogDuplexConnectionDecorator(
        IDuplexConnection decoratee,
        Endpoint endpoint,
        bool isServer,
        ILogger logger)
        : base(decoratee, endpoint, isServer, logger) => _decoratee = decoratee;
}
