// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Quic;
using System.Net.Security;
using System.Runtime.Versioning;

namespace IceRpc.Transports;

/// <summary>A class to create outgoing multiplexed connections.</summary>
public interface IMultiplexedClientTransport
{
    /// <summary>Gets the default multiplexed client transport.</summary>
    /// <value>The default multiplexed client transport is the <see cref="QuicClientTransport" />.</value>
    public static IMultiplexedClientTransport Default
    {
        get
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows())
            {
                return _quicClientTransport;
            }
            throw new PlatformNotSupportedException(
                "The default QUIC client transport is not supported on this platform.");
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("windows")]
    private static readonly QuicClientTransport _quicClientTransport = new();

    /// <summary>Gets the transport's name.</summary>
    string Name { get; }

    /// <summary>Creates a new transport connection to the specified server address.</summary>
    /// <param name="serverAddress">The server address of the connection.</param>
    /// <param name="options">The multiplexed connection options.</param>
    /// <param name="clientAuthenticationOptions">The SSL client authentication options.</param>
    /// <returns>The new transport connection. This connection is not yet connected.</returns>
    /// <remarks>The IceRPC core can call this method concurrently so it must be thread-safe.</remarks>
    IMultiplexedConnection CreateConnection(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions);
}
