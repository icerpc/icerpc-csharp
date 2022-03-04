// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using System.Buffers;

namespace IceRpc
{
    /// <summary>Represents the value of a field that is about to be sent. It's a kind of discriminated union: only one
    /// of the struct's properties can be set.</summary>
    public readonly record struct OutgoingFieldValue
    {
        /// <summary>Holds the value of this outgoing field when <see cref="EncodeAction"/> is <c>null</c>.</summary>
        public ReadOnlySequence<byte> ByteSequence { get; }

        /// <summary>Holds the value of this outgoing field not null</summary>
        public EncodeAction? EncodeAction { get; }

        /// <summary>Constructs an outgoing field value that holds a byte sequence.</summary>
        public OutgoingFieldValue(ReadOnlySequence<byte> byteSequence)
        {
            ByteSequence = byteSequence;
            EncodeAction = null;
        }

        /// <summary>Constructs an outgoing field value that holds an encode action.</summary>
        public OutgoingFieldValue(EncodeAction encodeAction)
        {
            ByteSequence = default;
            EncodeAction = encodeAction;
        }

        /// <summary>Encodes this field value using a Slice encoder.</summary>
        public void Encode(ref SliceEncoder encoder)
        {
            if (encoder.Encoding == Encoding.Slice11)
            {
                throw new NotSupportedException($"cannot encode a {nameof(OutgoingFieldValue)} using the 1.1 encoding");
            }

            if (EncodeAction is EncodeAction encodeAction)
            {
                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(2);
                int startPos = encoder.EncodedByteCount;
                encodeAction(ref encoder);
                Slice20Encoding.EncodeSize(encoder.EncodedByteCount - startPos, sizePlaceholder);
            }
            else
            {
                encoder.EncodeSize(checked((int)ByteSequence.Length));
                if (!ByteSequence.IsEmpty)
                {
                    if (ByteSequence.IsSingleSegment)
                    {
                        encoder.WriteByteSpan(ByteSequence.FirstSpan);
                    }
                    else
                    {
                        foreach (ReadOnlyMemory<byte> buffer in ByteSequence)
                        {
                            encoder.WriteByteSpan(buffer.Span);
                        }
                    }
                }
            }
        }
    }
}
