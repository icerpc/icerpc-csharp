// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Features;

/// <summary>The default implementation of <see cref="IServerAddressFeature"/>.</summary>
public sealed class ServerAddressFeature : IServerAddressFeature
{
    /// <inheritdoc/>
    public ImmutableList<ServerAddress> AltServerAddresses { get; set; }

    /// <inheritdoc/>
    public ServerAddress? ServerAddress { get; set; }

    /// <inheritdoc/>
    public ImmutableHashSet<ServerAddress> RemovedServerAddresses { get; set; }

    /// <summary>Constructs a server address feature that uses the server addresses of a service address.</summary>
    /// <param name="serviceAddress">The service address to copy the server addresses from.</param>
    public ServerAddressFeature(ServiceAddress serviceAddress)
    {
        ServerAddress = serviceAddress.ServerAddress;
        AltServerAddresses = serviceAddress.AltServerAddresses;
        RemovedServerAddresses = ImmutableHashSet<ServerAddress>.Empty;
    }
}
