// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Internal;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>A class to create outgoing multiplexed connections.</summary>
public interface IMultiplexedClientTransport
{
    /// <summary>Gets the default multiplexed client transport.</summary>
    /// <value>The default multiplexed client transport.</value>
    public static IMultiplexedClientTransport Default => _defaultClientTransport;

    private static readonly DefaultMultiplexedClientTransport _defaultClientTransport = new();

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
