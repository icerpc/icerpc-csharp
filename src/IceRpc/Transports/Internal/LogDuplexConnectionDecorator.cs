// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Text;

namespace IceRpc.Transports.Internal;

internal sealed class LogDuplexConnectionDecorator : IDuplexConnection
{
    public Endpoint Endpoint => _decoratee.Endpoint;

    private readonly IDuplexConnection _decoratee;
    private readonly ILogger _logger;

    // We don't log anything as it would be redundant with the ProtocolConnection logging.
    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel) =>
        _decoratee.ConnectAsync(cancel);

    // We don't log anything as it would be redundant with the ProtocolConnection logging.
    public void Dispose() => _decoratee.Dispose();

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
    {
        return _logger.IsEnabled(LogLevel.Trace) ? PerformReadAsync() : _decoratee.ReadAsync(buffer, cancel);

        async ValueTask<int> PerformReadAsync()
        {
            int received = await _decoratee.ReadAsync(buffer, cancel).ConfigureAwait(false);
            _logger.LogDuplexConnectionRead(received, ToHexString(buffer[0..received]));
            return received;
        }
    }

    public async Task ShutdownAsync(CancellationToken cancel)
    {
        // TODO: do we always get the scope from the LogProtocolConnectionDecorator?
        try
        {
            await _decoratee.ShutdownAsync(cancel).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDuplexConnectionShutdownException(exception);
            throw;
        }
        _logger.LogDuplexConnectionShutdown();
    }

    public override string? ToString() => _decoratee.ToString();

    public ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
    {
        return _logger.IsEnabled(LogLevel.Trace) ? PerformWriteAsync() : _decoratee.WriteAsync(buffers, cancel);

        async ValueTask PerformWriteAsync()
        {
            await _decoratee.WriteAsync(buffers, cancel).ConfigureAwait(false);
            int size = 0;
            foreach (ReadOnlyMemory<byte> buffer in buffers)
            {
                size += buffer.Length;
            }
            _logger.LogDuplexConnectionWrite(size, ToHexString(buffers));
        }
    }

    internal LogDuplexConnectionDecorator(IDuplexConnection decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }

    private static string ToHexString(Memory<byte> buffer)
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

    private static string ToHexString(IReadOnlyList<ReadOnlyMemory<byte>> buffers)
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
