// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>A class to create a <see cref="IListener{T}"/> to accept incoming multiplexed connections.</summary>
public interface IMultiplexedServerTransport
{
    /// <summary>Gets the transport's name.</summary>
    string Name { get; }

    /// <summary>Starts listening on a server address.</summary>
    /// <param name="serverAddress">The server address of the listener.</param>
    /// <param name="options">The multiplexed connection options.</param>
    /// <param name="serverAuthenticationOptions">The SSL server authentication options.</param>
    /// <returns>The new listener.</returns>
    IListener<IMultiplexedConnection> Listen(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions);
}
