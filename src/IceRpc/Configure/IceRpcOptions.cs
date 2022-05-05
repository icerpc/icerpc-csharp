// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Collections.Immutable;

namespace IceRpc.Configure;

/// <summary>A property bag used to configure the icerpc protocol.</summary>
public record class IceRpcOptions
{
    /// <summary>Gets or sets the connection fields to send to the peer when establishing a connection.</summary>
    public IDictionary<ConnectionFieldKey, OutgoingFieldValue> Fields { get; set; } =
        ImmutableDictionary<ConnectionFieldKey, OutgoingFieldValue>.Empty;
}

/// <summary>A property bag used to configure a client connection using the icerpc protocol.</summary>
public sealed record class IceRpcClientOptions : IceRpcOptions
{
    /// <summary>Returns the default value for <see cref="ClientTransport"/>.</summary>
    public static IClientTransport<IMultiplexedNetworkConnection> DefaultClientTransport { get; } =
        new CompositeMultiplexedClientTransport().UseSlicOverTcp();

    /// <summary>Gets or sets the <see cref="IClientTransport{IMultiplexedNetworkConnection}"/> used by this
    /// connection to create multiplexed network connections.</summary>
    public IClientTransport<IMultiplexedNetworkConnection> ClientTransport { get; set; } = DefaultClientTransport;
}

/// <summary>A property bag used to configure a server connection using the icerpc protocol.</summary>
public sealed record class IceRpcServerOptions : IceRpcOptions
{
    /// <summary>Returns the default value for <see cref="ServerTransport"/>.</summary>
    public static IServerTransport<IMultiplexedNetworkConnection> DefaultServerTransport { get; } =
        new CompositeMultiplexedServerTransport().UseSlicOverTcp();

    /// <summary>Gets or sets <see cref="IServerTransport{IMultiplexedNetworkConnection}"/> used by the
    /// server to accept multiplexed connections.</summary>
    public IServerTransport<IMultiplexedNetworkConnection> ServerTransport { get; set; } = DefaultServerTransport;
}
