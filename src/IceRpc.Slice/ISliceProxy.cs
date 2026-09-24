// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>Represents a local ambassador for a remote service.</summary>
public interface ISliceProxy
{
    /// <summary>Gets the encode options, used to customize the encoding of payloads created from this proxy.
    /// </summary>
    SliceEncodeOptions? EncodeOptions { get; }

    /// <summary>Gets the invocation pipeline of this proxy.</summary>
    IInvoker Invoker { get; }

    /// <summary>Gets a value indicating whether this proxy is a relative proxy.</summary>
    /// <value><see langword="true" /> when this proxy is a relative proxy; otherwise, <see langword="false" />.</value>
    bool IsRelative { get; }

    /// <summary>Gets the address of the remote service.</summary>
    /// <value>The service address. For a relative proxy, only the path of this service address is meaningful.</value>
    ServiceAddress ServiceAddress { get; }
}

/// <summary>Lets generic code construct a <typeparamref name="TSelf" /> proxy, since an interface cannot declare a
/// constructor.</summary>
/// <typeparam name="TSelf">The proxy struct that implements this interface.</typeparam>
public interface ISliceProxy<TSelf> : ISliceProxy where TSelf : struct, ISliceProxy<TSelf>
{
    /// <summary>Creates a proxy from an invoker, a service address and encode options.</summary>
    /// <param name="invoker">The invocation pipeline of the proxy.</param>
    /// <param name="serviceAddress">The service address. <see langword="null" /> is equivalent to the default service
    /// address of the proxy type.</param>
    /// <param name="encodeOptions">The encode options, used to customize the encoding of request payloads.</param>
    /// <returns>The new proxy.</returns>
    static abstract TSelf Create(IInvoker invoker, ServiceAddress? serviceAddress, SliceEncodeOptions? encodeOptions);

    /// <summary>Creates a relative proxy from a path.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The new relative proxy.</returns>
    static abstract TSelf FromPath(string path);
}
