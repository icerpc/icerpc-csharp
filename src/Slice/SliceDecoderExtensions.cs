// Copyright (c) ZeroC, Inc.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Slice;

/// <summary>Provides extension methods for <see cref="SliceDecoder" />.</summary>
public static class SliceDecoderExtensions
{
    /// <summary>Decodes a dictionary.</summary>
    /// <typeparam name="TDictionary">The type of the returned dictionary.</typeparam>
    /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of the values in the dictionary.</typeparam>
    /// <param name="decoder">The Slice decoder.</param>
    /// <param name="dictionaryFactory">The factory for creating the dictionary instance.</param>
    /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
    /// <param name="valueDecodeFunc">The decode function for each value of the dictionary.</param>
    /// <returns>The dictionary decoded by this decoder.</returns>
    public static TDictionary DecodeDictionary<TDictionary, TKey, TValue>(
        this ref SliceDecoder decoder,
        Func<int, TDictionary> dictionaryFactory,
        DecodeFunc<TKey> keyDecodeFunc,
        DecodeFunc<TValue> valueDecodeFunc)
        where TKey : notnull
        where TDictionary : ICollection<KeyValuePair<TKey, TValue>>
    {
        int count = decoder.DecodeSize();
        if (count == 0)
        {
            return dictionaryFactory(0);
        }
        else
        {
            decoder.IncreaseCollectionAllocation(count * (Unsafe.SizeOf<TKey>() + Unsafe.SizeOf<TValue>()));
            TDictionary dictionary = dictionaryFactory(count);
            for (int i = 0; i < count; ++i)
            {
                TKey key = keyDecodeFunc(ref decoder);
                TValue value = valueDecodeFunc(ref decoder);
                dictionary.Add(new KeyValuePair<TKey, TValue>(key, value));
            }
            return dictionary;
        }
    }

    /// <summary>Decodes a dictionary with an optional value type (T? in Slice).</summary>
    /// <typeparam name="TDictionary">The type of the returned dictionary.</typeparam>
    /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of the values in the dictionary.</typeparam>
    /// <param name="decoder">The Slice decoder.</param>
    /// <param name="dictionaryFactory">The factory for creating the dictionary instance.</param>
    /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
    /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
    /// <returns>The dictionary decoded by this decoder.</returns>
    public static TDictionary DecodeDictionaryWithOptionalValueType<TDictionary, TKey, TValue>(
        this ref SliceDecoder decoder,
        Func<int, TDictionary> dictionaryFactory,
        DecodeFunc<TKey> keyDecodeFunc,
        DecodeFunc<TValue?> valueDecodeFunc)
        where TKey : notnull
        where TDictionary : ICollection<KeyValuePair<TKey, TValue?>>
    {
        int count = decoder.DecodeSize();
        if (count == 0)
        {
            return dictionaryFactory(0);
        }
        else
        {
            decoder.IncreaseCollectionAllocation(count * (Unsafe.SizeOf<TKey>() + Unsafe.SizeOf<TValue?>()));
            TDictionary dictionary = dictionaryFactory(count);
            for (int i = 0; i < count; ++i)
            {
                // Each entry is encoded like a:
                // compact struct Pair
                // {
                //     key: Key,
                //     value: Value?
                // }
                bool hasValue = decoder.DecodeBool(); // simplified bit sequence
                TKey key = keyDecodeFunc(ref decoder);
                TValue? value = hasValue ? valueDecodeFunc(ref decoder) : default;
                dictionary.Add(new KeyValuePair<TKey, TValue?>(key, value));
            }
            return dictionary;
        }
    }

    /// <summary>Decodes a sequence of fixed-size numeric values.</summary>
    /// <typeparam name="T">The sequence element type.</typeparam>
    /// <param name="decoder">The Slice decoder.</param>
    /// <param name="checkElement">A delegate used to check each element of the array (optional).</param>
    /// <returns>An array of T.</returns>
    public static T[] DecodeSequence<T>(this ref SliceDecoder decoder, Action<T>? checkElement = null)
        where T : struct
    {
        int count = decoder.DecodeSize();
        if (count == 0)
        {
            return Array.Empty<T>();
        }
        else
        {
            int elementSize = Unsafe.SizeOf<T>();
            decoder.IncreaseCollectionAllocation(count * elementSize);
            var value = new T[count];
            Span<byte> destination = MemoryMarshal.Cast<T, byte>(value);
            decoder.CopyTo(destination);

            if (checkElement is not null)
            {
                foreach (T e in value)
                {
                    checkElement(e);
                }
            }
            return value;
        }
    }

