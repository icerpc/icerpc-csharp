// Copyright (c) ZeroC, Inc.

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

    /// <summary>Gets the action used to write the field value. This action is executed when the fields are sent.
    /// </summary>
    /// <value>The write action of this outgoing field. Defaults to <see langword="null"/>.</value>
    public Action<IBufferWriter<byte>>? WriteAction { get; }

    /// <summary>Constructs an outgoing field value that holds a byte sequence.</summary>
    /// <param name="byteSequence">The field encoded value.</param>
    public OutgoingFieldValue(ReadOnlySequence<byte> byteSequence)
    {
        ByteSequence = byteSequence;
        WriteAction = null;
    }

    /// <summary>Constructs an outgoing field value that holds a write action.</summary>
    /// <param name="writeAction">The action that writes the field value.</param>
    public OutgoingFieldValue(Action<IBufferWriter<byte>> writeAction)
    {
        ByteSequence = default;
        WriteAction = writeAction;
    }
}
