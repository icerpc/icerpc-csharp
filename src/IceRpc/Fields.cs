// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Extension class to decode fields.</summary>
    public static class Fields
    {
        /// <summary>Decodes fields from a <see cref="IceDecoder"/>.</summary>
        /// <param name="decoder">The Ice decoder.</param>
        /// <returns>The fields as an immutable dictionary.</returns>
        /// <remarks>The values of the dictionary reference memory in the decoder's underlying buffer.</remarks>
        public static ImmutableDictionary<int, ReadOnlyMemory<byte>> DecodeFieldDictionary(this IceDecoder decoder)
        {
            Debug.Assert(decoder.Encoding == Encoding.V20);

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
                    (int key, ReadOnlyMemory<byte> value) = decoder.DecodeField();
                    builder.Add(key, value);
                }
                return builder.ToImmutable();
            }
        }

        /// <summary>Decodes a field value written using <see cref="OutgoingFrame.Fields"/>.</summary>
        /// <typeparam name="T">The decoded type.</typeparam>
        /// <param name="value">The field value as a byte buffer.</param>
        /// <param name="decodeFunc">The <see cref="DecodeFunc{T}"/> for the field value.</param>
        /// <param name="connection">The connection that received this field (used only for proxies).</param>
        /// <param name="invoker">The invoker of proxies in the decoded type.</param>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidDataException">Thrown when <paramref name="decodeFunc"/> finds invalid data.
        /// </exception>
        public static T DecodeFieldValue<T>(
            this ReadOnlyMemory<byte> value,
            DecodeFunc<T> decodeFunc,
            Connection? connection = null,
            IInvoker? invoker = null)
        {
            var decoder = new IceDecoder(value, Encoding.V20, connection, invoker);
            T result = decodeFunc(decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return result;
        }
    }
}
