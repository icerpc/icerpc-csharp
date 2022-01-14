// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Globalization;
using System.Text;

namespace IceRpc.Internal
{
    /// <summary>This class provides extension methods for <see cref="Endpoint"/>.</summary>
    internal static class EndpointExtensions
    {
        /// <summary>Appends the endpoint and all its parameters (if any) to this string builder.</summary>
        /// <param name="sb">The string builder.</param>
        /// <param name="endpoint">The endpoint to append.</param>
        /// <param name="path">The path of the endpoint URI. Use this parameter to start building a proxy URI.</param>
        /// <param name="includeScheme">When true, first appends the endpoint's protocol followed by ://.</param>
        /// <param name="paramSeparator">The character that separates parameters in the query component of the URI.
        /// </param>
        /// <returns>The string builder <paramref name="sb"/>.</returns>
        internal static StringBuilder AppendEndpoint(
            this StringBuilder sb,
            Endpoint endpoint,
            string path = "",
            bool includeScheme = true,
            char paramSeparator = '&')
        {
            if (includeScheme)
            {
                sb.Append(endpoint.Protocol.Name);
                sb.Append("://");
            }

            if (endpoint.Host.Contains(':', StringComparison.Ordinal))
            {
                sb.Append('[');
                sb.Append(endpoint.Host);
                sb.Append(']');
            }
            else
            {
                sb.Append(endpoint.Host);
            }

            if (endpoint.Port != UriProxyFormat.DefaultUriPort)
            {
                sb.Append(':');
                sb.Append(endpoint.Port.ToString(CultureInfo.InvariantCulture));
            }

            if (path.Length > 0)
            {
                sb.Append(path);
            }

            bool firstOption = true;
            foreach ((string name, string value) in endpoint.Params)
            {
                if (firstOption)
                {
                    sb.Append('?');
                    firstOption = false;
                }
                else
                {
                    sb.Append(paramSeparator);
                }
                sb.Append(name);
                sb.Append('=');
                sb.Append(value);
            }
            return sb;
        }
    }
}
