// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System.Text;

namespace IceRpc.Transports.Internal;

internal delegate T LogTransportConnectionDecoratorFactory<T>(
    T decoratee,
    Endpoint endpoint,
    bool isServer,
    ILogger logger) where T : ITransportConnection;

internal abstract class LogTransportConnectionDecorator : ITransportConnection
{
    internal ILogger Logger { get; }

    private protected bool IsServer { get; }

    private protected TransportConnectionInformation? Information { get; set; }

    private readonly ITransportConnection _decoratee;

    private readonly Endpoint _endpoint;

    public virtual async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel)
    {
        using IDisposable scope = Logger.StartNewConnectionScope(_endpoint, IsServer);

        try
        {
            Information = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogTransportConnectionConnectFailed(ex);
            throw;
        }

        Logger.LogTransportConnectionConnect(Information.Value.LocalNetworkAddress, Information.Value.RemoteNetworkAddress);
        return Information.Value;
    }

    public override string? ToString() => _decoratee.ToString();

    internal LogTransportConnectionDecorator(
        ITransportConnection decoratee,
        Endpoint endpoint,
        bool isServer,
        ILogger logger)
    {
        _decoratee = decoratee;
        _endpoint = endpoint;
        IsServer = isServer;
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
