// Copyright (c) ZeroC, Inc.

using System.Globalization;
using System.Text;

namespace IceRpc.Internal;

/// <summary>Provides extension methods for <see cref="ServerAddress" />.</summary>
internal static class ServerAddressExtensions
{
    /// <summary>Appends the server address and all its parameters (if any) to this string builder.</summary>
    /// <param name="sb">The string builder.</param>
    /// <param name="serverAddress">The server address to append.</param>
    /// <param name="path">The path of the server address URI. Use this parameter to start building a service address
    /// URI.</param>
    /// <param name="altServer">When <see langword="true" />, appends the server address as an entry of the value of an
    /// alt-server query parameter: without the scheme, with <c>$</c> as the parameter separator, and with the brackets
    /// of an IPv6 host percent-encoded, since RFC 3986 allows brackets only around the host of the authority.</param>
    /// <returns>The string builder <paramref name="sb" />.</returns>
    internal static StringBuilder AppendServerAddress(
        this StringBuilder sb,
        ServerAddress serverAddress,
        string path = "",
        bool altServer = false)
    {
        if (!altServer)
        {
            sb.Append(serverAddress.Protocol);
            sb.Append("://");
        }

        if (serverAddress.Host.Contains(':', StringComparison.Ordinal))
        {
            sb.Append(altServer ? "%5B" : "[");
            sb.Append(serverAddress.Host);
            sb.Append(altServer ? "%5D" : "]");
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
                sb.Append(altServer ? '$' : '&');
            }
            sb.Append(name);
            if (value.Length > 0)
            {
                sb.Append('=');
                sb.Append(value);
            }
        }
        return sb;
    }
}
