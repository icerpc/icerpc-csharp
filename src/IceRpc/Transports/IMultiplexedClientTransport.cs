// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>A class to create outgoing multiplexed connections.</summary>
public interface IMultiplexedClientTransport
{
    /// <summary>Gets the default multiplexed client transport.</summary>
    public static IMultiplexedClientTransport Default { get; } = new SlicClientTransport(new TcpClientTransport());

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
    /// <param name="options">The multiplexed connection options.</param>
    /// <param name="clientAuthenticationOptions">The SSL client authentication options.</param>
    /// <returns>The new transport connection. This connection is not yet connected.</returns>
    IMultiplexedConnection CreateConnection(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions);
}
