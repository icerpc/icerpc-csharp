// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;

namespace IceRpc.Features;

/// <summary>Represents a feature used to customize the encoding and decoding of request and response payloads.
/// </summary>
public interface ISliceFeature
{
    /// <summary>Gets the base proxy used when decoding proxies.</summary>
    /// <value>The base proxy, or <see langword="null" /> when this feature does not configure a base proxy. A base
    /// proxy is never a relative proxy.</value>
    /// <remarks>A decoded proxy inherits the invoker and encode options of the base proxy. When the proxy was encoded
    /// as a path (the encoding of a relative proxy), the service address of the decoded proxy is the service address
    /// of the base proxy with this path. When this property is <see langword="null" />, the base proxy for a proxy
    /// decoded from an incoming response is the proxy that sent the request, while a proxy decoded from an incoming
    /// request has no base proxy: it receives <see cref="InvalidInvoker.Instance" /> as its invoker, and it is a
    /// relative proxy when it was encoded as a path.</remarks>
    ISliceProxy? BaseProxy { get; }

    /// <summary>Gets the options to use when encoding the payload of an outgoing response.</summary>
    /// <value>The Slice encode options. <see langword="null" /> is equivalent to
    /// <see cref="SliceEncodeOptions.Default" />.</value>
    SliceEncodeOptions? EncodeOptions { get; }

    /// <summary>Gets the maximum collection allocation when decoding a payload, in bytes.</summary>
    /// <value>The maximum collection allocation.</value>
    /// <remarks>This value is a cumulative budget for the decoding of one Slice payload segment, not a limit on the
    /// size of each collection: the decoder charges the estimated memory size of each string, sequence, and
    /// dictionary against this budget before decoding it, based on the size or element count found in the encoded
    /// data, and throws <see cref="InvalidDataException" /> when the charge exceeds the remaining budget.
    /// Implementations must return a value greater than or equal to <c>0</c>.</remarks>
    int MaxCollectionAllocation { get; }

    /// <summary>Gets the maximum size of a Slice payload segment, in bytes. A Slice payload segment corresponds to the
    /// encoded arguments of an operation, the encoded return values of an operation, or a portion of a stream of
    /// variable-size elements.</summary>
    /// <value>The maximum size of a Slice payload segment, in bytes.</value>
    /// <remarks>This limit applies only when decoding the payload of an incoming request or response: the decoding
    /// throws <see cref="InvalidDataException" /> when the size encoded in the size prefix of a segment exceeds this
    /// maximum, before decoding any byte of the segment. It does not restrict the size of the segments encoded by the
    /// application. The segment size does not include the size of its size prefix. Implementations must return a value
    /// greater than <c>0</c>.</remarks>
    int MaxSegmentSize { get; }
}
