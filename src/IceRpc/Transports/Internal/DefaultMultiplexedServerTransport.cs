// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Quic;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

/// <summary>Implements <see cref="IMultiplexedServerTransport" /> using two multiplexed transports: QUIC and
/// Slic over TCP.</summary>
internal class DefaultMultiplexedServerTransport : IMultiplexedServerTransport
{
    /// <inheritdoc/>
    public string DefaultName => _quicTransport.DefaultName;

    private readonly IMultiplexedServerTransport _quicTransport;
    private readonly IMultiplexedServerTransport _tcpTransport = new SlicServerTransport(new TcpServerTransport());

    /// <inheritdoc/>
    public IListener<IMultiplexedConnection> Listen(
        TransportAddress transportAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions) =>
        Resolve(transportAddress.TransportName).Listen(transportAddress, options, serverAuthenticationOptions);

    internal DefaultMultiplexedServerTransport()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows())
        {
            _quicTransport = new QuicServerTransport();
        }
        else
        {
            throw new PlatformNotSupportedException(
                "The default multiplexed server transport, QUIC, is only available on Linux, macOS, and Windows.");
        }
    }

    private IMultiplexedServerTransport Resolve(string? transportName) =>
        transportName switch
        {
            null => _quicTransport,
            "quic" => _quicTransport,
            "tcp" => _tcpTransport,
            _ => throw new NotSupportedException(
                $"The default multiplexed server transport does not support transport '{transportName}'.")
        };
}
