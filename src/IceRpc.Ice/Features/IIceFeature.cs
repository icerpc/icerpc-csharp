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
    /// <value>The base proxy.</value>
    IIceProxy? BaseProxy { get; }

    /// <summary>Gets the options to use when encoding the payload of an outgoing response.</summary>
    /// <value>The Ice encode options. <see langword="null" /> is equivalent to <see cref="IceEncodeOptions.Default"
    /// />.</value>
    IceEncodeOptions? EncodeOptions { get; }

    /// <summary>Gets the maximum collection allocation when decoding a payload, in bytes.</summary>
    /// <value>The maximum collection allocation.</value>
    int MaxCollectionAllocation { get; }

    /// <summary>Gets the maximum depth when decoding a class recursively.</summary>
    /// <value>The maximum depth.</value>
    int MaxDepth { get; }

    /// <summary>Gets the maximum size of an Ice-encoded payload, in bytes. An Ice-encoded payload corresponds to the
    /// encoded arguments of an operation, or the encoded return values of an operation.</summary>
    /// <value>The maximum size of an Ice-encoded payload, in bytes.</value>
    /// <remarks>The payload size does not include the size of any header for this payload, such as the encapsulation
    /// header with the ice protocol.</remarks>
    int MaxPayloadSize { get; }
}
