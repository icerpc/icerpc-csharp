// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>A feature to customize the encoding and decoding of request and response payloads.</summary>
public interface ISliceFeature
{
    /// <summary>Gets the activator to use when decoding Slice1-encoded classes and exceptions.</summary>
    /// <value>The activator. When null, the decoding of a request or response payload uses the activator injected by
    /// the Slice generated code.</value>
    IActivator? Activator { get; }

    /// <summary>Gets the options to use when encoding the payload of an outgoing response.</summary>
    /// <value>The Slice encode options. Null is equivalent to <see cref="SliceEncodeOptions.Default" />.</value>
    SliceEncodeOptions? EncodeOptions { get; }

    /// <summary>Gets the maximum collection allocation when decoding a payload, in bytes.</summary>
    int MaxCollectionAllocation { get; }

    /// <summary>Gets the maximum depth when decoding a class recursively.</summary>
    int MaxDepth { get; }

    /// <summary>Gets the maximum size of a Slice payload segment, in bytes. A Slice payload segment corresponds to the
    /// encoded arguments of an operation, the encoded return values of an operation, or a portion of a stream of
    /// variable-size elements.</summary>
    int MaxSegmentSize { get; }

    /// <summary>Gets the proxy factory to use when decoding proxies in request or response payloads.</summary>
    Func<ServiceAddress, IProxy?, IProxy>? ProxyFactory { get; }
}
