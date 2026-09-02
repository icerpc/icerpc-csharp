// Copyright (c) ZeroC, Inc.

using IceRpc.Ice;
using IceRpc.Ice.Codec;

namespace IceRpc.Features;

/// <summary>Represents a feature used to customize the encoding and decoding of request and response payloads.
/// </summary>
public interface IIceFeature
{
    /// <summary>Gets the activator to use when decoding Ice-encoded classes and exceptions.</summary>
    /// <value>The activator. When null, the decoding of a request or response payload uses the activator injected by
    /// the generated code.</value>
    IActivator? Activator { get; }

    /// <summary>Gets the base proxy used when decoding a service address into a proxy.</summary>
    /// <value>The base proxy. A decoded proxy inherits the invoker and encode options of the base proxy. When
    /// <see langword="null" />, a proxy decoded from an incoming request receives
    /// <see cref="InvalidInvoker.Instance" /> as its invoker, and a proxy decoded from an incoming response inherits
    /// the invoker and encode options of the proxy that sent the request.</value>
    IIceProxy? BaseProxy { get; }

    /// <summary>Gets the options to use when encoding the payload of an outgoing response.</summary>
    /// <value>The Ice encode options. <see langword="null" /> is equivalent to <see cref="IceEncodeOptions.Default"
    /// />.</value>
    IceEncodeOptions? EncodeOptions { get; }

    /// <summary>Gets the maximum collection allocation when decoding a payload, in bytes.</summary>
    /// <value>The maximum collection allocation.</value>
    /// <remarks>This value is a cumulative budget for the decoding of one payload, not a limit on the size of each
    /// collection: the decoder adds the estimated memory size of every string, sequence, and dictionary it decodes,
    /// and throws <see cref="InvalidDataException" /> once this total exceeds the maximum collection allocation.
    /// Implementations must return a value greater than or equal to <c>0</c>.</remarks>
    int MaxCollectionAllocation { get; }

    /// <summary>Gets the maximum depth when decoding a class recursively.</summary>
    /// <value>The maximum depth.</value>
    /// <remarks>Implementations must return a value greater than <c>0</c>.</remarks>
    int MaxDepth { get; }

    /// <summary>Gets the maximum size of an Ice-encoded payload, in bytes. An Ice-encoded payload corresponds to the
    /// encoded arguments of an operation, the encoded return values of an operation, or the encoded Ice exception
    /// carried by a response with status code <see cref="StatusCode.ApplicationError" />.</summary>
    /// <value>The maximum size of an Ice-encoded payload, in bytes.</value>
    /// <remarks>This limit applies only when decoding the payload of an incoming request or response: the payload is
    /// read in full into memory before it is decoded, and this read throws <see cref="InvalidDataException" /> when
    /// the payload exceeds this size. It does not restrict the size of the payloads encoded by the application. The
    /// payload size does not include the size of any header for this payload, such as the encapsulation header with
    /// the ice protocol. Implementations must return a value greater than or equal to <c>0</c> and less than
    /// <see cref="int.MaxValue" />.</remarks>
    int MaxPayloadSize { get; }
}
