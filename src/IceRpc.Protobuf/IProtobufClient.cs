// Copyright (c) ZeroC, Inc.

namespace IceRpc.Protobuf;

/// <summary>Represents a local ambassador for a remote service.</summary>
public interface IProtobufClient
{
    /// <summary>Gets the encode options, used to customize the encoding of payloads created from this client.
    /// </summary>
    ProtobufEncodeOptions? EncodeOptions { get; }

    /// <summary>Gets the invocation pipeline of this client.</summary>
    IInvoker Invoker { get; }

    /// <summary>Gets the address of the remote service.</summary>
    ServiceAddress ServiceAddress { get; }
}

/// <summary>Lets generic code construct a <typeparamref name="TSelf" /> client, since an interface cannot declare a
/// constructor.</summary>
/// <typeparam name="TSelf">The client struct that implements this interface.</typeparam>
public interface IProtobufClient<TSelf> : IProtobufClient where TSelf : struct, IProtobufClient<TSelf>
{
    /// <summary>Creates a client from an invoker, a service address and encode options.</summary>
    /// <param name="invoker">The invocation pipeline of the client.</param>
    /// <param name="serviceAddress">The service address. <see langword="null" /> is equivalent to the default service
    /// address of the client type.</param>
    /// <param name="encodeOptions">The encode options, used to customize the encoding of request payloads.</param>
    /// <returns>The new client.</returns>
    static abstract TSelf Create(
        IInvoker invoker,
        ServiceAddress? serviceAddress,
        ProtobufEncodeOptions? encodeOptions);
}
