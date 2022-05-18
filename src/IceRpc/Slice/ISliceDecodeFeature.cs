// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice;

/// <summary>A feature to customize the decoding of request and response payloads.</summary>
public interface ISliceDecodeFeature
{
    /// <summary>Gets the activator to use when decoding Slice classes, exceptions, and traits. When <c>null</c>, the
    /// decoding of a request or response payload uses the activator injected by the Slice generated code.</summary>
    IActivator? Activator { get; }

    /// <summary>Gets the maximum collection allocation when decoding a payload, in bytes.</summary>
    int MaxCollectionAllocation { get; }

    /// <summary>Gets the maximum depth when decoding a type recursively.</summary>
    int MaxDepth { get; }

    /// <summary>Gets the maximum size of a Slice payload segment, in bytes. A Slice payload segment corresponds to the
    /// encoded arguments of an operation, the encoded return values of an operation, or a portion of a stream of
    /// variable-size elements.</summary>
    int MaxSegmentSize { get; }

    /// <summary>Gets the invoker assigned to decoded proxies. When null, a proxy decoded from an incoming request
    /// gets <see cref="Proxy.DefaultInvoker"/> while a proxy decoded from an incoming response gets the invoker of the
    /// proxy that created the request.</summary>
    IInvoker? ProxyInvoker { get; }

    /// <summary>Gets the stream pause writer threshold. When the Slice engine decodes a stream into an async
    /// enumerable, it will pause when the number of bytes decoded but not read is greater or equal to this value.
    /// </summary>
    int StreamPauseWriterThreshold { get; }

    /// <summary>Gets the stream resume writer threshold. When the decoding of a stream into an async enumerable is
    /// paused (<see cref="StreamPauseWriterThreshold"/>), the decoding resumes when the number of bytes decoded but not
    /// read yet falls below this threshold.</summary>
    int StreamResumeWriterThreshold { get; }
}
