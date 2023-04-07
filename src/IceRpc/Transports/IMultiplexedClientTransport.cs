// Copyright (c) ZeroC, Inc.

using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>A class to create outgoing multiplexed connections.</summary>
public interface IMultiplexedClientTransport
{
    /// <summary>Gets the default multiplexed client transport.</summary>
    /// <value>The default multiplexed client transport is the <see cref="SlicClientTransport" />.</value>
    public static IMultiplexedClientTransport Default { get; } = new SlicClientTransport(new TcpClientTransport());

    /// <summary>Gets the transport's name.</summary>
    string Name { get; }

    /// <summary>Creates a new transport connection to the specified server address.</summary>
    /// <param name="serverAddress">The server address of the connection.</param>
    /// <param name="options">The multiplexed connection options.</param>
    /// <param name="clientAuthenticationOptions">The SSL client authentication options.</param>
    /// <returns>The new transport connection. This connection is not yet connected.</returns>
    /// <remarks>The IceRPC core can call this method concurrently so it must be thread-safe.</remarks>
    IMultiplexedConnection CreateConnection(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions);
}
