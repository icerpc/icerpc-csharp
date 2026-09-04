// Copyright (c) ZeroC, Inc.

using IceRpc.Protobuf;

namespace IceRpc.Features;

/// <summary>Represents a feature used to customize the encoding and decoding of request and response payloads.
/// </summary>
public interface IProtobufFeature
{
    /// <summary>Gets the maximum length of an encoded Protobuf message, in bytes.</summary>
    /// <value>The maximum length of a Protobuf message, in bytes.</value>
    /// <remarks>This limit applies only when decoding the payload of an incoming request or response, whether this
    /// payload holds a single message or a stream of messages: the decoding throws
    /// <see cref="InvalidDataException" /> when the length encoded in the 5-byte prefix of a message exceeds this
    /// maximum, before parsing any byte of the message. It does not restrict the length of the messages encoded by the
    /// application. The message length does not include this prefix. Implementations must return a value greater than
    /// or equal to <c>0</c>.</remarks>
    int MaxMessageLength { get; }

    /// <summary>Gets the options to use when encoding the payload of an outgoing response.</summary>
    /// <value>The Protobuf encode options. <see langword="null" /> is equivalent to
    /// <see cref="ProtobufEncodeOptions.Default" />.</value>
    ProtobufEncodeOptions? EncodeOptions { get; }
}
