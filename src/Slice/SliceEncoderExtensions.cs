// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Slice;

/// <summary>Provides extension methods for <see cref="SliceEncoder" />.</summary>
public static class SliceEncoderExtensions
{
    /// <summary>Encodes a dictionary.</summary>
    /// <typeparam name="TKey">The dictionary key type.</typeparam>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
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

    /// <summary>Encodes a dictionary with an optional value type (T? in Slice).</summary>
    /// <typeparam name="TKey">The dictionary key type.</typeparam>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="v">The dictionary to encode.</param>
    /// <param name="keyEncodeAction">The encode action for the keys.</param>
    /// <param name="valueEncodeAction">The encode action for the non-null values.</param>
    public static void EncodeDictionaryWithOptionalValueType<TKey, TValue>(
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
            foreach ((TKey key, TValue value) in v)
            {
                // Each entry is encoded like a:
                // compact struct Pair
                // {
                //     key: Key,
                //     value: Value?
                // }
                encoder.EncodeBool(value is not null); // simplified bit sequence

                keyEncodeAction(ref encoder, key);
                if (value is not null)
                {
                    valueEncodeAction(ref encoder, value);
                }
            }
        }
    }

    /// <summary>Encodes a sequence of fixed-size numeric values, such as int or ulong.</summary>
    /// <typeparam name="T">The sequence element type.</typeparam>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="v">The sequence of numeric values.</param>
    public static void EncodeSequence<T>(this ref SliceEncoder encoder, IEnumerable<T> v)
        where T : struct
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

            case List<T> list:
                encoder.EncodeSpan((ReadOnlySpan<T>)CollectionsMarshal.AsSpan(list));
                break;

            default:
                encoder.EncodeSequence(
                    v,
                    (ref SliceEncoder encoder, T element) => encoder.EncodeFixedSizeNumeric(element));
                break;
        }
    }

    /// <summary>Encodes a sequence.</summary>
    /// <typeparam name="T">The type of the sequence elements. It is non-nullable except for nullable class and
    /// proxy types.</typeparam>
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

    /// <summary>Encodes a sequence where the element type is an optional Slice type (T?).</summary>
    /// <typeparam name="T">The nullable type of the sequence elements.</typeparam>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="v">The sequence to encode.</param>
    /// <param name="encodeAction">The encode action for a non-null value.</param>
    /// <remarks>This method always encodes a bit sequence.</remarks>
    public static void EncodeSequenceOfOptionals<T>(
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
                bitSequenceWriter.Write(item is not null);
                if (item is not null)
                {
                    encodeAction(ref encoder, item);
                }
            }
        }
    }

    /// <summary>Encodes a span of fixed-size numeric values, such as int or ulong.</summary>
    /// <typeparam name="T">The span element type.</typeparam>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="v">The span of numeric values represented by a <see cref="ReadOnlySpan{T}" />.</param>
    public static void EncodeSpan<T>(this ref SliceEncoder encoder, ReadOnlySpan<T> v)
        where T : struct
    {
        // This method works because (as long as) there is no padding in the memory representation of the ReadOnlySpan.
        encoder.EncodeSize(v.Length);
        if (!v.IsEmpty)
        {
            encoder.WriteByteSpan(MemoryMarshal.AsBytes(v));
        }
    }

    /// <summary>Copies a sequence of bytes to the underlying buffer writer.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="v">The sequence to copy.</param>
    public static void WriteByteSequence(this ref SliceEncoder encoder, ReadOnlySequence<byte> v)
    {
        if (!v.IsEmpty)
        {
            if (v.IsSingleSegment)
            {
                encoder.WriteByteSpan(v.FirstSpan);
            }
            else
            {
                foreach (ReadOnlyMemory<byte> buffer in v)
                {
                    encoder.WriteByteSpan(buffer.Span);
                }
            }
        }
    }
}
