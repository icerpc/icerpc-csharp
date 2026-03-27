// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Tcp;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>A class to create outgoing duplex connections.</summary>
public interface IDuplexClientTransport
{
    /// <summary>Gets the default duplex client transport.</summary>
    /// <value>The default duplex client transport instance is the <see cref="TcpClientTransport" />.</value>
    public static IDuplexClientTransport Default { get; } = new TcpClientTransport();

    /// <summary>Gets the default transport name.</summary>
    /// <value>The transport accepts transport addresses that use this name as the
    /// <see cref="TransportAddress.TransportName"/>. Some transports may accept additional names beyond this default.
    /// </value>
    string DefaultName { get; }

    /// <summary>Gets a value indicating whether this transport requires SSL.</summary>
    /// <value><see langword="true" /> if this transport requires SSL; otherwise, <see langword="false" />.</value>
    bool IsSslRequired(string? transportName);

    /// <summary>Creates a new transport connection to the specified transport address.</summary>
    /// <param name="transportAddress">The transport address to connect to.</param>
    /// <param name="options">The duplex connection options.</param>
    /// <param name="clientAuthenticationOptions">The SSL client authentication options.</param>
    /// <returns>The new transport connection. This connection is not yet connected.</returns>
    /// <remarks>The IceRPC core can call this method concurrently so it must be thread-safe.</remarks>
    IDuplexConnection CreateConnection(
        TransportAddress transportAddress,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions);
}
