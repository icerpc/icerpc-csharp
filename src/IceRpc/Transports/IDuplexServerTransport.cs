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

    /// <summary>Gets a value indicating whether this transport requires SSL.</summary>
    /// <value><see langword="true" /> if this transport requires SSL; otherwise, <see langword="false" />. Defaults to
    /// <see langword="false" />.</value>
    bool IsSslRequired(string? transportName);

    /// <summary>Gets the transport names accepted by this transport.</summary>
    /// <value>A set of transport names. The first name is the primary name used as the default when no transport is
    /// specified in the server address.</value>
    string Name { get; }

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
