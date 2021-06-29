// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Extension class to read fields.</summary>
    public static class Fields
    {
        /// <summary>Reads fields from an <see cref="BufferReader"/>.</summary>
        /// <param name="istr">The buffer reader.</param>
        /// <returns>The fields as an immutable dictionary.</returns>
        /// <remarks>The values of the dictionary reference memory in the stream's underlying buffer.</remarks>
        public static ImmutableDictionary<int, ReadOnlyMemory<byte>> ReadFieldDictionary(this BufferReader istr)
        {
            Debug.Assert(istr.Encoding == Encoding.V20);

            int size = istr.ReadSize();
            if (size == 0)
            {
                return ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;
            }
            else
            {
                var builder = ImmutableDictionary.CreateBuilder<int, ReadOnlyMemory<byte>>();
                for (int i = 0; i < size; ++i)
                {
                    (int key, ReadOnlyMemory<byte> value) = istr.ReadField();
                    builder.Add(key, value);
                }
                return builder.ToImmutable();
            }
        }

        /// <summary>Reads a field value written using <see cref="OutgoingFrame.Fields"/>.</summary>
        /// <typeparam name="T">The type of the contents.</typeparam>
        /// <param name="value">The field value.</param>
        /// <param name="reader">The <see cref="InputStreamReader{T}"/> that reads the field value.</param>
        /// <param name="connection">The connection that received this field (used only for proxies).</param>
        /// <param name="invoker">The invoker of proxies in the contents.</param>
        /// <returns>The contents of the value.</returns>
        /// <exception cref="InvalidDataException">Thrown when <paramref name="reader"/> finds invalid data.</exception>
        public static T ReadFieldValue<T>(
            this ReadOnlyMemory<byte> value,
            InputStreamReader<T> reader,
            Connection? connection = null,
            IInvoker? invoker = null)
        {
            var istr = new BufferReader(value, Encoding.V20, connection, invoker);
            T result = reader(istr);
            istr.CheckEndOfBuffer(skipTaggedParams: false);
            return result;
        }
    }
}
