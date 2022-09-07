// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Features;

/// <summary>Extension methods for interface <see cref="IServerAddressFeature"/>.</summary>
public static class ServerAddressFeatureExtensions
{
    /// <summary>Tries to remove a server address from this server address feature. The
    /// <paramref name="serverAddress"/> is added to the excluded server addresses of the feature.</summary>
    /// <param name="feature">The server address feature.</param>
    /// <param name="serverAddress">The server address to remove from the server address feature.</param>
    public static void Remove(this IServerAddressFeature feature, ServerAddress serverAddress) =>
        feature.RemoveAll(ImmutableList.Create(serverAddress));

    /// <summary>Tries to remove all server addresses in <paramref name="serverAddresses"/> from this server address
    /// feature. The <paramref name="serverAddresses"/> are added to the excluded server addresses of the feature.
    /// </summary>
    /// <param name="feature">The server address feature.</param>
    /// <param name="serverAddresses">The server addresses to remove from the server address feature.</param>
    public static void RemoveAll(this IServerAddressFeature feature, IEnumerable<ServerAddress> serverAddresses)
    {
        // Filter-out the serverAddress
        if (feature.ServerAddress is ServerAddress serverAddress && serverAddresses.Contains(serverAddress))
        {
            feature.ServerAddress = null;
        }
        feature.AltServerAddresses = feature.AltServerAddresses.RemoveAll(e => serverAddresses.Contains(e));

        if (feature.ServerAddress is null && feature.AltServerAddresses.Count > 0)
        {
            feature.ServerAddress = feature.AltServerAddresses[0];
            feature.AltServerAddresses = feature.AltServerAddresses.RemoveAt(0);
        }
        feature.RemovedServerAddresses = feature.RemovedServerAddresses.Union(serverAddresses);
    }
}
