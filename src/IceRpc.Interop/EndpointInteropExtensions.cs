// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Globalization;
using System.Text;

namespace IceRpc
{
    /// <summary>Extension methods for class <see cref="Endpoint"/>.</summary>
    public static class EndpointInteropExtensions
    {
        /// <summary>Converts an endpoint into a string in a format compatible with ZeroC Ice.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>The string representation of this endpoint.</returns>
        public static string ToIceString(this Endpoint endpoint)
        {
            var sb = new StringBuilder();

            if (endpoint.Transport == TransportNames.Tcp)
            {
                if (endpoint.Params.Find(p => p.Name == "tls").Value == "false")
                {
                    sb.Append(TransportNames.Tcp);
                }
                else
                {
                    sb.Append(TransportNames.Ssl);
                }
            }
            else
            {
                sb.Append(endpoint.Transport);
            }

            if (endpoint.Host.Length > 0)
            {
                sb.Append(" -h ");
                bool addQuote = endpoint.Host.IndexOf(':', StringComparison.Ordinal) != -1;
                if (addQuote)
                {
                    sb.Append('"');
                }
                sb.Append(endpoint.Host);
                if (addQuote)
                {
                    sb.Append('"');
                }
            }

            // For backwards compatibility, we don't output "-p 0" for opaque endpoints.
            if (endpoint.Transport != TransportNames.Opaque || endpoint.Port != 0)
            {
                sb.Append(" -p ");
                sb.Append(endpoint.Port.ToString(CultureInfo.InvariantCulture));
            }

            foreach ((string name, string value) in endpoint.Params)
            {
                if (name != "tls")
                {
                    sb.Append(' ');

                    // Add - or -- prefix as appropriate
                    sb.Append('-');
                    if (name.Length > 1)
                    {
                        sb.Append('-');
                    }
                    sb.Append(name);
                    if (value != "true")
                    {
                        sb.Append(' ');
                        sb.Append(value);
                    }
                }
            }
            return sb.ToString();
        }
    }
}