    /// <summary>Decodes a sequence.</summary>
    /// <typeparam name="T">The type of the elements in the array.</typeparam>
    /// <param name="decoder">The Slice decoder.</param>
    /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
    /// <returns>An array of T.</returns>
    public static T[] DecodeSequence<T>(this ref SliceDecoder decoder, DecodeFunc<T> decodeFunc)
    {
        int count = decoder.DecodeSize();
        if (count == 0)
        {
            return Array.Empty<T>();
        }
        else
        {
            decoder.IncreaseCollectionAllocation(count * Unsafe.SizeOf<T>());
            var array = new T[count];
            for (int i = 0; i < count; ++i)
            {
                array[i] = decodeFunc(ref decoder);
            }
            return array;
        }
    }

    /// <summary>Decodes a sequence.</summary>
    /// <typeparam name="TSequence">The type of the returned sequence.</typeparam>
    /// <typeparam name="TElement">The type of the elements in the sequence.</typeparam>
    /// <param name="decoder">The Slice decoder.</param>
    /// <param name="sequenceFactory">The factory for creating the sequence instance.</param>
    /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
    /// <returns>A TSequence.</returns>
    public static TSequence DecodeSequence<TSequence, TElement>(
        this ref SliceDecoder decoder,
        Func<int, TSequence> sequenceFactory,
        DecodeFunc<TElement> decodeFunc) where TSequence : ICollection<TElement>
    {
        int count = decoder.DecodeSize();
        if (count == 0)
        {
            return sequenceFactory(0);
        }
        else
        {
            decoder.IncreaseCollectionAllocation(count * Unsafe.SizeOf<TElement>());
            TSequence sequence = sequenceFactory(count);
            for (int i = 0; i < count; ++i)
            {
                sequence.Add(decodeFunc(ref decoder));
            }
            return sequence;
        }
    }

    /// <summary>Decodes a sequence where the element type is an optional Slice type (T?).</summary>
    /// <typeparam name="T">The type of the elements in the array.</typeparam>
    /// <param name="decoder">The Slice decoder.</param>
    /// <param name="decodeFunc">The decode function for each non-null element of the sequence.</param>
    /// <returns>An array of T.</returns>
    /// <remarks>We return a T? and not a T to avoid ambiguities in the generated code with nullable reference
    /// types such as string?.</remarks>
    public static T?[] DecodeSequenceOfOptionals<T>(this ref SliceDecoder decoder, DecodeFunc<T> decodeFunc)
    {
        int count = decoder.DecodeSize();
        if (count == 0)
        {
            return Array.Empty<T>();
        }
        else
        {
            BitSequenceReader bitSequenceReader = decoder.GetBitSequenceReader(count);
            decoder.IncreaseCollectionAllocation(count * Unsafe.SizeOf<T>());
            var array = new T?[count];
            for (int i = 0; i < count; ++i)
            {
                array[i] = bitSequenceReader.Read() ? decodeFunc(ref decoder) : default;
            }
            return array;
        }
    }

    /// <summary>Decodes a sequence where the element type is an optional Slice type (T?).</summary>
    /// <typeparam name="TSequence">The type of the returned sequence.</typeparam>
    /// <typeparam name="TElement">The type of the elements in the sequence.</typeparam>
    /// <param name="decoder">The Slice decoder.</param>
    /// <param name="sequenceFactory">The factory for creating the sequence instance.</param>
    /// <param name="decodeFunc">The decode function for each non-null element of the sequence.</param>
    /// <returns>A TSequence.</returns>
    public static TSequence DecodeSequenceOfOptionals<TSequence, TElement>(
        this ref SliceDecoder decoder,
        Func<int, TSequence> sequenceFactory,
        DecodeFunc<TElement> decodeFunc) where TSequence : ICollection<TElement>
    {
        int count = decoder.DecodeSize();
        if (count == 0)
        {
            return sequenceFactory(0);
        }
        else
        {
            BitSequenceReader bitSequenceReader = decoder.GetBitSequenceReader(count);
            decoder.IncreaseCollectionAllocation(count * Unsafe.SizeOf<TElement>());
            TSequence sequence = sequenceFactory(count);
            for (int i = 0; i < count; ++i)
            {
                sequence.Add(bitSequenceReader.Read() ? decodeFunc(ref decoder) : default!);
            }
            return sequence;
        }
    }
}
