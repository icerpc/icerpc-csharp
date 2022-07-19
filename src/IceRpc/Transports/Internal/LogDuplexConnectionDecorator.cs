// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System.Text;

namespace IceRpc.Transports.Internal;

internal sealed class LogDuplexConnectionDecorator : IDuplexConnection
{
    public Endpoint Endpoint => _decoratee.Endpoint;

    internal ILogger Logger { get; }

    private readonly IDuplexConnection _decoratee;
    private readonly Endpoint _endpoint;
    private TransportConnectionInformation? _information;
    private readonly bool _isServer;

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel)
    {
        using IDisposable scope = Logger.StartNewConnectionScope(_endpoint, _isServer);

        try
        {
            _information = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogTransportConnectionConnectFailed(ex);
            throw;
        }

        Logger.LogTransportConnectionConnect(
            _information.Value.LocalNetworkAddress,
            _information.Value.RemoteNetworkAddress);
        return _information.Value;
    }

    public void Dispose()
    {
        _decoratee.Dispose();

        if (_information is TransportConnectionInformation connectionInformation)
        {
            using IDisposable scope = Logger.StartConnectionScope(connectionInformation, _isServer);
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

    public override string? ToString() => _decoratee.ToString();

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

    internal LogDuplexConnectionDecorator(
        IDuplexConnection decoratee,
        Endpoint endpoint,
        bool isServer,
        ILogger logger)
    {
        _decoratee = decoratee;
        _endpoint = endpoint;
        _isServer = isServer;
        Logger = logger;
    }

    internal static string ToHexString(Memory<byte> buffer)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Math.Min(buffer.Length, 64); ++i)
        {
            _ = sb.Append("0x").Append(Convert.ToHexString(buffer.Span[i..(i + 1)])).Append(' ');
        }
        if (buffer.Length > 64)
        {
            _ = sb.Append("...");
        }
        return sb.ToString().Trim();
    }

    internal static string ToHexString(IReadOnlyList<ReadOnlyMemory<byte>> buffers)
    {
        int size = 0;
        var sb = new StringBuilder();
        foreach (ReadOnlyMemory<byte> buffer in buffers)
        {
            if (size < 64)
            {
                for (int j = 0; j < Math.Min(buffer.Length, 64 - size); ++j)
                {
                    _ = sb.Append("0x").Append(Convert.ToHexString(buffer.Span[j..(j + 1)])).Append(' ');
                }
            }
            size += buffer.Length;
            if (size > 64)
            {
                _ = sb.Append("...");
                break;
            }
        }
        return sb.ToString().Trim();
    }
}
