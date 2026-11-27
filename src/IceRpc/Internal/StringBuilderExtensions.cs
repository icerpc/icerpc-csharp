// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Globalization;
using System.Text;

namespace IceRpc.Internal;

/// <summary>Provides extension methods for <see cref="StringBuilder" />.</summary>
internal static class StringBuilderExtensions
{
    extension(StringBuilder sb)
    {
        /// <summary>Appends a string to this string builder, with each character not in
        /// <paramref name="unencodedChars" /> percent-encoded: its UTF-8 bytes are written as <c>%XX</c>, with
        /// uppercase hexadecimal digits.</summary>
        /// <param name="value">The string to append.</param>
        /// <param name="unencodedChars">The characters to append as is. They must all be ASCII characters.</param>
        /// <returns>This string builder.</returns>
        internal StringBuilder AppendPercentEncoded(string value, SearchValues<char> unencodedChars)
        {
            if (value.AsSpan().IndexOfAnyExcept(unencodedChars) == -1)
            {
                return sb.Append(value);
            }

            foreach (byte b in Encoding.UTF8.GetBytes(value))
            {
                if (unencodedChars.Contains((char)b))
                {
                    sb.Append((char)b);
                }
                else
                {
                    sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }
            return sb;
        }
    }
}
