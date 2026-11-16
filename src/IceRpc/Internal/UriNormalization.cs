// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace IceRpc.Internal;

/// <summary>Computes the normalized form of URI components, as described in RFC 3986 section 6.2.2. An address
/// accepts only normalized components, so that equivalent URIs have the same string form; it never rewrites a
/// component, and its error messages name the normalized form instead.</summary>
/// <remarks><see cref="Uri" /> decodes the percent-escapes of unreserved characters, and removes the dot segments of
/// a URI with an authority, but it keeps the case of the hex digits of the other percent-escapes and keeps the dot
/// segments of a URI without an authority: an address created from such a URI is rejected.</remarks>
internal static class UriNormalization
{
    private const string HexDigits = "0123456789ABCDEF";

    private static readonly SearchValues<char> _unreservedChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~");

    /// <summary>Checks that each <c>%</c> of a URI component starts a percent-escape, i.e. is followed by two hex
    /// digits.</summary>
    /// <param name="value">The URI component.</param>
    /// <returns><see langword="true" /> when every <c>%</c> starts a percent-escape; otherwise,
    /// <see langword="false" />.</returns>
    internal static bool IsPercentEncoded(string value)
    {
        for (int index = value.IndexOf('%', StringComparison.Ordinal);
            index != -1;
            index = value.IndexOf('%', index + 3, StringComparison.Ordinal))
        {
            if (index + 2 >= value.Length || HexValue(value[index + 1]) == -1 || HexValue(value[index + 2]) == -1)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Normalizes the percent-encoding of a URI component: converts the hex digits of each percent-escape to
    /// uppercase, and decodes each percent-escape of an unreserved character (RFC 3986 sections 6.2.2.1 and 6.2.2.2).
    /// </summary>
    /// <param name="value">The URI component.</param>
    /// <returns>The normalized component, or <paramref name="value" /> itself when it is already normalized. A
    /// <c>%</c> that is not followed by two hex digits is kept as is.</returns>
    internal static string NormalizePercentEncoding(string value)
    {
        int index = value.IndexOf('%', StringComparison.Ordinal);
        if (index == -1)
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        sb.Append(value, 0, index);
        for (; index < value.Length; ++index)
        {
            char c = value[index];
            if (c == '%' &&
                index + 2 < value.Length &&
                HexValue(value[index + 1]) is int high and >= 0 &&
                HexValue(value[index + 2]) is int low and >= 0)
            {
                char decoded = (char)((high << 4) | low);
                if (_unreservedChars.Contains(decoded))
                {
                    sb.Append(decoded);
                }
                else
                {
                    sb.Append('%').Append(HexDigits[high]).Append(HexDigits[low]);
                }
                index += 2;
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>Normalizes an absolute URI path: normalizes its percent-encoding, then removes its dot segments
    /// (RFC 3986 sections 6.2.2.2 and 6.2.2.3).</summary>
    /// <param name="path">The path, which must start with a <c>/</c>.</param>
    /// <returns>The normalized path, or <paramref name="path" /> itself when it is already normalized.</returns>
    internal static string NormalizePath(string path) => RemoveDotSegments(NormalizePercentEncoding(path));

    /// <summary>Removes the <c>.</c> and <c>..</c> segments of an absolute path, as described by the
    /// remove_dot_segments algorithm of RFC 3986 section 5.2.4.</summary>
    private static string RemoveDotSegments(string path)
    {
        Debug.Assert(path.StartsWith('/', StringComparison.Ordinal));

        // A dot segment necessarily starts right after a '/'.
        if (!path.Contains("/.", StringComparison.Ordinal))
        {
            return path;
        }

        string[] segments = path.Split('/');
        var output = new List<string>(segments.Length);
        for (int i = 1; i < segments.Length; ++i)
        {
            string segment = segments[i];
            bool isLast = i == segments.Length - 1;
            if (segment == "." || segment == "..")
            {
                if (segment == ".." && output.Count > 0)
                {
                    output.RemoveAt(output.Count - 1);
                }

                // A path that ends with a dot segment keeps its trailing '/'.
                if (isLast)
                {
                    output.Add("");
                }
            }
            else
            {
                output.Add(segment);
            }
        }
        return $"/{string.Join('/', output)}";
    }

    private static int HexValue(char c) =>
        c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'A' and <= 'F' => c - 'A' + 10,
            >= 'a' and <= 'f' => c - 'a' + 10,
            _ => -1
        };
}
