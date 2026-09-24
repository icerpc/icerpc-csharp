// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace IceRpc.Tests.Common;

/// <summary>Provides extension methods for <see cref="Protocol" /> to create addresses in tests parameterized by
/// protocol.</summary>
public static class ProtocolExtensions
{
    /// <summary>Creates a server address with this protocol.</summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="host">The host.</param>
    /// <returns>A new server address.</returns>
    public static ServerAddress CreateServerAddress(this Protocol protocol, string host = "::0") =>
        protocol == Protocol.Ice ?
            new ServerAddress.Ice { Host = host } :
            new ServerAddress.IceRpc { Host = host };

    /// <summary>Creates a service address with this protocol.</summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="path">The path.</param>
    /// <returns>A new service address.</returns>
    public static ServiceAddress CreateServiceAddress(this Protocol protocol, string path = "/") =>
        protocol == Protocol.Ice ?
            new ServiceAddress.Ice { Path = path } :
            new ServiceAddress.IceRpc { Path = path };
}

/// <summary>Provides extension methods for <see cref="ServerAddress" /> and <see cref="ServiceAddress" /> to create
/// addresses in tests parameterized by protocol.</summary>
public static class AddressExtensions
{
    /// <summary>Creates a service address with this server address as its main server address.</summary>
    /// <param name="serverAddress">The main server address.</param>
    /// <param name="path">The path.</param>
    /// <param name="altServerAddresses">The secondary server addresses.</param>
    /// <returns>A new service address with the protocol of <paramref name="serverAddress" />.</returns>
    public static ServiceAddress CreateServiceAddress(
        this ServerAddress serverAddress,
        string path = "/",
        ImmutableList<ServerAddress>? altServerAddresses = null)
    {
        ImmutableList<ServerAddress> alt = altServerAddresses ?? [];
        return serverAddress.Protocol == Protocol.Ice ?
            new ServiceAddress.Ice { Path = path, ServerAddress = serverAddress, AltServerAddresses = alt } :
            new ServiceAddress.IceRpc { Path = path, ServerAddress = serverAddress, AltServerAddresses = alt };
    }

    /// <summary>Returns a copy of this service address with a new path.</summary>
    /// <param name="serviceAddress">The service address.</param>
    /// <param name="path">The new path.</param>
    /// <returns>The new service address.</returns>
    public static ServiceAddress WithPath(this ServiceAddress serviceAddress, string path) =>
        serviceAddress switch
        {
            ServiceAddress.Ice ice => ice with { Path = path },
            ServiceAddress.IceRpc icerpc => icerpc with { Path = path },
        };
}
