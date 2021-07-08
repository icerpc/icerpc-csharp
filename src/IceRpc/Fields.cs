// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Extension class to read fields.</summary>
    public static class Fields
    {
        /// <summary>Reads fields from a <see cref="IceDecoder"/>.</summary>
        /// <param name="iceDecoder">The Ice decoder.</param>
        /// <returns>The fields as an immutable dictionary.</returns>
        /// <remarks>The values of the dictionary reference memory in the decoder's underlying buffer.</remarks>
        public static ImmutableDictionary<int, ReadOnlyMemory<byte>> ReadFieldDictionary(this IceDecoder iceDecoder)
        {
            Debug.Assert(iceDecoder.Encoding == Encoding.V20);

            int size = iceDecoder.ReadSize();
            if (size == 0)
            {
                return ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;
            }
            else
            {
                var builder = ImmutableDictionary.CreateBuilder<int, ReadOnlyMemory<byte>>();
                for (int i = 0; i < size; ++i)
                {
                    (int key, ReadOnlyMemory<byte> value) = iceDecoder.ReadField();
                    builder.Add(key, value);
                }
                return builder.ToImmutable();
            }
        }

        /// <summary>Decodes a field value written using <see cref="OutgoingFrame.Fields"/>.</summary>
        /// <typeparam name="T">The decoded type.</typeparam>
        /// <param name="value">The field value as a byte buffer.</param>
        /// <param name="iceReader">The <see cref="IceDecodeFunc{T}"/> for the field value.</param>
        /// <param name="connection">The connection that received this field (used only for proxies).</param>
        /// <param name="invoker">The invoker of proxies in the decoded type.</param>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidDataException">Thrown when <paramref name="iceReader"/> finds invalid data.
        /// </exception>
        public static T ReadFieldValue<T>(
            this ReadOnlyMemory<byte> value,
            IceDecodeFunc<T> iceReader,
            Connection? connection = null,
            IInvoker? invoker = null)
        {
            var iceDecoder = new IceDecoder(value, Encoding.V20, connection, invoker);
            T result = iceReader(iceDecoder);
            iceDecoder.CheckEndOfBuffer(skipTaggedParams: false);
            return result;
        }
    }
}
