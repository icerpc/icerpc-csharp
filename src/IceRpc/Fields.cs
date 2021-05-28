// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Extension class to read fields.</summary>
    public static class Fields
    {
        /// <summary>Reads fields from an <see cref="InputStream"/>.</summary>
        /// <param name="istr">The input stream.</param>
        /// <returns>The fields as an immutable dictionary.</returns>
        /// <remarks>The values of the dictionary reference memory in the stream's underlying buffer.</remarks>
        public static ImmutableDictionary<int, ReadOnlyMemory<byte>> ReadFieldDictionary(this InputStream istr)
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
    }
}
