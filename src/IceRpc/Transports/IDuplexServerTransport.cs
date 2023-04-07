// Copyright (c) ZeroC, Inc.

using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>A class to create a <see cref="IListener{T}" /> to accept incoming duplex connections.</summary>
public interface IDuplexServerTransport
{
    /// <summary>Gets the default duplex server transport.</summary>
    /// <value>The default duplex server transport is the <see cref="TcpServerTransport" />.</value>
    public static IDuplexServerTransport Default { get; } = new TcpServerTransport();

    /// <summary>Gets the transport's name.</summary>
    /// <value>The transport name.</value>
    string Name { get; }

    /// <summary>Starts listening on a server address.</summary>
    /// <param name="serverAddress">The server address of the listener.</param>
    /// <param name="options">The duplex connection options.</param>
    /// <param name="serverAuthenticationOptions">The SSL server authentication options.</param>
    /// <returns>The new listener.</returns>
    /// <remarks>The IceRPC core can call this method concurrently so it must be thread-safe.</remarks>
    IListener<IDuplexConnection> Listen(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions);
}
