// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections;
using System.Diagnostics;

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
        public static Dictionary<TKey, TValue> DecodeDictionary<TDecoder, TKey, TValue>(
            this TDecoder decoder,
            int minKeySize,
            int minValueSize,
            DecodeFunc<TDecoder, TKey> keyDecodeFunc,
            DecodeFunc<TDecoder, TValue> valueDecodeFunc)
            where TDecoder : IceDecoder
            where TKey : notnull
        {
            int sz = decoder.DecodeAndCheckSeqSize(minKeySize + minValueSize);
            var dict = new Dictionary<TKey, TValue>(sz);
            for (int i = 0; i < sz; ++i)
            {
                TKey key = keyDecodeFunc(decoder);
                TValue value = valueDecodeFunc(decoder);
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
        public static Dictionary<TKey, TValue?> DecodeDictionaryWithBitSequence<TDecoder, TKey, TValue>(
            this TDecoder decoder,
            int minKeySize,
            DecodeFunc<TDecoder, TKey> keyDecodeFunc,
            DecodeFunc<TDecoder, TValue?> valueDecodeFunc)
            where TDecoder : IceDecoder
            where TKey : notnull
        {
            int sz = decoder.DecodeAndCheckSeqSize(minKeySize);
            return decoder.DecodeDictionaryWithBitSequence(
                new Dictionary<TKey, TValue?>(sz),
                sz,
                keyDecodeFunc,
                valueDecodeFunc);
        }

        /// <summary>Decodes a sequence.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="minElementSize">The minimum size of each element of the sequence, in bytes.</param>
        /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
        /// <returns>A collection that provides the size of the sequence and allows you to decode the sequence from the
        /// the buffer. The return value does not fully implement ICollection{T}, in particular you can only call
        /// GetEnumerator() once on this collection. You would typically use this collection to construct a List{T} or
        /// some other generic collection that can be constructed from an IEnumerable{T}.</returns>
        public static ICollection<T> DecodeSequence<TDecoder, T>(
            this TDecoder decoder,
            int minElementSize,
            DecodeFunc<TDecoder, T> decodeFunc) where TDecoder : IceDecoder =>
            new Collection<TDecoder, T>(decoder, minElementSize, decodeFunc);

        /// <summary>Decodes a sequence that encodes null values using a bit sequence.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="decodeFunc">The decode function for each non-null element of the sequence.</param>
        /// <returns>A collection that provides the size of the sequence and allows you to decode the sequence from the
        /// the buffer. The returned collection does not fully implement ICollection{T}, in particular you can only
        /// call GetEnumerator() once on this collection. You would typically use this collection to construct a
        /// List{T} or some other generic collection that can be constructed from an IEnumerable{T}.</returns>
        public static ICollection<T> DecodeSequenceWithBitSequence<TDecoder, T>(
            this TDecoder decoder,
            DecodeFunc<TDecoder, T> decodeFunc) where TDecoder : IceDecoder =>
            new CollectionWithBitSequence<TDecoder, T>(decoder, decodeFunc);

        /// <summary>Decodes a sorted dictionary.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="minValueSize">The minimum size of each value of the dictionary, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each value of the dictionary.</param>
        /// <returns>The sorted dictionary decoded by this decoder.</returns>
        public static SortedDictionary<TKey, TValue> DecodeSortedDictionary<TDecoder, TKey, TValue>(
            this TDecoder decoder,
            int minKeySize,
            int minValueSize,
            DecodeFunc<TDecoder, TKey> keyDecodeFunc,
            DecodeFunc<TDecoder, TValue> valueDecodeFunc)
            where TDecoder : IceDecoder
            where TKey : notnull
        {
            int sz = decoder.DecodeAndCheckSeqSize(minKeySize + minValueSize);
            var dict = new SortedDictionary<TKey, TValue>();
            for (int i = 0; i < sz; ++i)
            {
                TKey key = keyDecodeFunc(decoder);
                TValue value = valueDecodeFunc(decoder);
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
        public static SortedDictionary<TKey, TValue?> DecodeSortedDictionaryWithBitSequence<TDecoder, TKey, TValue>(
            this TDecoder decoder,
            int minKeySize,
            DecodeFunc<TDecoder, TKey> keyDecodeFunc,
            DecodeFunc<TDecoder, TValue?> valueDecodeFunc)
            where TDecoder : IceDecoder
            where TKey : notnull =>
            decoder.DecodeDictionaryWithBitSequence(
                new SortedDictionary<TKey, TValue?>(),
                decoder.DecodeAndCheckSeqSize(minKeySize),
                keyDecodeFunc,
                valueDecodeFunc);

        private static TDict DecodeDictionaryWithBitSequence<TDecoder, TDict, TKey, TValue>(
            this TDecoder decoder,
            TDict dict,
            int size,
            DecodeFunc<TDecoder, TKey> keyDecodeFunc,
            DecodeFunc<TDecoder, TValue?> valueDecodeFunc)
            where TDecoder : IceDecoder
            where TDict : IDictionary<TKey, TValue?>
            where TKey : notnull
        {
            ReadOnlyBitSequence bitSequence = decoder.DecodeBitSequence(size);
            for (int i = 0; i < size; ++i)
            {
                TKey key = keyDecodeFunc(decoder);
                TValue? value = bitSequence[i] ? valueDecodeFunc(decoder) : default(TValue?);
                dict.Add(key, value);
            }
            return dict;
        }

        // Helper base class for the concrete collection implementations.
        private abstract class CollectionBase<TDecoder, T> : ICollection<T> where TDecoder : IceDecoder
        {
            public struct Enumerator : IEnumerator<T>
            {
                public T Current
                {
                    get
                    {
                        if (_pos == 0 || _pos > _collection.Count)
                        {
                            throw new InvalidOperationException();
                        }
                        return _current;
                    }

                    private set => _current = value;
                }

                object? IEnumerator.Current => Current;

                private readonly CollectionBase<TDecoder, T> _collection;
                private T _current;
                private int _pos;

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    if (_pos < _collection.Count)
                    {
                        Current = _collection.Decode(_pos);
                        _pos++;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }

                public void Reset() => throw new NotImplementedException();

                // Disable these warnings as the _current field is never read before it is initialized in MoveNext.
                // Declaring this field as nullable is not an option for a generic T that can be used with reference
                // and value types.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
                internal Enumerator(CollectionBase<TDecoder, T> collection)
#pragma warning restore CS8618
                {
                    _collection = collection;
#pragma warning disable CS8601 // Possible null reference assignment.
                    _current = default;
#pragma warning restore CS8601
                    _pos = 0;
                }
            }

            public int Count { get; }
            public bool IsReadOnly => true;
            protected TDecoder Decoder { get; }

            private bool _enumeratorRetrieved;

            public void Add(T item) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
            public bool Contains(T item) => throw new NotSupportedException();

            public void CopyTo(T[] array, int arrayIndex)
            {
                foreach (T value in this)
                {
                    array[arrayIndex++] = value;
                }
            }
            public IEnumerator<T> GetEnumerator()
            {
                if (_enumeratorRetrieved)
                {
                    throw new NotSupportedException("cannot get a second enumerator for this enumerable");
                }
                _enumeratorRetrieved = true;
                return new Enumerator(this);
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public bool Remove(T item) => throw new NotSupportedException();
            public void Reset() => throw new NotSupportedException();

            private protected abstract T Decode(int pos);

            protected CollectionBase(TDecoder decoder, int minElementSize)
            {
                Count = decoder.DecodeAndCheckSeqSize(minElementSize);
                Decoder = decoder;
            }
        }

        // Collection<T> holds the size of a Slice sequence and decodes the sequence elements on-demand. It does not
        // fully implement IEnumerable<T> and ICollection<T> (i.e. some methods throw NotSupportedException) because
        // it's not resettable: you can't use it to decode the same bytes multiple times.
        private sealed class Collection<TDecoder, T> : CollectionBase<TDecoder, T> where TDecoder : IceDecoder
        {
            private readonly DecodeFunc<TDecoder, T> _decodeFunc;
            internal Collection(TDecoder decoder, int minElementSize, DecodeFunc<TDecoder, T> decodeFunc)
                : base(decoder, minElementSize) => _decodeFunc = decodeFunc;

            private protected override T Decode(int pos)
            {
                Debug.Assert(pos < Count);
                return _decodeFunc(Decoder);
            }
        }

        // A collection that encodes nulls with a bit sequence.
        private sealed class CollectionWithBitSequence<TDecoder, T> : CollectionBase<TDecoder, T>
            where TDecoder : IceDecoder
        {
            private readonly ReadOnlyMemory<byte> _bitSequenceMemory;
            readonly DecodeFunc<TDecoder, T> _decodeFunc;

            internal CollectionWithBitSequence(TDecoder decoder, DecodeFunc<TDecoder, T> decodeFunc)
                : base(decoder, 0)
            {
                _bitSequenceMemory = decoder.DecodeBitSequenceMemory(Count);
                _decodeFunc = decodeFunc;
            }

            private protected override T Decode(int pos)
            {
                Debug.Assert(pos < Count);
                var bitSequence = new ReadOnlyBitSequence(_bitSequenceMemory.Span);
                return bitSequence[pos] ? _decodeFunc(Decoder) : default!;
            }
        }
    }
}
