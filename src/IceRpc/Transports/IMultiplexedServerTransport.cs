// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>A class to create a <see cref="IListener{T}" /> to accept incoming multiplexed connections.</summary>
public interface IMultiplexedServerTransport
{
    /// <summary>Gets the default multiplexed server transport.</summary>
    /// <value>The default multiplexed server transport is the <see cref="SlicServerTransport" />.</value>
    public static IMultiplexedServerTransport Default { get; } = new SlicServerTransport(new TcpServerTransport());

    /// <summary>Gets the transport's name.</summary>
    /// <value>The transport name.</value>
    string Name { get; }

    /// <summary>Starts listening on a server address.</summary>
    /// <param name="serverAddress">The server address of the listener.</param>
    /// <param name="options">The multiplexed connection options.</param>
    /// <param name="serverAuthenticationOptions">The SSL server authentication options.</param>
    /// <returns>The new listener.</returns>
    /// <remarks>The IceRPC core can call this method concurrently so it must be thread-safe.</remarks>
    IListener<IMultiplexedConnection> Listen(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions);
}
