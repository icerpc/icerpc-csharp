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
    /// collection: the decoder charges the estimated memory size of each string, sequence, and dictionary against
    /// this budget before decoding it, based on the size or element count found in the encoded data, and throws
    /// <see cref="InvalidDataException" /> when the charge exceeds the remaining budget. Implementations must return
    /// a value greater than or equal to <c>0</c>.</remarks>
    int MaxCollectionAllocation { get; }

    /// <summary>Gets the maximum depth when decoding a class recursively.</summary>
    /// <value>The maximum depth.</value>
    /// <remarks>Implementations must return a value greater than <c>0</c>.</remarks>
    int MaxDepth { get; }

    /// <summary>Gets the maximum size of an Ice-encoded payload, in bytes. An Ice-encoded payload corresponds to the
    /// encoded arguments of an operation, the encoded return values of an operation, or the encoded Ice exception
    /// carried by a response with status code <see cref="StatusCode.ApplicationError" />.</summary>
    /// <value>The maximum size of an Ice-encoded payload, in bytes.</value>
    /// <remarks><para>This limit applies only when decoding the payload of an incoming request or response: the
    /// decoding throws <see cref="InvalidDataException" /> when the payload size carried by the request or response
    /// exceeds this maximum, before decoding any byte of the payload. It does not restrict the size of the payloads
    /// encoded by the application. The payload size does not include the size of any header for this payload, such
    /// as the encapsulation header with the ice protocol. Implementations must return a value greater than or equal
    /// to <c>0</c> and less than <see cref="int.MaxValue" />.</para>
    /// <para>This property is the counterpart of the Ice property
    /// <see href="https://docs.zeroc.com/ice/3.8/cpp/ice#Ice.MessageSizeMax">Ice.MessageSizeMax</see>, with two
    /// differences: the maximum payload size is in bytes rather than kilobytes, and it excludes the protocol header
    /// that Ice.MessageSizeMax includes.</para></remarks>
    int MaxPayloadSize { get; }
}
