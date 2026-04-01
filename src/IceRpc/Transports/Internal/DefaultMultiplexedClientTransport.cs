// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Quic;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using System.Net.Security;
using System.Runtime.Versioning;

namespace IceRpc.Transports.Internal;

/// <summary>Implements <see cref="IMultiplexedClientTransport" /> using two multiplexed transports: QUIC and
/// Slic over TCP.</summary>
internal class DefaultMultiplexedClientTransport : IMultiplexedClientTransport
{
    /// <inheritdoc/>
    public string DefaultName => _quicTransport.DefaultName;

    /// <inheritdoc/>
    public bool IsSslRequired(string? transportName) => Resolve(transportName).IsSslRequired(transportName);

    internal static DefaultMultiplexedClientTransport Instance
    {
        get
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows())
            {
                return _instance;
            }
            throw new PlatformNotSupportedException(
                "The default multiplexed client transport, QUIC, is only available on Linux, macOS, and Windows.");
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("windows")]
    private static readonly DefaultMultiplexedClientTransport _instance = new();

    private readonly IMultiplexedClientTransport _quicTransport;
    private readonly IMultiplexedClientTransport _tcpTransport = new SlicClientTransport(new TcpClientTransport());

    /// <inheritdoc/>
    public IMultiplexedConnection CreateConnection(
        TransportAddress transportAddress,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions) =>
        Resolve(transportAddress.TransportName).CreateConnection(
            transportAddress,
            options,
            clientAuthenticationOptions);

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("windows")]
    private DefaultMultiplexedClientTransport() => _quicTransport = new QuicClientTransport();

    private IMultiplexedClientTransport Resolve(string? transportName) =>
        transportName switch
        {
            null => _quicTransport,
            "quic" => _quicTransport,
            "tcp" => _tcpTransport,
            _ => throw new NotSupportedException(
                $"The default multiplexed client transport does not support transport '{transportName}'.")
        };
}
