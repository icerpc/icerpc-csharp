// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice;

/// <summary>Represents a local ambassador for a remote service.</summary>
public interface IIceProxy
{
    /// <summary>Gets the encode options, used to customize the encoding of payloads created from this proxy.
    /// </summary>
    IceEncodeOptions? EncodeOptions { get; }

    /// <summary>Gets the invocation pipeline of this proxy.</summary>
    IInvoker Invoker { get; }

    /// <summary>Gets the address of the remote service.</summary>
    ServiceAddress ServiceAddress { get; }
}

/// <summary>Lets generic code construct a <typeparamref name="TSelf" /> proxy, since an interface cannot declare a
/// constructor.</summary>
/// <typeparam name="TSelf">The proxy struct that implements this interface.</typeparam>
public interface IIceProxy<TSelf> : IIceProxy where TSelf : struct, IIceProxy<TSelf>
{
    /// <summary>Creates a proxy from an invoker, a service address and encode options.</summary>
    /// <param name="invoker">The invocation pipeline of the proxy.</param>
    /// <param name="serviceAddress">The service address. <see langword="null" /> is equivalent to the default service
    /// address of the proxy type.</param>
    /// <param name="encodeOptions">The encode options, used to customize the encoding of request payloads.</param>
    /// <returns>The new proxy.</returns>
    static abstract TSelf Create(IInvoker invoker, ServiceAddress? serviceAddress, IceEncodeOptions? encodeOptions);
}
