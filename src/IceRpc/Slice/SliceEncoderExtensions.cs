// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace IceRpc.Slice
{
    /// <summary>Provides extension methods for <see cref="SliceEncoder"/>.</summary>
    public static class SliceEncoderExtensions
    {
        /// <summary>Encodes a dictionary.</summary>
        /// <param name="encoder">The Slice encoder.</param>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the values.</param>
        public static void EncodeDictionary<TKey, TValue>(
            this ref SliceEncoder encoder,
            IEnumerable<KeyValuePair<TKey, TValue>> v,
            EncodeAction<TKey> keyEncodeAction,
            EncodeAction<TValue> valueEncodeAction)
            where TKey : notnull
        {
            encoder.EncodeSize(v.Count());
            foreach ((TKey key, TValue value) in v)
            {
                keyEncodeAction(ref encoder, key);
                valueEncodeAction(ref encoder, value);
            }
        }

        /// <summary>Encodes a dictionary with null values encoded using a bit sequence.</summary>
        /// <param name="encoder">The Slice encoder.</param>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the non-null values.</param>
        public static void EncodeDictionaryWithBitSequence<TKey, TValue>(
            this ref SliceEncoder encoder,
            IEnumerable<KeyValuePair<TKey, TValue>> v,
            EncodeAction<TKey> keyEncodeAction,
            EncodeAction<TValue> valueEncodeAction)
            where TKey : notnull
        {
            int count = v.Count();
            encoder.EncodeSize(count);
            if (count > 0)
            {
                BitSequenceWriter bitSequenceWriter = encoder.GetBitSequenceWriter(count);
                foreach ((TKey key, TValue value) in v)
                {
                    keyEncodeAction(ref encoder, key);

                    bitSequenceWriter.Write(value != null);
                    if (value != null)
                    {
                        valueEncodeAction(ref encoder, value);
                    }
                }
            }
        }

        /// <summary>Encodes a fields dictionary.</summary>
        /// <param name="encoder">This Slice encoder.</param>
        /// <param name="fieldsOverrides">The fields overrides.</param>
        /// <param name="fields">The fields.</param>
        public static void EncodeFieldDictionary(
            this ref SliceEncoder encoder,
            IDictionary<int, EncodeAction> fieldsOverrides,
            IDictionary<int, ReadOnlySequence<byte>> fields)
        {
            // can be larger than necessary, which is fine
            int sizeLength = encoder.GetSizeLength(fields.Count + fieldsOverrides.Count);

            Span<byte> countPlaceholder = encoder.GetPlaceholderSpan(sizeLength);

            int count = 0; // the number of fields

            // Encode the fields overrides then the actual fields.

            foreach ((int key, EncodeAction action) in fieldsOverrides)
            {
                encoder.EncodeVarInt(key);
                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(2);
                int startPos = encoder.EncodedByteCount;
                action(ref encoder);
                encoder.Encoding.EncodeSize(encoder.EncodedByteCount - startPos, sizePlaceholder);
                count++;
            }

            foreach ((int key, ReadOnlySequence<byte> value) in fields)
            {
                if (!fieldsOverrides.ContainsKey(key))
                {
                    encoder.EncodeVarInt(key);
                    encoder.EncodeSize(checked((int)value.Length));

                    if (value.IsSingleSegment)
                    {
                        encoder.WriteByteSpan(value.FirstSpan);
                    }
                    else
                    {
                        // TODO: for now the fields are backed by a single byte[] so this can't happen.
                        foreach (ReadOnlyMemory<byte> buffer in value)
                        {
                            encoder.WriteByteSpan(buffer.Span);
                        }
                    }
                    count++;
                }
            }

            encoder.Encoding.EncodeSize(count, countPlaceholder);
        }

        /// <summary>Encodes a sequence of fixed-size numeric values, such as int and long.</summary>
        /// <param name="encoder">The Slice encoder.</param>
        /// <param name="v">The sequence of numeric values.</param>
        public static void EncodeSequence<T>(this ref SliceEncoder encoder, IEnumerable<T> v) where T : struct
        {
            switch (v)
            {
                case T[] vArray:
                    encoder.EncodeSpan(new ReadOnlySpan<T>(vArray));
                    break;

                case ImmutableArray<T> vImmutableArray:
                    encoder.EncodeSpan(vImmutableArray.AsSpan());
                    break;

                case ArraySegment<T> vArraySegment:
                    encoder.EncodeSpan((ReadOnlySpan<T>)vArraySegment.AsSpan());
                    break;

                default:
                    encoder.EncodeSequence(
                        v,
                        (ref SliceEncoder encoder, T element) => encoder.EncodeFixedSizeNumeric(element));
                    break;
            }
        }

        /// <summary>Encodes a sequence.</summary>
        /// <paramtype name="T">The type of the sequence elements. It is non-nullable except for nullable class and
        /// proxy types.</paramtype>
        /// <param name="encoder">The Slice encoder.</param>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="encodeAction">The encode action for an element.</param>
        public static void EncodeSequence<T>(
            this ref SliceEncoder encoder,
            IEnumerable<T> v,
            EncodeAction<T> encodeAction)
        {
            encoder.EncodeSize(v.Count()); // potentially slow Linq Count()
            foreach (T item in v)
            {
                encodeAction(ref encoder, item);
            }
        }

        /// <summary>Encodes a sequence with null values encoded using a bit sequence.</summary>
        /// <paramtype name="T">The nullable type of the sequence elements.</paramtype>
        /// <param name="encoder">The Slice encoder.</param>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="encodeAction">The encode action for a non-null value.</param>
        /// <remarks>This method always encodes a bit sequence.</remarks>
        public static void EncodeSequenceWithBitSequence<T>(
            this ref SliceEncoder encoder,
            IEnumerable<T> v,
            EncodeAction<T> encodeAction)
        {
            int count = v.Count(); // potentially slow Linq Count()
            encoder.EncodeSize(count);
            if (count > 0)
            {
                BitSequenceWriter bitSequenceWriter = encoder.GetBitSequenceWriter(count);
                foreach (T item in v)
                {
                    bitSequenceWriter.Write(item != null);
                    if (item != null)
                    {
                        encodeAction(ref encoder, item);
                    }
                }
            }
        }

        /// <summary>Encodes a span of fixed-size numeric values, such as int and long.</summary>
        /// <param name="encoder">The Slice encoder.</param>
        /// <param name="v">The span of numeric values represented by a <see cref="ReadOnlySpan{T}"/>.</param>
        // This method works because (as long as) there is no padding in the memory representation of the ReadOnlySpan.
        public static void EncodeSpan<T>(this ref SliceEncoder encoder, ReadOnlySpan<T> v) where T : struct
        {
            encoder.EncodeSize(v.Length);
            if (!v.IsEmpty)
            {
                encoder.WriteByteSpan(MemoryMarshal.AsBytes(v));
            }
        }
    }
}
