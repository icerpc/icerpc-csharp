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
                "The default multiplexed client transport, QUIC, is only available on Linux, macOS, and Windows.");
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

    /// <summary>Determines whether this transport requires SSL for the specified transport name.</summary>
    /// <param name="transportName">The transport name, or <see langword="null" /> which is equivalent to
    /// <see cref="DefaultName" />.</param>
    /// <returns><see langword="true" /> if SSL is required; otherwise, <see langword="false" />.</returns>
    /// <exception cref="NotSupportedException">Thrown if <paramref name="transportName" /> is not supported by this
    /// transport.</exception>
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
