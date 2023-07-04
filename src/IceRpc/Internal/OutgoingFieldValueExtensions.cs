// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;

namespace IceRpc.Internal;

/// <summary>Provides an extension method for <see cref="OutgoingFieldValue" />.</summary>
internal static class OutgoingFieldValueExtensions
{
    /// <summary>Encodes a field value using a Slice encoder.</summary>
    /// <param name="value">The field value to encode.</param>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="sizeLength">The number of bytes to use to encode the size when <see cref="EncodeAction" /> is
    /// not <see langword="null" />.</param>
    internal static void Encode(this OutgoingFieldValue value, ref SliceEncoder encoder, int sizeLength = 2)
    {
        if (encoder.Encoding == SliceEncoding.Slice1)
        {
            // It's a field of the ice protocol, and the ice protocol supports only one field: the request context.
            // It's known to both peers.
            if (value.EncodeAction is EncodeAction encodeAction)
            {
                encodeAction(ref encoder);
            }
            else
            {
                encoder.WriteByteSequence(value.ByteSequence);
            }
        }
        else
        {
            if (value.EncodeAction is EncodeAction encodeAction)
            {
                // We encode a size: this way, the recipient can skip this field value if it does not know the field.
                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(sizeLength);
                int startPos = encoder.EncodedByteCount;
                encodeAction(ref encoder);
                SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
            }
            else
            {
                encoder.EncodeSize(checked((int)value.ByteSequence.Length));
                encoder.WriteByteSequence(value.ByteSequence);
            }
        }
    }
}
