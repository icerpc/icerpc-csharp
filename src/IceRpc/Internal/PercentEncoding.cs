// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Text;
using System.Text.Unicode;

namespace IceRpc.Internal;

/// <summary>Converts the strings of Ice identities and facets to and from the canonical percent-encoded form of a URI
/// path segment or fragment.</summary>
/// <remarks>The canonical form leaves unescaped exactly the characters RFC 3986 allows in the URI component, and
/// percent-encodes every other byte of the UTF-8 string with uppercase hex digits. A string has a single canonical
/// form, and a URI parser that normalizes (RFC 3986 section 6.2.2) does not alter this form: the hex digits are
/// already uppercase, and no unreserved character is escaped.</remarks>
internal static class PercentEncoding
{
    /// <summary>Gets the characters that a URI path segment contains unescaped: the unreserved characters, the
    /// sub-delimiters, <c>:</c> and <c>@</c> (RFC 3986 section 3.3).</summary>
    internal static SearchValues<char> PathSegmentChars { get; } =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~!$&'()*+,;=:@");

    /// <summary>Gets the characters that a URI fragment contains unescaped: the path segment characters plus
    /// <c>/</c> and <c>?</c>.</summary>
    internal static SearchValues<char> FragmentChars { get; } =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~!$&'()*+,;=:@/?");

    private const string HexDigits = "0123456789ABCDEF";

    /// <summary>Percent-encodes a string in canonical form.</summary>
    /// <param name="value">The string to encode.</param>
    /// <param name="unescapedChars">The characters that stay unescaped; all of them are ASCII.</param>
    /// <returns>The encoded string.</returns>
    internal static string Encode(string value, SearchValues<char> unescapedChars)
    {
        if (!value.AsSpan().ContainsAnyExcept(unescapedChars))
        {
            return value;
        }

        // UTF-8 lead and continuation bytes are >= 0x80, so they are never in unescapedChars.
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        var sb = new StringBuilder(utf8.Length * 3);
        foreach (byte b in utf8)
        {
            if (unescapedChars.Contains((char)b))
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('%').Append(HexDigits[b >> 4]).Append(HexDigits[b & 0xF]);
            }
        }
        return sb.ToString();
    }

    /// <summary>Decodes a string percent-encoded in canonical form.</summary>
    /// <param name="value">The encoded string.</param>
    /// <param name="unescapedChars">The characters that the canonical form leaves unescaped.</param>
    /// <param name="decoded">When this method returns <see langword="true" />, the decoded string.</param>
    /// <returns><see langword="true" /> when <paramref name="value" /> is in canonical form: it contains only
    /// <paramref name="unescapedChars" /> and escapes with uppercase hex digits of bytes that are not in
    /// <paramref name="unescapedChars" />, and these bytes form valid UTF-8; otherwise, <see langword="false" />.
    /// </returns>
    internal static bool TryDecode(ReadOnlySpan<char> value, SearchValues<char> unescapedChars, out string decoded)
    {
        int index = value.IndexOfAnyExcept(unescapedChars);
        if (index == -1)
        {
            decoded = value.ToString();
            return true;
        }

        decoded = "";
        var utf8 = new byte[value.Length];
        int length = Encoding.ASCII.GetBytes(value[..index], utf8);

        for (; index < value.Length; ++index)
        {
            char c = value[index];
            if (unescapedChars.Contains(c))
            {
                utf8[length++] = (byte)c;
            }
            else if (c == '%' &&
                index + 2 < value.Length &&
                HexDigits.IndexOf(value[index + 1], StringComparison.Ordinal) is int high and >= 0 &&
                HexDigits.IndexOf(value[index + 2], StringComparison.Ordinal) is int low and >= 0 &&
                !unescapedChars.Contains((char)((high << 4) | low)))
            {
                utf8[length++] = (byte)((high << 4) | low);
                index += 2;
            }
            else
            {
                return false;
            }
        }

        ReadOnlySpan<byte> bytes = utf8.AsSpan(0, length);
        if (!Utf8.IsValid(bytes))
        {
            return false;
        }
        decoded = Encoding.UTF8.GetString(bytes);
        return true;
    }
}
