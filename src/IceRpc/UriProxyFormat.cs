// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Text;

namespace IceRpc
{
    /// <summary>The default proxy format using URIs.</summary>
    public class UriProxyFormat : IProxyFormat
    {
        /// <summary>The only instance of UriProxyFormat.</summary>
        public static UriProxyFormat Instance { get; } = new();

        /// <summary>Parses a string into a proxy.</summary>
        /// <param name="s">The string to parse. It must be either an absolute path or a URI string (with a scheme).
        /// </param>
        /// <param name="invoker">The invoker.</param>
        /// <returns>The new proxy.</returns>
        /// <exception cref="FormatException">Thrown when <paramref name="s"/> is not in the correct format.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="invoker"/> is not null and
        /// <paramref name="s"/> is a path or a URI with a non-supported protocol.</exception>
        public Proxy Parse(string s, IInvoker? invoker = null)
        {
            Proxy proxy;

            try
            {
                proxy = s.StartsWith('/') ? Proxy.FromPath(s) : new Proxy(new Uri(s, UriKind.Absolute));
            }
            catch (ArgumentException ex)
            {
                throw new FormatException($"cannot parse URI '{s}'", ex);
            }

            if (invoker != null)
            {
                try
                {
                    proxy.Invoker = invoker;
                }
                catch (InvalidOperationException ex)
                {
                    throw new ArgumentException($"cannot set invoker on proxy '{proxy}'", ex);
                }
            }

            return proxy;
        }

        /// <inheritdoc/>
        public string ToString(Proxy proxy)
        {
            if (proxy.Protocol == Protocol.Relative)
            {
                return proxy.Path;
            }
            else if (proxy.OriginalUri is Uri uri)
            {
                return uri.ToString();
            }

            // else, construct a string with a string builder.

            var sb = new StringBuilder();
            bool firstOption = true;

            if (proxy.Endpoint is Endpoint endpoint)
            {
                sb.AppendEndpoint(endpoint, proxy.Path);
                firstOption = endpoint.Params.Count == 0;
            }
            else
            {
                sb.Append(proxy.Protocol);
                sb.Append(':');
                sb.Append(proxy.Path);
            }

            // TODO: remove
            if (proxy.Encoding != proxy.Protocol.SliceEncoding)
            {
                StartQueryOption(sb, ref firstOption);
                sb.Append("encoding=");
                sb.Append(proxy.Encoding);
            }

            if (proxy.AltEndpoints.Count > 0)
            {
                StartQueryOption(sb, ref firstOption);
                sb.Append("alt-endpoint=");
                for (int i = 0; i < proxy.AltEndpoints.Count; ++i)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.AppendEndpoint(proxy.AltEndpoints[i], path: "", includeScheme: false, paramSeparator: '$');
                }
            }

            foreach ((string name, string value) in proxy.Params)
            {
                StartQueryOption(sb, ref firstOption);
                sb.Append(name);
                sb.Append('=');
                sb.Append(value);
            }

            if (proxy.Fragment.Length > 0)
            {
                sb.Append('#');
                sb.Append(proxy.Fragment);
            }

            return sb.ToString();

            static void StartQueryOption(StringBuilder sb, ref bool firstOption)
            {
                if (firstOption)
                {
                    sb.Append('?');
                    firstOption = false;
                }
                else
                {
                    sb.Append('&');
                }
            }
        }

        private UriProxyFormat()
        {
           // ensures we have a singleton
        }
    }
}
