// Copyright (c) ZeroC, Inc.

namespace IceRpc.Protobuf;

/// <summary>Represents a feature used to customize the encoding and decoding of request and response payloads.
/// </summary>
public interface IProtobufFeature
{
    /// <summary>Gets the maximum length of an encoded Protobuf message, in bytes.</summary>
    /// <value>The maximum length of a Protobuf message, in bytes.</value>
    int MaxMessageLength { get; }

    /// <summary>Gets the options to use when encoding the payload of an outgoing response.</summary>
    /// <value>The Protobuf encode options. <see langword="null" /> is equivalent to <see cref="ProtobufEncodeOptions.Default"
    /// />.</value>
    ProtobufEncodeOptions? EncodeOptions { get; }
}
