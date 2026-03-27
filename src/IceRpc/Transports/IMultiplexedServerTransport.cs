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
                if (QuicListener.IsSupported)
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

    /// <summary>Gets the default transport name.</summary>
    /// <value>The transport accepts transport addresses that use this name as the
    /// <see cref="TransportAddress.TransportName"/>. Some transports may accept additional names beyond this default.
    /// </value>
    string DefaultName { get; }

    /// <summary>Gets a value indicating whether this transport requires SSL.</summary>
    /// <value><see langword="true" /> if this transport requires SSL; otherwise, <see langword="false" />. Defaults to
    /// <see langword="false" />.</value>
    bool IsSslRequired(string? transportName);

    /// <summary>Starts listening on a transport address.</summary>
    /// <param name="transportAddress">The transport address to listen on.</param>
    /// <param name="options">The multiplexed connection options.</param>
    /// <param name="serverAuthenticationOptions">The SSL server authentication options.</param>
    /// <returns>The new listener.</returns>
    /// <remarks>The IceRPC core can call this method concurrently so it must be thread-safe.</remarks>
    IListener<IMultiplexedConnection> Listen(
        TransportAddress transportAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions);
}
