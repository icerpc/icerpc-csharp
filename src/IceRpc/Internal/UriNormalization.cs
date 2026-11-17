// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace IceRpc.Internal;

/// <summary>Checks that URI components are in the normalized form described in RFC 3986 section 6.2.2. An address
/// accepts only normalized components, so that equivalent URIs have the same string form.</summary>
/// <remarks><see cref="Uri" /> decodes the percent-escapes of unreserved characters, and removes the dot segments of
/// a URI with an authority, but it keeps the case of the hex digits of the other percent-escapes and keeps the dot
/// segments of a URI without an authority: an address created from such a URI is rejected.</remarks>
internal static class UriNormalization
{
    private static readonly SearchValues<char> _unreservedChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~");

    /// <summary>Checks whether a URI component contains a <c>%</c> that does not start a percent-escape, i.e. that is
    /// not followed by two hex digits.</summary>
    /// <param name="value">The URI component.</param>
    /// <returns><see langword="true" /> when a <c>%</c> does not start a percent-escape; otherwise,
    /// <see langword="false" />.</returns>
    internal static bool HasStrayPercent(string value)
    {
        for (int index = value.IndexOf('%', StringComparison.Ordinal);
            index != -1;
            index = value.IndexOf('%', index + 3, StringComparison.Ordinal))
        {
            if (index + 2 >= value.Length || HexValue(value[index + 1]) == -1 || HexValue(value[index + 2]) == -1)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Checks whether a URI component contains a percent-escape that is not normalized (RFC 3986 sections
    /// 6.2.2.1 and 6.2.2.2): a percent-escape with a lowercase hex digit, or a percent-escape of an unreserved
    /// character.</summary>
    /// <param name="value">The URI component. Each of its <c>%</c> starts a percent-escape.</param>
    /// <returns><see langword="true" /> when a percent-escape is not normalized; otherwise,
    /// <see langword="false" />.</returns>
    internal static bool HasNonNormalizedPercentEscape(string value)
    {
        for (int index = value.IndexOf('%', StringComparison.Ordinal);
            index != -1;
            index = value.IndexOf('%', index + 3, StringComparison.Ordinal))
        {
            char high = value[index + 1];
            char low = value[index + 2];
            if (char.IsAsciiLetterLower(high) ||
                char.IsAsciiLetterLower(low) ||
                _unreservedChars.Contains((char)((HexValue(high) << 4) | HexValue(low))))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Checks whether a URI path has a <c>.</c> or <c>..</c> segment (RFC 3986 section 6.2.2.3).</summary>
    /// <param name="path">The path, which starts with a <c>/</c>.</param>
    /// <returns><see langword="true" /> when the path has a dot segment; otherwise, <see langword="false" />.
    /// </returns>
    internal static bool HasDotSegment(string path) =>
        path.Contains("/./", StringComparison.Ordinal) ||
        path.Contains("/../", StringComparison.Ordinal) ||
        path.EndsWith("/.", StringComparison.Ordinal) ||
        path.EndsWith("/..", StringComparison.Ordinal);

    private static int HexValue(char c) =>
        c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'A' and <= 'F' => c - 'A' + 10,
            >= 'a' and <= 'f' => c - 'a' + 10,
            _ => -1
        };
}
