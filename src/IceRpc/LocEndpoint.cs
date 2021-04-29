// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>Describes a special endpoint that needs to be resolved with a location resolver. See
    /// <see cref="ILocationResolver"/>.</summary>
    internal sealed class LocEndpoint : Endpoint
    {
        /// <inherit-doc/>
        public override string? this[string option] =>
            option == "category" && Protocol == Protocol.Ice1 ?
                (Data.Options.Length > 0 ? Data.Options[0] : null) : base[option];

        protected internal override ushort DefaultPort => DefaultLocPort;
        protected internal override bool HasOptions => Protocol == Protocol.Ice1 || Data.Options.Length > 0;

        internal const ushort DefaultLocPort = 0;

        public override IAcceptor Acceptor(Server server) =>
            throw new NotSupportedException($"endpoint '{this}' cannot accept connections");

        // There is no Equals as it's identical to the base.

        public override Connection CreateDatagramServerConnection(Server server) =>
            throw new NotSupportedException($"endpoint '{this}' cannot accept datagram connections");

        protected internal override void AppendOptions(StringBuilder sb, char optionSeparator)
        {
            base.AppendOptions(sb, optionSeparator);

            if (Protocol == Protocol.Ice1 && Data.Options.Length > 0)
            {
                sb.Append(" -c "); // category
                sb.Append(Data.Options[0]); // can be empty (all this is temporary anyway)
            }
        }

        // InvalidOperationException because this method should never get called.
        protected internal override Task<Connection> ConnectAsync(
            OutgoingConnectionOptions options,
            ILogger logger,
            CancellationToken cancel) =>
            throw new InvalidOperationException($"cannot establish a connection to endpoint '{this}'");

        protected internal override void WriteOptions11(OutputStream ostr) =>
            Debug.Assert(false); // loc endpoints are not marshaled as endpoint with ice1/1.1

        internal static LocEndpoint Create(EndpointData data, Protocol protocol)
        {
            // Drop all options we don't understand.

            if (protocol == Protocol.Ice1 && data.Options.Length > 1)
            {
                // Well-known proxy.
                data = new EndpointData(data.Transport, data.Host, data.Port, new string[] { data.Options[0] });
            }
            else if (protocol != Protocol.Ice1 && data.Options.Length > 0)
            {
                data = new EndpointData(data.Transport, data.Host, data.Port, Array.Empty<string>());
            }
            return new(data, protocol);
        }

        internal static LocEndpoint Create(Interop.Identity identity) =>
            new(new EndpointData(Transport.Loc, identity.Name, port: 0, new string[] { identity.Category }),
                Protocol.Ice1);

        internal static LocEndpoint Create(string location, Protocol protocol) =>
            new(new EndpointData(Transport.Loc, location, port: 0, Array.Empty<string>()), protocol);

        internal static LocEndpoint ParseIce1Endpoint(
            Transport transport,
            Dictionary<string, string?> options,
            string endpointString)
        {
            Debug.Assert(transport == Transport.Loc);
            (string host, ushort port) = ParseHostAndPort(options, endpointString);

            if (options.TryGetValue("-c", out string? category))
            {
                if (category == null)
                {
                    category = "";
                }
                options.Remove("-c");
            }

            return new(new EndpointData(transport,
                                        host,
                                        port,
                                        category == null ? Array.Empty<string>() : new string[] { category }),
                        Protocol.Ice1);
        }

        internal static LocEndpoint ParseIce2Endpoint(
            Transport transport,
            string host,
            ushort port,
            Dictionary<string, string> _)
        {
            Debug.Assert(transport == Transport.Loc);
            return new(new EndpointData(transport, host, port, Array.Empty<string>()), Protocol.Ice2);
        }

        // Constructor
        private LocEndpoint(EndpointData data, Protocol protocol)
            : base(data, protocol)
        {
        }
    }
}
