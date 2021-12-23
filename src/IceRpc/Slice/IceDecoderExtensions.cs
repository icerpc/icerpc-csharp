// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Slice
{
    /// <summary>Provides extension methods for class IceDecoder.</summary>
    public static class IceDecoderExtensions
    {
        /// <summary>Decodes a dictionary.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="minValueSize">The minimum size of each value of the dictionary, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each value of the dictionary.</param>
        /// <returns>The dictionary decoded by this decoder.</returns>
        public static Dictionary<TKey, TValue> DecodeDictionary<TKey, TValue>(
            this ref IceDecoder decoder,
            int minKeySize,
            int minValueSize,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
        {
            int sz = decoder.DecodeAndCheckSeqSize(minKeySize + minValueSize);
            var dict = new Dictionary<TKey, TValue>(sz);
            for (int i = 0; i < sz; ++i)
            {
                TKey key = keyDecodeFunc(ref decoder);
                TValue value = valueDecodeFunc(ref decoder);
                dict.Add(key, value);
            }
            return dict;
        }

        /// <summary>Decodes a dictionary with null values encoded using a bit sequence.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The dictionary decoded by this decoder.</returns>
        public static Dictionary<TKey, TValue?> DecodeDictionaryWithBitSequence<TKey, TValue>(
            this ref IceDecoder decoder,
            int minKeySize,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue?> valueDecodeFunc)
            where TKey : notnull
        {
            int sz = decoder.DecodeAndCheckSeqSize(minKeySize);
            return decoder.DecodeDictionaryWithBitSequence(
                new Dictionary<TKey, TValue?>(sz),
                sz,
                keyDecodeFunc,
                valueDecodeFunc);
        }

        /// <summary>Decodes fields.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <returns>The fields as an immutable dictionary.</returns>
        public static ImmutableDictionary<int, ReadOnlyMemory<byte>> DecodeFieldDictionary(this ref IceDecoder decoder)
        {
            int size = decoder.DecodeSize();
            if (size == 0)
            {
                return ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;
            }
            else
            {
                var builder = ImmutableDictionary.CreateBuilder<int, ReadOnlyMemory<byte>>();
                for (int i = 0; i < size; ++i)
                {
                    builder.Add(decoder.DecodeVarInt(), decoder.DecodeSequence<byte>());
                }
                return builder.ToImmutable();
            }
        }

        /// <summary>Decodes a sequence.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="minElementSize">The minimum size of each element of the sequence, in bytes.</param>
        /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
        /// <returns>An array of T.</returns>
        public static T[] DecodeSequence<T>(
            this ref IceDecoder decoder,
            int minElementSize,
            DecodeFunc<T> decodeFunc)
        {
            int count = decoder.DecodeAndCheckSeqSize(minElementSize);
            if (count == 0)
            {
                return Array.Empty<T>();
            }
            else
            {
                var array = new T[count];
                for (int i = 0; i < count; ++i)
                {
                    array[i] = decodeFunc(ref decoder);
                }
                return array;
            }
        }

        /// <summary>Decodes a sequence that encodes null values using a bit sequence.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="decodeFunc">The decode function for each non-null element of the sequence.</param>
        /// <returns>An array of T.</returns>
        public static T[] DecodeSequenceWithBitSequence<T>(this ref IceDecoder decoder, DecodeFunc<T> decodeFunc)
        {
            int count = decoder.DecodeAndCheckSeqSize(0);
            if (count == 0)
            {
                return Array.Empty<T>();
            }
            else
            {
                BitSequenceReader bitSequenceReader = decoder.DecodeBitSequence(count);
                var array = new T[count];
                for (int i = 0; i < count; ++i)
                {
                    array[i] = bitSequenceReader.Read() ? decodeFunc(ref decoder) : default!;
                }
                return array;
            }
        }

        /// <summary>Decodes a sorted dictionary.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="minValueSize">The minimum size of each value of the dictionary, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each value of the dictionary.</param>
        /// <returns>The sorted dictionary decoded by this decoder.</returns>
        public static SortedDictionary<TKey, TValue> DecodeSortedDictionary<TKey, TValue>(
            this ref IceDecoder decoder,
            int minKeySize,
            int minValueSize,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
        {
            int sz = decoder.DecodeAndCheckSeqSize(minKeySize + minValueSize);
            var dict = new SortedDictionary<TKey, TValue>();
            for (int i = 0; i < sz; ++i)
            {
                TKey key = keyDecodeFunc(ref decoder);
                TValue value = valueDecodeFunc(ref decoder);
                dict.Add(key, value);
            }
            return dict;
        }

        /// <summary>Decodes a sorted dictionary.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The sorted dictionary decoded by this decoder.</returns>
        public static SortedDictionary<TKey, TValue?> DecodeSortedDictionaryWithBitSequence<TKey, TValue>(
            this ref IceDecoder decoder,
            int minKeySize,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue?> valueDecodeFunc)
            where TKey : notnull =>
            decoder.DecodeDictionaryWithBitSequence(
                new SortedDictionary<TKey, TValue?>(),
                decoder.DecodeAndCheckSeqSize(minKeySize),
                keyDecodeFunc,
                valueDecodeFunc);

        private static TDict DecodeDictionaryWithBitSequence<TDict, TKey, TValue>(
            this ref IceDecoder decoder,
            TDict dict,
            int size,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue?> valueDecodeFunc)
            where TDict : IDictionary<TKey, TValue?>
            where TKey : notnull
        {
            BitSequenceReader bitSequenceReader = decoder.DecodeBitSequence(size);
            for (int i = 0; i < size; ++i)
            {
                TKey key = keyDecodeFunc(ref decoder);
                TValue? value = bitSequenceReader.Read() ? valueDecodeFunc(ref decoder) : default(TValue?);
                dict.Add(key, value);
            }
            return dict;
        }
    }
}
