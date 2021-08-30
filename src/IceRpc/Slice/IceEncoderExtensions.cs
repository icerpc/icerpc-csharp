// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc.Slice
{
    /// <summary>Provides extension methods for class IceEncoder.</summary>
    public static class IceEncoderExtensions
    {
        /// <summary>Encodes a dictionary.</summary>
        /// <paramtype name="TEncoder">The type of the Ice encoder.</paramtype>
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the values.</param>
        public static void EncodeDictionary<TEncoder, TKey, TValue>(
            this TEncoder encoder,
            IEnumerable<KeyValuePair<TKey, TValue>> v,
            EncodeAction<TEncoder, TKey> keyEncodeAction,
            EncodeAction<TEncoder, TValue> valueEncodeAction)
            where TEncoder : IceEncoder
            where TKey : notnull
        {
            encoder.EncodeSize(v.Count());
            foreach ((TKey key, TValue value) in v)
            {
                keyEncodeAction(encoder, key);
                valueEncodeAction(encoder, value);
            }
        }

        /// <summary>Encodes a dictionary with null values encoded using a bit sequence.</summary>
        /// <paramtype name="TEncoder">The type of the Ice encoder.</paramtype>
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the non-null values.</param>
        public static void EncodeDictionaryWithBitSequence<TEncoder, TKey, TValue>(
            this TEncoder encoder,
            IEnumerable<KeyValuePair<TKey, TValue>> v,
            EncodeAction<TEncoder, TKey> keyEncodeAction,
            EncodeAction<TEncoder, TValue> valueEncodeAction)
            where TEncoder : IceEncoder
            where TKey : notnull
        {
            int count = v.Count();
            encoder.EncodeSize(count);
            BitSequence bitSequence = encoder.BufferWriter.WriteBitSequence(count);
            int index = 0;
            foreach ((TKey key, TValue value) in v)
            {
                keyEncodeAction(encoder, key);
                if (value == null)
                {
                    bitSequence[index] = false;
                }
                else
                {
                    valueEncodeAction(encoder, value);
                }
                index++;
            }
        }

        /// <summary>Encodes a sequence.</summary>
        /// <paramtype name="TEncoder">The type of the Ice encoder.</paramtype>
        /// <paramtype name="T">The type of the sequence elements. It is non-nullable except for nullable class and
        /// proxy types.</paramtype>
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="encodeAction">The encode action for an element.</param>
        public static void EncodeSequence<TEncoder, T>(
            this TEncoder encoder,
            IEnumerable<T> v,
            EncodeAction<TEncoder, T> encodeAction) where TEncoder : IceEncoder
        {
            encoder.EncodeSize(v.Count()); // potentially slow Linq Count()
            foreach (T item in v)
            {
                encodeAction(encoder, item);
            }
        }

        /// <summary>Encodes a sequence with null values encoded using a bit sequence.</summary>
        /// <paramtype name="TEncoder">The type of the Ice encoder.</paramtype>
        /// <paramtype name="T">The nullable type of the sequence elements.</paramtype>
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="encodeAction">The encode action for a non-null value.</param>
        /// <remarks>This method always encodes a bit sequence.</remarks>
        public static void EncodeSequenceWithBitSequence<TEncoder, T>(
            this TEncoder encoder,
            IEnumerable<T> v,
            EncodeAction<TEncoder, T> encodeAction) where TEncoder : IceEncoder
        {
            int count = v.Count(); // potentially slow Linq Count()
            encoder.EncodeSize(count);
            BitSequence bitSequence = encoder.BufferWriter.WriteBitSequence(count);
            int index = 0;
            foreach (T item in v)
            {
                if (item == null)
                {
                    bitSequence[index] = false;
                }
                else
                {
                    encodeAction(encoder, item);
                }
                index++;
            }
        }
    }
}
