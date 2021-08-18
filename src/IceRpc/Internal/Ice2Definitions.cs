// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    // Definitions for the ice2 protocol.

    internal static class Ice2Definitions
    {
        internal static readonly Encoding Encoding = Encoding.Ice20;

        private static readonly ReadOnlyMemory<byte> _voidReturnValuePayload11 = ReadOnlyMemory<byte>.Empty;

        // The only byte is for the compression format.
        private static readonly ReadOnlyMemory<byte> _voidReturnValuePayload20 = new byte[] { 0 };

        /// <summary>Encodes ice2 fields. Fields are encoded first, followed by the field defaults.</summary>
        /// <param name="encoder">This Ice encoder.</param>
        /// <param name="fields">The fields.</param>
        /// <param name="fieldsDefaults">The fields defaults.</param>
        internal static void EncodeFields(
            this Ice20Encoder encoder,
            Dictionary<int, Action<IceEncoder>>? fields,
            IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fieldsDefaults)
        {
            // can be larger than necessary, which is fine
            int sizeLength = Ice20Encoder.GetSizeLength(fieldsDefaults.Count + (fields?.Count ?? 0));

            BufferWriter.Position start = encoder.StartFixedLengthSize(sizeLength);

            int count = 0; // the number of fields

            // First encode the fields then the remaining FieldsDefaults.

            if (fields != null)
            {
                foreach ((int key, Action<IceEncoder> action) in fields)
                {
                    encoder.EncodeVarInt(key);
                    BufferWriter.Position startValue = encoder.StartFixedLengthSize(2);
                    action(encoder);
                    encoder.EndFixedLengthSize(startValue, 2);
                    count++;
                }
            }
            foreach ((int key, ReadOnlyMemory<byte> value) in fieldsDefaults)
            {
                if (fields == null || !fields.ContainsKey(key))
                {
                    encoder.EncodeVarInt(key);
                    encoder.EncodeSize(value.Length);
                    encoder.BufferWriter.WriteByteSpan(value.Span);
                    count++;
                }
            }
            encoder.EncodeFixedLengthSize(count, start, sizeLength);
        }

        /// <summary>Returns the payload of an ice2 request frame for an operation with no argument.</summary>
        /// <param name="encoding">The encoding of this empty args payload.</param>
        /// <returns>The payload.</returns>
        internal static ReadOnlyMemory<byte> GetEmptyArgsPayload(Encoding encoding) =>
            GetVoidReturnValuePayload(encoding);

        /// <summary>Returns the payload of an ice2 response frame for an operation returning void.</summary>
        /// <param name="encoding">The encoding of this void return.</param>
        /// <returns>The payload.</returns>
        internal static ReadOnlyMemory<byte> GetVoidReturnValuePayload(Encoding encoding)
        {
            encoding.CheckSupportedIceEncoding();
            return encoding == Encoding.Ice11 ? _voidReturnValuePayload11 : _voidReturnValuePayload20;
        }
    }
}
