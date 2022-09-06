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
    public ImmutableList<ServerAddress> ExcludedServerAddresses { get; set; }

    /// <summary>Constructs a server address feature that uses the server addresses of a service address.</summary>
    /// <param name="serviceAddress">The service address to copy the server addresses from.</param>
    public ServerAddressFeature(ServiceAddress serviceAddress)
        : this(serviceAddress, ImmutableList<ServerAddress>.Empty)
    {
    }

    /// <summary>Constructs a server address feature that uses the server addresses of a service address.</summary>
    /// <param name="serviceAddress">The service address to copy the server addresses from.</param>
    /// <param name="excludedAddresses">The list of excluded addresses.</param>
    public ServerAddressFeature(ServiceAddress serviceAddress, IList<ServerAddress> excludedAddresses)
    {
        ServerAddress = serviceAddress.ServerAddress;
        AltServerAddresses = serviceAddress.AltServerAddresses.RemoveAll(
            serverAddress => excludedAddresses.Contains(serverAddress));

        if (serviceAddress.ServerAddress is ServerAddress serverAddress && excludedAddresses.Contains(serverAddress))
        {
            ServerAddress = AltServerAddresses.Count > 0 ? AltServerAddresses.First() : null;
            AltServerAddresses = AltServerAddresses.Skip(1).ToImmutableList();
        }

        ExcludedServerAddresses = excludedAddresses.ToImmutableList();
    }
}
