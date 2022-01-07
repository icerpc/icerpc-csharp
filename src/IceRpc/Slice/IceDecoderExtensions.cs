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
        /// <param name="dictionaryFactory">The factory for creating the dictionary instance.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each value of the dictionary.</param>
        /// <returns>The dictionary decoded by this decoder.</returns>
        public static TDictionary DecodeDictionary<TDictionary, TKey, TValue>(
            this ref IceDecoder decoder,
            int minKeySize,
            int minValueSize,
            Func<int, TDictionary> dictionaryFactory,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TDictionary : IDictionary<TKey, TValue>
        {
            int sz = decoder.DecodeAndCheckSeqSize(minKeySize + minValueSize);
            TDictionary dict = dictionaryFactory(sz);
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
        /// <param name="dictionaryFactory">The factory for creating the dictionary instance.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The dictionary decoded by this decoder.</returns>
        public static TDictionary DecodeDictionaryWithBitSequence<TDictionary, TKey, TValue>(
            this ref IceDecoder decoder,
            int minKeySize,
            Func<int, TDictionary> dictionaryFactory,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue?> valueDecodeFunc)
            where TKey : notnull
            where TDictionary : IDictionary<TKey, TValue?>
        {
            int sz = decoder.DecodeAndCheckSeqSize(minKeySize);
            return decoder.DecodeDictionaryWithBitSequence(
                dictionaryFactory(sz),
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

        /// <summary>Decodes a sequence.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="minElementSize">The minimum size of each element of the sequence, in bytes.</param>
        /// <param name="sequenceFactory">The factory for creating the sequence instance.</param>
        /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
        /// <returns>An IList of TElement.</returns>
        public static TContainer DecodeSequence<TContainer, TElement>(
            this ref IceDecoder decoder,
            int minElementSize,
            Func<int, TContainer> sequenceFactory,
            DecodeFunc<TElement> decodeFunc) where TContainer : IList<TElement>
        {
            int count = decoder.DecodeAndCheckSeqSize(minElementSize);
            TContainer container = sequenceFactory(count);
            for (int i = 0; i < count; ++i)
            {
                container.Add(decodeFunc(ref decoder));
            }
            return container;
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
                BitSequenceReader bitSequenceReader = decoder.GetBitSequenceReader(count);
                var array = new T[count];
                for (int i = 0; i < count; ++i)
                {
                    array[i] = bitSequenceReader.Read() ? decodeFunc(ref decoder) : default!;
                }
                return array;
            }
        }

        /// <summary>Decodes a sequence that encodes null values using a bit sequence.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="sequenceFactory">The factory for creating the sequence instance.</param>
        /// <param name="decodeFunc">The decode function for each non-null element of the sequence.</param>
        /// <returns>An array of T.</returns>
        public static TContainer DecodeSequenceWithBitSequence<TContainer, TElement>(
            this ref IceDecoder decoder,
            Func<int, TContainer> sequenceFactory,
            DecodeFunc<TElement> decodeFunc) where TContainer : IList<TElement>
        {
            int count = decoder.DecodeAndCheckSeqSize(0);
            if (count == 0)
            {
                return sequenceFactory(count);
            }
            else
            {
                BitSequenceReader bitSequenceReader = decoder.GetBitSequenceReader(count);
                TContainer container = sequenceFactory(count);
                for (int i = 0; i < count; ++i)
                {
                    container.Add(bitSequenceReader.Read() ? decodeFunc(ref decoder) : default!);
                }
                return container;
            }
        }

        private static TDict DecodeDictionaryWithBitSequence<TDict, TKey, TValue>(
            this ref IceDecoder decoder,
            TDict dict,
            int size,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue?> valueDecodeFunc)
            where TDict : IDictionary<TKey, TValue?>
            where TKey : notnull
        {
            BitSequenceReader bitSequenceReader = decoder.GetBitSequenceReader(size);
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
