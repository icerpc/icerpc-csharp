// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IceRpc
{
    internal sealed class WSEndpoint : TcpEndpoint
    {
        public override bool? IsSecure => Protocol == Protocol.Ice1 ? Transport == Transport.WSS : base.IsSecure;

        public override string? this[string option] => option == "resource" ? Resource : base[option];

        protected internal override bool HasOptions => Data.Options.Length > 0 || base.HasOptions;

        /// <summary>A URI specifying the resource associated with this endpoint. The value is passed as the target for
        /// GET in the WebSocket upgrade request.</summary>
        internal string Resource => Data.Options.Length > 0 ? Data.Options[0] : "/";

        // There is no Equals or GetHashCode because they are identical to the base.

        protected internal override void WriteOptions11(OutputStream ostr)
        {
            Debug.Assert(Protocol == Protocol.Ice1 && ostr.Encoding == Encoding.V11);
            base.WriteOptions11(ostr);
            ostr.WriteString(Resource);
        }

        protected internal override void AppendOptions(StringBuilder sb, char optionSeparator)
        {
            base.AppendOptions(sb, optionSeparator);
            if (Resource != "/")
            {
                if (Protocol == Protocol.Ice1)
                {
                    sb.Append(" -r ");
                    bool addQuote = Resource.IndexOf(':') != -1;
                    if (addQuote)
                    {
                        sb.Append('"');
                    }
                    sb.Append(Resource);
                    if (addQuote)
                    {
                        sb.Append('"');
                    }
                }
                else
                {
                    if (base.HasOptions)
                    {
                        sb.Append(optionSeparator);
                    }
                    sb.Append("resource=");
                    // resource must be in a URI-compatible format, with for example spaces escaped as %20.
                    sb.Append(Resource);
                }
            }
        }
        internal static new WSEndpoint CreateIce1Endpoint(Transport transport, InputStream istr)
        {
            Debug.Assert(transport == Transport.WS || transport == Transport.WSS);

            string host = istr.ReadString();
            ushort port = ReadPort(istr);
            var timeout = TimeSpan.FromMilliseconds(istr.ReadInt());
            bool compress = istr.ReadBool();
            string resource = istr.ReadString();

            string[] options = resource == "/" ? Array.Empty<string>() : new string[] { resource };

            return new WSEndpoint(new EndpointData(transport, host, port, options), timeout, compress);
        }

        internal static new WSEndpoint CreateEndpoint(EndpointData data, Protocol protocol)
        {
            if (data.Options.Length > 1)
            {
                // Drop options we don't understand
                data = new EndpointData(data.Transport, data.Host, data.Port, new string[] { data.Options[0] });
            }

            return new(data, protocol);
        }

        internal static new WSEndpoint ParseIce1Endpoint(
            Transport transport,
            Dictionary<string, string?> options,
            string endpointString)
        {
            (string host, ushort port) = ParseHostAndPort(options, endpointString);

            string resource = "/";

            if (options.TryGetValue("-r", out string? argument))
            {
                resource = argument ??
                    throw new FormatException($"no argument provided for -r option in endpoint '{endpointString}'");

                options.Remove("-r");
            }

            string[] endpointDataOptions = resource == "/" ? Array.Empty<string>() : new string[] { resource };

            return new WSEndpoint(new EndpointData(transport, host, port, endpointDataOptions),
                                  ParseTimeout(options, endpointString),
                                  ParseCompress(options, endpointString));
        }

        internal static new WSEndpoint ParseIce2Endpoint(
            Transport transport,
            string host,
            ushort port,
            Dictionary<string, string> options)
        {
            Debug.Assert(transport == Transport.WS);

            string? resource = null;
            bool? tls = null;

            if (options.TryGetValue("resource", out string? value))
            {
                // The resource value (as supplied in a URI string) must be percent-escaped with '/' separators
                // We keep it as-is, and will marshal it as-is.
                resource = value;
                options.Remove("resource");
            }
            else if (options.TryGetValue("option", out value))
            {
                // We are parsing a ice+universal endpoint
                if (value.Contains(','))
                {
                    throw new FormatException("an ice+ws endpoint accepts at most one marshaled option (resource)");
                }
                // Each option of a universal endpoint needs to be unescaped
                resource = Uri.UnescapeDataString(value);
                options.Remove("option");
            }
            else if (options.TryGetValue("tls", out value))
            {
                tls = bool.Parse(value);
                options.Remove("tls");
            }

            var data = new EndpointData(transport,
                                        host,
                                        port,
                                        resource == null ? Array.Empty<string>() : new string[] { resource });

            return new WSEndpoint(data, tls);
        }

        internal override SingleStreamSocket CreateSocket(EndPoint addr, TcpOptions options, ILogger logger) =>
            new WSSocket(base.CreateSocket(addr, options, logger));

        internal override SingleStreamSocket CreateSocket(Socket socket, ILogger logger) =>
            new WSSocket(base.CreateSocket(socket, logger));

        protected internal override Connection CreateConnection(
            MultiStreamOverSingleStreamSocket socket,
            ConnectionOptions options,
            Server? server) =>
            new WSConnection(this, socket, options, server);

        // Constructor used for ice2 parsing.
        private WSEndpoint(EndpointData data, bool? tls)
            : base(data, tls)
        {
        }

        // Constructor for ice1 parsing and ummarshaling
        private WSEndpoint(EndpointData data, TimeSpan timeout, bool compress)
            : base(data, timeout, compress)
        {
        }

        // Constructor for unmarshaling with the 2.0 encoding
        private WSEndpoint(EndpointData data, Protocol protocol)
            : base(data, protocol)
        {
        }

        // Clone constructor
        private WSEndpoint(WSEndpoint endpoint, string host, ushort port)
            : base(endpoint, host, port)
        {
        }

        private protected override IPEndpoint Clone(string host, ushort port) =>
            new WSEndpoint(this, host, port);
    }
}
