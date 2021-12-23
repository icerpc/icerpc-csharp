// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>Provides extension methods for class IceEncoder.</summary>
    public static class IceEncoderExtensions
    {
        /// <summary>Encodes a dictionary.</summary>
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the values.</param>
        public static void EncodeDictionary<TKey, TValue>(
            this ref IceEncoder encoder,
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
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the non-null values.</param>
        public static void EncodeDictionaryWithBitSequence<TKey, TValue>(
            this ref IceEncoder encoder,
            IEnumerable<KeyValuePair<TKey, TValue>> v,
            EncodeAction<TKey> keyEncodeAction,
            EncodeAction<TValue> valueEncodeAction)
            where TKey : notnull
        {
            int count = v.Count();
            encoder.EncodeSize(count);
            BitSequence bitSequence = encoder.EncodeBitSequence(count);
            int index = 0;
            foreach ((TKey key, TValue value) in v)
            {
                keyEncodeAction(ref encoder, key);
                if (value == null)
                {
                    bitSequence[index] = false;
                }
                else
                {
                    valueEncodeAction(ref encoder, value);
                }
                index++;
            }
        }

        /// <summary>Encodes a sequence.</summary>
        /// <paramtype name="T">The type of the sequence elements. It is non-nullable except for nullable class and
        /// proxy types.</paramtype>
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="encodeAction">The encode action for an element.</param>
        public static void EncodeSequence<T>(
            this ref IceEncoder encoder,
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
        /// <param name="encoder">The Ice encoder.</param>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="encodeAction">The encode action for a non-null value.</param>
        /// <remarks>This method always encodes a bit sequence.</remarks>
        public static void EncodeSequenceWithBitSequence<T>(
            this ref IceEncoder encoder,
            IEnumerable<T> v,
            EncodeAction<T> encodeAction)
        {
            int count = v.Count(); // potentially slow Linq Count()
            encoder.EncodeSize(count);
            BitSequence bitSequence = encoder.EncodeBitSequence(count);
            int index = 0;
            foreach (T item in v)
            {
                if (item == null)
                {
                    bitSequence[index] = false;
                }
                else
                {
                    encodeAction(ref encoder, item);
                }
                index++;
            }
        }
    }
}
