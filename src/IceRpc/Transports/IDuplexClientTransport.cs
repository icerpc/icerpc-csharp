// Copyright (c) ZeroC, Inc.

using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>A class to create outgoing duplex connections.</summary>
public interface IDuplexClientTransport
{
    /// <summary>Gets the default duplex client transport.</summary>
    public static IDuplexClientTransport Default { get; } = new TcpClientTransport();

    /// <summary>Gets the transport's name.</summary>
    string Name { get; }

    /// <summary>Checks if a server address has valid <see cref="ServerAddress.Params" /> for this client transport.
    /// Only the params are included in this check.</summary>
    /// <param name="serverAddress">The server address to check.</param>
    /// <returns><see langword="true" /> when all params of <paramref name="serverAddress" /> are valid for this
    /// transport; otherwise, <see langword="false" />.</returns>
    bool CheckParams(ServerAddress serverAddress);

    /// <summary>Creates a new transport connection to the specified server address.</summary>
    /// <param name="serverAddress">The server address of the connection.</param>
    /// <param name="options">The duplex connection options.</param>
    /// <param name="clientAuthenticationOptions">The SSL client authentication options.</param>
    /// <returns>The new transport connection. This connection is not yet connected.</returns>
    IDuplexConnection CreateConnection(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions);
}
