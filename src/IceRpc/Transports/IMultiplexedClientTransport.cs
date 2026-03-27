// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Quic;
using System.Net.Quic;
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
                if (QuicConnection.IsSupported)
                {
                    return _quicClientTransport;
                }
                throw new NotSupportedException(
                    "The default QUIC client transport is not available on this system. Please review the Platform Dependencies for QUIC in the .NET documentation.");
            }
            throw new PlatformNotSupportedException(
                "The default QUIC client transport is not supported on this platform.");
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("windows")]
    private static readonly QuicClientTransport _quicClientTransport = new();

    /// <summary>Gets the default transport name.</summary>
    /// <value>The transport accepts transport addresses that use this name as the
    /// <see cref="TransportAddress.TransportName"/>. Some transports may accept additional names beyond this default.
    /// </value>
    string DefaultName { get; }

    /// <summary>Gets a value indicating whether this transport requires SSL.</summary>
    /// <value><see langword="true" /> if this transport requires SSL; otherwise, <see langword="false" />. Defaults to
    /// <see langword="false" />.</value>
    bool IsSslRequired(string? transportName);

    /// <summary>Creates a new transport connection to the specified transport address.</summary>
    /// <param name="transportAddress">The transport address to connect to.</param>
    /// <param name="options">The multiplexed connection options.</param>
    /// <param name="clientAuthenticationOptions">The SSL client authentication options.</param>
    /// <returns>The new transport connection. This connection is not yet connected.</returns>
    /// <remarks>The IceRPC core can call this method concurrently so it must be thread-safe.</remarks>
    IMultiplexedConnection CreateConnection(
        TransportAddress transportAddress,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions);
}
