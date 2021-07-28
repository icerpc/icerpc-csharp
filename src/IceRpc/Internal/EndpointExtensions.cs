// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace IceRpc.Internal
{
    /// <summary>This class provides extension methods for <see cref="Endpoint"/>.</summary>
    internal static class EndpointExtensions
    {
        /// <summary>Appends the endpoint and all its parameters (if any) to this string builder. This method is never
        /// called for ice1 endpoints.</summary>
        /// <param name="sb">The string builder.</param>
        /// <param name="endpoint">The endpoint to append.</param>
        /// <param name="path">The path of the endpoint URI. Use this parameter to start building a proxy URI.</param>
        /// <param name="includeScheme">When true, first appends the endpoint's scheme followed by ://.</param>
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
            Debug.Assert(endpoint.Protocol != Protocol.Ice1); // we never generate URIs for the ice1 protocol

            if (includeScheme)
            {
                sb.Append("ice+");
                sb.Append(endpoint.Transport);
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

            if (endpoint.Port != IceUriParser.DefaultUriPort)
            {
                sb.Append(':');
                sb.Append(endpoint.Port.ToString(CultureInfo.InvariantCulture));
            }

            if (path.Length > 0)
            {
                sb.Append(path);
            }

            bool firstOption = true;

            if (endpoint.Protocol != Protocol.Ice2)
            {
                AppendQueryOption();
                sb.Append("protocol=");
                sb.Append(endpoint.Protocol.GetName());
            }
            foreach ((string name, string value) in endpoint.ExternalParams)
            {
                AppendQueryOption();
                sb.Append(name);
                sb.Append('=');
                sb.Append(value);
            }
            foreach ((string name, string value) in endpoint.LocalParams)
            {
                AppendQueryOption();
                sb.Append(name);
                sb.Append('=');
                sb.Append(value);
            }
            return sb;

            void AppendQueryOption()
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
            }
        }

        /// <summary>Converts this endpoint into an endpoint data. This method is called when encoding an endpoint.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>An endpoint data with all the properties of this endpoint except
        /// <see cref="Endpoint.LocalParams"/>.</returns>
        internal static EndpointData ToEndpointData(this Endpoint endpoint) =>
            new(endpoint.Protocol, endpoint.Transport, endpoint.Host, endpoint.Port, endpoint.ExternalParams);
    }

    /// <summary>This class provides extension methods for <see cref="EndpointData"/>.</summary>
    internal static class EndpointDataExtensions
    {
        /// <summary>Converts an endpoint data into an endpoint. This method is used when decoding an endpoint.
        /// </summary>
        /// <param name="endpointData">The endpoint data struct.</param>
        /// <returns>The new endpoint.</returns>
        internal static Endpoint ToEndpoint(this in EndpointData endpointData) =>
            new(endpointData.Protocol,
                string.IsInterned(endpointData.Transport) ?? endpointData.Transport,
                endpointData.Host,
                endpointData.Port,
                endpointData.Params.ToImmutableList(),
                ImmutableList<EndpointParam>.Empty);
    }
}
