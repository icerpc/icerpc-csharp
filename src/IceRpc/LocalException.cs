// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>This exception reports that a service address has no server address or no usable server address.</summary>
public class NoServerAddressException : Exception
{
    /// <summary>Constructs a new instance of the <see cref="NoServerAddressException" /> class.</summary>
    public NoServerAddressException()
    {
    }

    /// <summary>Constructs a new instance of the <see cref="NoServerAddressException" /> class.</summary>
    /// <param name="serviceAddress">The service address with no server address or no usable server address.</param>
    public NoServerAddressException(ServiceAddress serviceAddress)
        : base($"service address '{serviceAddress}' has no usable serverAddress")
    {
    }
}
