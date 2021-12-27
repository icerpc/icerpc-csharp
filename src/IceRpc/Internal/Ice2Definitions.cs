// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;

namespace IceRpc.Internal
{
    // Definitions for the ice2 protocol.

    internal static class Ice2Definitions
    {
        internal static readonly Encoding Encoding = Encoding.Ice20;

        /// <summary>Encodes ice2 fields. Fields are encoded first, followed by the field defaults.</summary>
        /// <param name="encoder">This Ice encoder.</param>
        /// <param name="fields">The fields.</param>
        /// <param name="fieldsDefaults">The fields defaults.</param>
        internal static void EncodeFields(
            this ref IceEncoder encoder,
            Dictionary<int, EncodeAction>? fields,
            IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fieldsDefaults)
        {
            // can be larger than necessary, which is fine
            int sizeLength = Ice20Encoding.GetSizeLength(fieldsDefaults.Count + (fields?.Count ?? 0));

            Span<byte> countPlaceholder = encoder.GetPlaceholderSpan(sizeLength);

            int count = 0; // the number of fields

            // First encode the fields then the remaining FieldsDefaults.

            if (fields != null)
            {
                foreach ((int key, EncodeAction action) in fields)
                {
                    encoder.EncodeVarInt(key);
                    Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(2);
                    int startPos = encoder.EncodedByteCount;
                    action(ref encoder);
                    Ice20Encoding.EncodeSize(encoder.EncodedByteCount - startPos, sizePlaceholder);
                    count++;
                }
            }
            foreach ((int key, ReadOnlyMemory<byte> value) in fieldsDefaults)
            {
                if (fields == null || !fields.ContainsKey(key))
                {
                    encoder.EncodeVarInt(key);
                    encoder.EncodeSize(value.Length);
                    encoder.WriteByteSpan(value.Span);
                    count++;
                }
            }

            Ice20Encoding.EncodeSize(count, countPlaceholder);
        }
    }
}
