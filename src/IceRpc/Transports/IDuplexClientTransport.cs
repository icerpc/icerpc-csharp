// Copyright (c) ZeroC, Inc.

using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>A class to create outgoing duplex connections.</summary>
public interface IDuplexClientTransport
{
    /// <summary>Gets the default duplex client transport.</summary>
    /// <value>The default duplex client transport instance is the <see cref="TcpClientTransport" />.</value>
    public static IDuplexClientTransport Default { get; } = new TcpClientTransport();

    /// <summary>Gets the transport's name.</summary>
    /// <value>The transport name.</value>
    string Name { get; }

    /// <summary>Creates a new transport connection to the specified server address.</summary>
    /// <param name="serverAddress">The server address of the connection.</param>
    /// <param name="options">The duplex connection options.</param>
    /// <param name="clientAuthenticationOptions">The SSL client authentication options.</param>
    /// <returns>The new transport connection. This connection is not yet connected.</returns>
    /// <remarks>The IceRPC core can call this method concurrently so it must be thread-safe.</remarks>
    IDuplexConnection CreateConnection(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions);
}
