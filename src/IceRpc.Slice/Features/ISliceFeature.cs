// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;

namespace IceRpc.Features;

/// <summary>Represents a feature used to customize the encoding and decoding of request and response payloads.
/// </summary>
public interface ISliceFeature
{
    /// <summary>Gets the base proxy used when decoding a service address into a proxy.</summary>
    /// <value>The base proxy. A decoded proxy inherits the invoker and encode options of the base proxy, and a decoded
    /// relative service address is resolved against the service address of the base proxy. When
    /// <see langword="null" />, a proxy decoded from an incoming request receives
    /// <see cref="InvalidInvoker.Instance" /> as its invoker and keeps its relative service address as is, and a proxy
    /// decoded from an incoming response inherits the invoker and encode options of the proxy that sent the request.
    /// </value>
    ISliceProxy? BaseProxy { get; }

    /// <summary>Gets the options to use when encoding the payload of an outgoing response.</summary>
    /// <value>The Slice encode options. <see langword="null" /> is equivalent to
    /// <see cref="SliceEncodeOptions.Default" />.</value>
    SliceEncodeOptions? EncodeOptions { get; }

    /// <summary>Gets the maximum collection allocation when decoding a payload, in bytes.</summary>
    /// <value>The maximum collection allocation.</value>
    /// <remarks>This value is a cumulative budget for the decoding of one Slice payload segment, not a limit on the
    /// size of each collection: the decoder adds the estimated memory size of every string, sequence, and dictionary
    /// it decodes, and throws <see cref="InvalidDataException" /> once this total exceeds the maximum collection
    /// allocation. Implementations must return a value greater than or equal to <c>0</c>.</remarks>
    int MaxCollectionAllocation { get; }

    /// <summary>Gets the maximum size of a Slice payload segment, in bytes. A Slice payload segment corresponds to the
    /// encoded arguments of an operation, the encoded return values of an operation, or a portion of a stream of
    /// variable-size elements.</summary>
    /// <value>The maximum size of a Slice payload segment, in bytes.</value>
    /// <remarks>This limit applies only when decoding the payload of an incoming request or response: each segment is
    /// read in full into memory before it is decoded, and this read throws <see cref="InvalidDataException" /> when
    /// the segment exceeds this size. It does not restrict the size of the segments encoded by the application. The
    /// segment size does not include the size of its size prefix. Implementations must return a value greater than
    /// <c>0</c>.</remarks>
    int MaxSegmentSize { get; }
}
