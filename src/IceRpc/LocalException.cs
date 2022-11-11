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

/// <summary>This exception reports an error in the ice or icerpc protocol, such as an attempt to send a request
/// or response with a header size greater than the remote peer's max header size.</summary>
public class ProtocolException : Exception
{
    /// <summary>Constructs a new instance of the <see cref="ProtocolException" /> class with a specified error
    /// message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public ProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>Constructs a new instance of the <see cref="ProtocolException" /> class with a specified error
    /// message and a reference to the inner exception that is the cause of this exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
