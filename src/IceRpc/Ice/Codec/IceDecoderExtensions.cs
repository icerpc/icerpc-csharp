// Copyright (c) ZeroC, Inc.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc.Ice.Codec;

/// <summary>Provides extension methods for <see cref="IceDecoder" /> to decode sequences or dictionaries.</summary>
public static class IceDecoderExtensions
{
    /// <summary>Verifies the Ice decoder has reached the end of its underlying buffer.</summary>
    /// <param name="decoder">The Ice decoder.</param>
    public static void CheckEndOfBuffer(this ref IceDecoder decoder)
    {
        if (!decoder.End)
        {
            throw new InvalidDataException($"There are {decoder.Remaining} bytes remaining in the buffer.");
        }
    }

    /// <summary>Decodes a dictionary.</summary>
    /// <typeparam name="TDictionary">The type of the returned dictionary.</typeparam>
    /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of the values in the dictionary.</typeparam>
    /// <param name="decoder">The Ice decoder.</param>
    /// <param name="dictionaryFactory">The factory for creating the dictionary instance.</param>
    /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
    /// <param name="valueDecodeFunc">The decode function for each value of the dictionary.</param>
    /// <returns>The dictionary decoded by this decoder.</returns>
    /// <remarks>Duplicate-key detection depends on the collection returned by <paramref name="dictionaryFactory" />.
    /// When the collection throws <see cref="ArgumentException" /> on a duplicate key — as
    /// <see cref="Dictionary{TKey,TValue}" /> and <see cref="SortedDictionary{TKey,TValue}" /> do — this method
    /// translates that exception into an <see cref="InvalidDataException" />. Collections that silently accept
    /// duplicates (e.g. <see cref="List{T}" />) follow their own semantics.</remarks>
    public static TDictionary DecodeDictionary<TDictionary, TKey, TValue>(
        this ref IceDecoder decoder,
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
            decoder.IncreaseCollectionAllocation(count, Unsafe.SizeOf<TKey>() + Unsafe.SizeOf<TValue>());
            TDictionary dictionary = dictionaryFactory(count);
            for (int i = 0; i < count; ++i)
            {
                TKey key = keyDecodeFunc(ref decoder);
                TValue value = valueDecodeFunc(ref decoder);
                try
                {
                    dictionary.Add(new KeyValuePair<TKey, TValue>(key, value));
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException($"Received dictionary with duplicate key '{key}'.", exception);
                }
            }
            return dictionary;
        }
    }

    /// <summary>Decodes a sequence of fixed-size numeric values.</summary>
    /// <typeparam name="T">The sequence element type.</typeparam>
    /// <param name="decoder">The Ice decoder.</param>
    /// <param name="checkElement">A delegate used to check each element of the array (optional).</param>
    /// <returns>An array of T.</returns>
    public static T[] DecodeSequence<T>(this ref IceDecoder decoder, Action<T>? checkElement = null)
        where T : struct
    {
        int count = decoder.DecodeSize();
        if (count == 0)
        {
            return [];
        }
        else
        {
            int elementSize = Unsafe.SizeOf<T>();
            decoder.IncreaseCollectionAllocation(count, elementSize);
            var value = new T[count];
            Span<byte> destination = MemoryMarshal.Cast<T, byte>(value.AsSpan());
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
    /// <param name="decoder">The Ice decoder.</param>
    /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
    /// <returns>An array of T.</returns>
    public static T[] DecodeSequence<T>(this ref IceDecoder decoder, DecodeFunc<T> decodeFunc)
    {
        int count = decoder.DecodeSize();
        if (count == 0)
        {
            return [];
        }
        else
        {
            decoder.IncreaseCollectionAllocation(count, Unsafe.SizeOf<T>());
            var array = new T[count];
            for (int i = 0; i < count; ++i)
            {
                array[i] = decodeFunc(ref decoder);
            }
            return array;
        }
    }

    /// <summary>Decodes an Ice sequence mapped to a custom collection.</summary>
    /// <typeparam name="TCollection">The type of the returned collection.</typeparam>
    /// <typeparam name="TElement">The type of the elements in the collection.</typeparam>
    /// <param name="decoder">The Ice decoder.</param>
    /// <param name="collectionFactory">A delegate used to create the collection with the specified capacity.</param>
    /// <param name="addElement">A delegate used to add each decoded element to the collection.</param>
    /// <param name="decodeFunc">The decode function for each element of the collection.</param>
    /// <returns>The decoded collection.</returns>
    public static TCollection DecodeCollection<TCollection, TElement>(
        this ref IceDecoder decoder,
        Func<int, TCollection> collectionFactory,
        Action<TCollection, TElement> addElement,
        DecodeFunc<TElement> decodeFunc)
    {
        int count = decoder.DecodeSize();
        if (count == 0)
        {
            return collectionFactory(0);
        }
        else
        {
            decoder.IncreaseCollectionAllocation(count, Unsafe.SizeOf<TElement>());
            TCollection collection = collectionFactory(count);
            for (int i = 0; i < count; ++i)
            {
                addElement(collection, decodeFunc(ref decoder));
            }
            return collection;
        }
    }

    /// <summary>Decodes an Ice sequence mapped to a <see cref="LinkedList{T}" />.</summary>
    /// <typeparam name="TElement">The type of the elements in the sequence.</typeparam>
    /// <param name="decoder">The Ice decoder.</param>
    /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
    /// <returns>A <see cref="LinkedList{T}" />.</returns>
    public static LinkedList<TElement> DecodeLinkedList<TElement>(
        this ref IceDecoder decoder,
        DecodeFunc<TElement> decodeFunc) =>
        decoder.DecodeCollection<LinkedList<TElement>, TElement>(
            collectionFactory: _ => new LinkedList<TElement>(),
            (list, element) => list.AddLast(element),
            decodeFunc);

    /// <summary>Decodes an Ice sequence mapped to a <see cref="List{T}" />.</summary>
    /// <typeparam name="TElement">The type of the elements in the sequence.</typeparam>
    /// <param name="decoder">The Ice decoder.</param>
    /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
    /// <returns>A <see cref="List{T}" />.</returns>
    public static List<TElement> DecodeList<TElement>(
        this ref IceDecoder decoder,
        DecodeFunc<TElement> decodeFunc) =>
        decoder.DecodeCollection<List<TElement>, TElement>(
            collectionFactory: count => new List<TElement>(count),
            (list, element) => list.Add(element),
            decodeFunc);

    /// <summary>Decodes an Ice sequence mapped to a <see cref="Queue{T}" />.</summary>
    /// <typeparam name="TElement">The type of the elements in the sequence.</typeparam>
    /// <param name="decoder">The Ice decoder.</param>
    /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
    /// <returns>A <see cref="Queue{T}" />.</returns>
    public static Queue<TElement> DecodeQueue<TElement>(this ref IceDecoder decoder, DecodeFunc<TElement> decodeFunc) =>
        decoder.DecodeCollection<Queue<TElement>, TElement>(
            collectionFactory: count => new Queue<TElement>(count),
            (queue, element) => queue.Enqueue(element),
            decodeFunc);

    /// <summary>Decodes an Ice sequence mapped to a <see cref="Stack{T}" />.</summary>
    /// <typeparam name="TElement">The type of the elements in the sequence.</typeparam>
    /// <param name="decoder">The Ice decoder.</param>
    /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
    /// <returns>A <see cref="Stack{T}" />.</returns>
    public static Stack<TElement> DecodeStack<TElement>(this ref IceDecoder decoder, DecodeFunc<TElement> decodeFunc)
    {
        var array = decoder.DecodeSequence(decodeFunc);
        Array.Reverse(array);
        return new Stack<TElement>(array);
    }
}
