// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Globalization;
using System.Text;

namespace IceRpc.Internal;

/// <summary>Percent-encodes the components of a service address or server address URI. Each method encodes the UTF-8
/// bytes of every character that RFC 3986 does not allow in the component, as <c>%XX</c> with uppercase hexadecimal
/// digits.</summary>
internal static class PercentEncoding
{
    // The pchar characters of RFC 3986, without '%'.
    private const string PChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~!$&'()*+,;=:@";

    private static readonly SearchValues<char> _unencodedInFragment = SearchValues.Create(PChars + "/?");

    // The characters that RFC 3986 allows in a query, except '%' and the separators:
    // - '&' between the parameters of a server address or service address
    // - ',' between the server addresses of an alt-server parameter
    // - '$' between the parameters of a server address in an alt-server parameter
    private static readonly SearchValues<char> _unencodedInParamValue = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~!'()*+;=:@/?");

    private static readonly SearchValues<char> _unencodedInPath = SearchValues.Create(PChars + "/");

    /// <summary>Percent-encodes a fragment.</summary>
    internal static string EncodeFragment(string fragment) => Encode(fragment, _unencodedInFragment);

    /// <summary>Percent-encodes the value of a query parameter.</summary>
    internal static string EncodeParamValue(string value) => Encode(value, _unencodedInParamValue);

    /// <summary>Percent-encodes a path. The <c>/</c> characters of the path are not encoded.</summary>
    internal static string EncodePath(string path) => Encode(path, _unencodedInPath);

    private static string Encode(string value, SearchValues<char> unencodedChars)
    {
        if (value.AsSpan().IndexOfAnyExcept(unencodedChars) == -1)
        {
            return value;
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        var sb = new StringBuilder(utf8.Length + 8);
        foreach (byte b in utf8)
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
        return sb.ToString();
    }
}
