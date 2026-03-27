// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Tcp;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>A class to create a <see cref="IListener{T}" /> to accept incoming duplex connections.</summary>
public interface IDuplexServerTransport
{
    /// <summary>Gets the default duplex server transport.</summary>
    /// <value>The default duplex server transport is the <see cref="TcpServerTransport" />.</value>
    public static IDuplexServerTransport Default { get; } = new TcpServerTransport();

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
    /// <param name="options">The duplex connection options.</param>
    /// <param name="serverAuthenticationOptions">The SSL server authentication options.</param>
    /// <returns>The new listener.</returns>
    /// <remarks>The IceRPC core can call this method concurrently so it must be thread-safe.</remarks>
    IListener<IDuplexConnection> Listen(
        TransportAddress transportAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions);
}
