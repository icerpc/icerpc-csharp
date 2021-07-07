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
        /// <param name="reader">The buffer reader.</param>
        /// <returns>The fields as an immutable dictionary.</returns>
        /// <remarks>The values of the dictionary reference memory in the reader's underlying buffer.</remarks>
        public static ImmutableDictionary<int, ReadOnlyMemory<byte>> ReadFieldDictionary(this IceDecoder reader)
        {
            Debug.Assert(reader.Encoding == Encoding.V20);

            int size = reader.ReadSize();
            if (size == 0)
            {
                return ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;
            }
            else
            {
                var builder = ImmutableDictionary.CreateBuilder<int, ReadOnlyMemory<byte>>();
                for (int i = 0; i < size; ++i)
                {
                    (int key, ReadOnlyMemory<byte> value) = reader.ReadField();
                    builder.Add(key, value);
                }
                return builder.ToImmutable();
            }
        }

        /// <summary>Decodes a field value written using <see cref="OutgoingFrame.Fields"/>.</summary>
        /// <typeparam name="T">The decoded type.</typeparam>
        /// <param name="value">The field value as a byte buffer.</param>
        /// <param name="decoder">The <see cref="IceReader{T}"/> for the field value.</param>
        /// <param name="connection">The connection that received this field (used only for proxies).</param>
        /// <param name="invoker">The invoker of proxies in the decoded type.</param>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidDataException">Thrown when <paramref name="decoder"/> finds invalid data.
        /// </exception>
        public static T ReadFieldValue<T>(
            this ReadOnlyMemory<byte> value,
            IceReader<T> decoder,
            Connection? connection = null,
            IInvoker? invoker = null)
        {
            var reader = new IceDecoder(value, Encoding.V20, connection, invoker);
            T result = decoder(reader);
            reader.CheckEndOfBuffer(skipTaggedParams: false);
            return result;
        }
    }
}
