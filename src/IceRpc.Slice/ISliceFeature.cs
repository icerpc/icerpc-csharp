// Copyright (c) ZeroC, Inc.

using ZeroC.Slice;

namespace IceRpc.Slice;

/// <summary>Represents a feature used to customize the encoding and decoding of request and response payloads.
/// </summary>
public interface ISliceFeature
{
    /// <summary>Gets the activator to use when decoding Slice1-encoded classes and exceptions.</summary>
    /// <value>The activator. When null, the decoding of a request or response payload uses the activator injected by
    /// the Slice generated code.</value>
    IActivator? Activator { get; }

    /// <summary>Gets the options to use when encoding the payload of an outgoing response.</summary>
    /// <value>The Slice encode options. <see langword="null" /> is equivalent to <see cref="SliceEncodeOptions.Default"
    /// />.</value>
    SliceEncodeOptions? EncodeOptions { get; }

    /// <summary>Gets the maximum collection allocation when decoding a payload, in bytes.</summary>
    /// <value>The maximum collection allocation.</value>
    int MaxCollectionAllocation { get; }

    /// <summary>Gets the maximum depth when decoding a class recursively.</summary>
    /// <value>The maximum depth.</value>
    int MaxDepth { get; }

    /// <summary>Gets the maximum size of a Slice payload segment, in bytes. A Slice payload segment corresponds to the
    /// encoded arguments of an operation, the encoded return values of an operation, or a portion of a stream of
    /// variable-size elements.</summary>
    /// <value>The maximum size of a Slice payload segment, in bytes.</value>
    int MaxSegmentSize { get; }

    /// <summary>Gets the proxy factory to customize the decoding of proxies in request or response payloads.</summary>
    /// <value>The proxy factory used when decoding proxies.</value>
    Func<ServiceAddress, GenericProxy>? ProxyFactory { get; }
}
