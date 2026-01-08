// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Quic;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;

namespace IceRpc.Transports;

/// <summary>A class to create a <see cref="IListener{T}" /> to accept incoming multiplexed connections.</summary>
public interface IMultiplexedServerTransport
{
    /// <summary>Gets the default multiplexed server transport.</summary>
    /// <value>The default multiplexed server transport is the <see cref="QuicServerTransport" />.</value>
    public static IMultiplexedServerTransport Default
    {
        get
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsWindows())
            {
                if (QuicConnection.IsSupported)
                {
                    return _quicServerTransport;
                }
                throw new NotSupportedException(
                    "The default QUIC server transport is not available on this system. Please review the Platform Dependencies for QUIC in the .NET documentation.");
            }
            throw new PlatformNotSupportedException(
                "The default QUIC server transport is not supported on this platform.");
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("windows")]
    private static readonly QuicServerTransport _quicServerTransport = new();

    /// <summary>Gets the transport's name.</summary>
    /// <value>The transport name.</value>
    string Name { get; }

    /// <summary>Starts listening on a server address.</summary>
    /// <param name="serverAddress">The server address of the listener.</param>
    /// <param name="options">The multiplexed connection options.</param>
    /// <param name="serverAuthenticationOptions">The SSL server authentication options.</param>
    /// <returns>The new listener.</returns>
    /// <remarks>The IceRPC core can call this method concurrently so it must be thread-safe.</remarks>
    IListener<IMultiplexedConnection> Listen(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions);
}
