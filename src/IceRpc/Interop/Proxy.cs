// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Text;

namespace IceRpc.Interop
{
    /// <summary>Interop.Proxy provides extension methods for IServicePrx for interop with ZeroC Ice.</summary>
    public static class Proxy
    {
        /// <summary>Converts a proxy into a string, using the format specified by ToStringMode.</summary>
        /// <param name="proxy">The service proxy.</param>
        /// <param name="mode">Specifies how non-printable ASCII characters are escaped in the resulting string. See
        /// <see cref="ToStringMode"/>.</param>
        /// <returns>The string representation of this proxy.</returns>
        public static string ToString(this IServicePrx proxy, ToStringMode mode)
        {
            if (proxy.Protocol != Protocol.Ice1)
            {
                return proxy.ToString()!;
            }

            ServicePrx impl = proxy.Impl;

            var sb = new StringBuilder();

            // If the encoded identity string contains characters which the reference parser uses as separators,
            // then we enclose the identity string in quotes.
            string id = proxy.GetIdentity().ToString(mode);
            if (StringUtil.FindFirstOf(id, " :@") != -1)
            {
                sb.Append('"');
                sb.Append(id);
                sb.Append('"');
            }
            else
            {
                sb.Append(id);
            }

            string facet = proxy.GetFacet();
            if (facet.Length > 0)
            {
                // If the encoded facet string contains characters which the reference parser uses as separators,
                // then we enclose the facet string in quotes.
                sb.Append(" -f ");
                string fs = StringUtil.EscapeString(facet, mode);
                if (StringUtil.FindFirstOf(fs, " :@") != -1)
                {
                    sb.Append('"');
                    sb.Append(fs);
                    sb.Append('"');
                }
                else
                {
                    sb.Append(fs);
                }
            }

            if (proxy.IsOneway)
            {
                if (impl.ParsedEndpoint?.IsDatagram ?? false)
                {
                    sb.Append(" -d");
                }
                else
                {
                    sb.Append(" -o");
                }
            }
            else
            {
                sb.Append(" -t");
            }

            // Always print the encoding version to ensure a stringified proxy will convert back to a proxy with the
            // same encoding with StringToProxy. (Only needed for backwards compatibility).
            sb.Append(" -e ");
            sb.Append(proxy.Encoding.ToString());

            if (impl.IsIndirect)
            {
                if (!impl.IsWellKnown)
                {
                    string adapterId = impl.ParsedEndpoint!.Host;

                    sb.Append(" @ ");

                    // If the encoded adapter ID contains characters which the proxy parser uses as separators, then
                    // we enclose the adapter ID string in double quotes.
                    adapterId = StringUtil.EscapeString(adapterId, mode);
                    if (StringUtil.FindFirstOf(adapterId, " :@") != -1)
                    {
                        sb.Append('"');
                        sb.Append(adapterId);
                        sb.Append('"');
                    }
                    else
                    {
                        sb.Append(adapterId);
                    }
                }
            }
            else
            {
                if (impl.ParsedEndpoint != null)
                {
                    sb.Append(':');
                    sb.Append(impl.ParsedEndpoint);
                }
                foreach (Endpoint e in impl.ParsedAltEndpoints)
                {
                    sb.Append(':');
                    sb.Append(e);
                }
            }
            return sb.ToString();
        }
    }
}
