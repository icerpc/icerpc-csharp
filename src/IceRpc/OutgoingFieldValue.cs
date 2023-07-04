// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using System.Buffers;

namespace IceRpc;

/// <summary>Represents the value of a field that is about to be sent. It's a kind of discriminated union: only one
/// of the struct's properties can be set.</summary>
public readonly record struct OutgoingFieldValue
{
    /// <summary>Gets the value of this outgoing field.</summary>
    /// <value>The value of this outgoing field. Defaults to an empty byte sequence.</value>
    /// <remarks><see cref="ByteSequence" /> is set when the outgoing field value is constructed with <see
    /// cref="OutgoingFieldValue(ReadOnlySequence{byte})" />.</remarks>
    public ReadOnlySequence<byte> ByteSequence { get; }

    /// <summary>Gets the action used to encode the field value using the Slice2 encoding. The action is executed when
    /// the fields are about to be sent.</summary>
    /// <value>The encode action of this outgoing field. Defaults to <see langword="null"/>.</value>
    /// <remarks><see cref="EncodeAction" /> is set when the outgoing field value is constructed with <see
    /// cref="OutgoingFieldValue(EncodeAction)" />.</remarks>
    public EncodeAction? EncodeAction { get; }

    /// <summary>Constructs an outgoing field value that holds a byte sequence.</summary>
    /// <param name="byteSequence">The field encoded value.</param>
    public OutgoingFieldValue(ReadOnlySequence<byte> byteSequence)
    {
        ByteSequence = byteSequence;
        EncodeAction = null;
    }

    /// <summary>Constructs an outgoing field value that holds an encode action.</summary>
    /// <param name="encodeAction">The action used to encode the field value.</param>
    public OutgoingFieldValue(EncodeAction encodeAction)
    {
        ByteSequence = default;
        EncodeAction = encodeAction;
    }
}
