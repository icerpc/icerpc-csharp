// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Quic;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using System.Net.Security;
using System.Runtime.Versioning;

namespace IceRpc.Transports.Internal;

/// <summary>Implements <see cref="IMultiplexedServerTransport" /> using two multiplexed transports: QUIC and
/// Slic over TCP.</summary>
internal class DefaultMultiplexedServerTransport : IMultiplexedServerTransport
{
    /// <inheritdoc/>
    public string DefaultName => _quicTransport.DefaultName;

    internal static DefaultMultiplexedServerTransport Instance
    {
        get
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows())
            {
                return _instance;
            }
            throw new PlatformNotSupportedException(
                "The default multiplexed server transport, QUIC, is only available on Linux, macOS, and Windows.");
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("windows")]
    private static readonly DefaultMultiplexedServerTransport _instance = new();

    private readonly IMultiplexedServerTransport _quicTransport;
    private readonly IMultiplexedServerTransport _tcpTransport = new SlicServerTransport(new TcpServerTransport());

    /// <inheritdoc/>
    public IListener<IMultiplexedConnection> Listen(
        TransportAddress transportAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        return Resolve(transportAddress.TransportName).Listen(transportAddress, options, serverAuthenticationOptions);

        IMultiplexedServerTransport Resolve(string? transportName) =>
            transportName switch
            {
                null => _quicTransport,
                "quic" => _quicTransport,
                "tcp" => _tcpTransport,
                _ => throw new NotSupportedException(
                    $"The default multiplexed server transport does not support transport '{transportName}'.")
            };
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("windows")]
    private DefaultMultiplexedServerTransport() => _quicTransport = new QuicServerTransport();
}
