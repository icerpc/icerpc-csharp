// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Globalization;
using System.Text;

namespace IceRpc.Internal;

/// <summary>Provides extension methods for <see cref="ServerAddress" />.</summary>
internal static class ServerAddressExtensions
{
    // The characters that RFC 3986 allows in a query, except '%' and the separators:
    // - '&' between the parameters of a server address
    // - ',' between the server addresses of an alt-server parameter
    // - '$' between the parameters of a server address in an alt-server parameter
    private static readonly SearchValues<char> _unencodedInParamValue = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~!'()*+;=:@/?");

    /// <summary>Appends the server address and all its parameters (if any) to this string builder.</summary>
    /// <param name="sb">The string builder.</param>
    /// <param name="serverAddress">The server address to append.</param>
    /// <param name="path">The path of the server address URI. Use this parameter to start building a service address
    /// URI.</param>
    /// <param name="includeScheme">When <see langword="true" />, first appends the server address protocol followed by
    /// ://.</param>
    /// <param name="paramSeparator">The character that separates parameters in the query component of the URI.</param>
    /// <returns>The string builder <paramref name="sb" />.</returns>
    internal static StringBuilder AppendServerAddress(
        this StringBuilder sb,
        ServerAddress serverAddress,
        string path = "",
        bool includeScheme = true,
        char paramSeparator = '&')
    {
        if (includeScheme)
        {
            sb.Append(serverAddress.Protocol);
            sb.Append("://");
        }

        if (serverAddress.Host.Contains(':', StringComparison.Ordinal))
        {
            sb.Append('[');
            sb.Append(serverAddress.Host);
            sb.Append(']');
        }
        else
        {
            sb.Append(serverAddress.Host);
        }

        if (serverAddress.Port != serverAddress.Protocol.DefaultPort)
        {
            sb.Append(':');
            sb.Append(serverAddress.Port.ToString(CultureInfo.InvariantCulture));
        }

        if (path.Length > 0)
        {
            sb.Append(path);
        }

        bool firstParam = true;
        if (serverAddress.Transport is string transport)
        {
            firstParam = false;
            sb.Append("?transport=").Append(transport);
        }

        foreach ((string name, string value) in serverAddress.Params)
        {
            if (firstParam)
            {
                sb.Append('?');
                firstParam = false;
            }
            else
            {
                sb.Append(paramSeparator);
            }
            sb.Append(name);
            if (value.Length > 0)
            {
                sb.Append('=');
                sb.AppendPercentEncoded(value, _unencodedInParamValue);
            }
        }
        return sb;
    }
}
