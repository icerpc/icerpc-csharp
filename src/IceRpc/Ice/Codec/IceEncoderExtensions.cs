// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace IceRpc.Ice.Codec;

/// <summary>Provides extension methods for <see cref="IceEncoder" /> to encode sequences or dictionaries.</summary>
public static class IceEncoderExtensions
{
    /// <summary>Encodes a dictionary.</summary>
    /// <typeparam name="TKey">The dictionary key type.</typeparam>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
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
        if (!v.TryGetNonEnumeratedCount(out int count))
        {
            KeyValuePair<TKey, TValue>[] array = v.ToArray();
            count = array.Length;
            v = array;
        }
        encoder.EncodeSize(count);
        foreach ((TKey key, TValue value) in v)
        {
            keyEncodeAction(ref encoder, key);
            valueEncodeAction(ref encoder, value);
        }
    }

    /// <summary>Encodes a sequence of fixed-size numeric values, such as int or ulong.</summary>
    /// <typeparam name="T">The sequence element type.</typeparam>
    /// <param name="encoder">The Ice encoder.</param>
    /// <param name="v">The sequence of numeric values.</param>
    public static void EncodeSequence<T>(this ref IceEncoder encoder, IEnumerable<T> v)
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
                    (ref IceEncoder encoder, T element) => encoder.EncodeFixedSizeNumeric(element));
                break;
        }
    }

    /// <summary>Encodes a sequence.</summary>
    /// <typeparam name="T">The type of the sequence elements. It is non-nullable except for class and proxy types.
    /// </typeparam>
    /// <param name="encoder">The Ice encoder.</param>
    /// <param name="v">The sequence to encode.</param>
    /// <param name="encodeAction">The encode action for an element.</param>
    public static void EncodeSequence<T>(this ref IceEncoder encoder, IEnumerable<T> v, EncodeAction<T> encodeAction)
    {
        if (!v.TryGetNonEnumeratedCount(out int count))
        {
            T[] array = v.ToArray();
            count = array.Length;
            v = array;
        }
        encoder.EncodeSize(count);
        foreach (T item in v)
        {
            encodeAction(ref encoder, item);
        }
    }

    /// <summary>Encodes a span of fixed-size numeric values, such as int or ulong.</summary>
    /// <typeparam name="T">The span element type.</typeparam>
    /// <param name="encoder">The Ice encoder.</param>
    /// <param name="v">The span of numeric values represented by a <see cref="ReadOnlySpan{T}" />.</param>
    public static void EncodeSpan<T>(this ref IceEncoder encoder, ReadOnlySpan<T> v)
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
    /// <param name="encoder">The Ice encoder.</param>
    /// <param name="v">The sequence to copy.</param>
    public static void WriteByteSequence(this ref IceEncoder encoder, ReadOnlySequence<byte> v)
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
