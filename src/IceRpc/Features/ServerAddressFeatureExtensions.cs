// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features;

/// <summary>Extension methods for interface <see cref="IServerAddressFeature"/>.</summary>
public static class ServerAddressFeatureExtensions
{
    /// <summary>Tries to remove a server address from this server address feature. If the server address is not found, this method
    /// does nothing.</summary>
    /// <param name="feature">The server address feature.</param>
    /// <param name="serverAddress">The server address to remove from the server address feature.</param>
    public static void RemoveServerAddress(this IServerAddressFeature feature, ServerAddress serverAddress)
    {
        // Filter-out the serverAddress
        if (feature.ServerAddress == serverAddress)
        {
            feature.ServerAddress = null;
        }
        feature.AltServerAddresses = feature.AltServerAddresses.RemoveAll(e => e == serverAddress);

        if (feature.ServerAddress is null && feature.AltServerAddresses.Count > 0)
        {
            feature.ServerAddress = feature.AltServerAddresses[0];
            feature.AltServerAddresses = feature.AltServerAddresses.RemoveAt(0);
        }
        feature.ExcludedServerAddresses = feature.ExcludedServerAddresses.Add(serverAddress);
    }
}
