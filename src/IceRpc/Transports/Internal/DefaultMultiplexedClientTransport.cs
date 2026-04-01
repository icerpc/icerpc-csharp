// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Quic;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

/// <summary>Implements <see cref="IMultiplexedClientTransport" /> using two multiplexed transports: QUIC and
/// Slic over TCP.</summary>
internal class DefaultMultiplexedClientTransport : IMultiplexedClientTransport
{
    /// <inheritdoc/>
    public string DefaultName => _quicTransport.DefaultName;

    /// <inheritdoc/>
    public bool IsSslRequired(string? transportName) => Resolve(transportName).IsSslRequired(transportName);

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

    internal DefaultMultiplexedClientTransport()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows())
        {
            _quicTransport = new QuicClientTransport();
        }
        else
        {
            throw new PlatformNotSupportedException(
                "The default multiplexed client transport, QUIC, is only available on Linux, macOS, and Windows.");
        }
    }

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
