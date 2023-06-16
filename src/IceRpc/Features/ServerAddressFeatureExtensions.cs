// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>Provides extension methods for <see cref="IServerAddressFeature" />.</summary>
public static class ServerAddressFeatureExtensions
{
    /// <summary>Tries to remove a server address from this server address feature. The server address is added to the
    /// removed server addresses of the feature.</summary>
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
        feature.RemovedServerAddresses = feature.RemovedServerAddresses.Add(serverAddress);
    }

    /// <summary>Rotates the server addresses the first alt server address becomes the main server address and the main
    /// server address becomes the last alt server address.</summary>
    /// <param name="feature">The server address feature.</param>
    public static void RotateAddresses(this IServerAddressFeature feature)
    {
        if (feature.ServerAddress is not null && feature.AltServerAddresses.Count > 0)
        {
            feature.AltServerAddresses = feature.AltServerAddresses.Add(feature.ServerAddress.Value);
            feature.ServerAddress = feature.AltServerAddresses[0];
            feature.AltServerAddresses = feature.AltServerAddresses.RemoveAt(0);
        }
    }
}
