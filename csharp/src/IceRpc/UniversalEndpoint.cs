// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>Describes an endpoint with a transport or protocol that the associated communicator does not implement.
    /// The communicator cannot send a request to this endpoint; it can however marshal this endpoint (within a proxy)
    /// and send this proxy to another application that may know this transport. This class is used only for protocol
    /// ice2 or greater.</summary>
    internal sealed class UniversalEndpoint : Endpoint
    {
        /// <inherit-doc/>
        public override string? this[string option] =>
            option switch
            {
                "option" => Data.Options.Length > 0 ?
                                string.Join(",", Data.Options.Select(s => Uri.EscapeDataString(s))) : null,
                "transport" => TransportName,
                _ => null
            };

        public override string Scheme => "ice+universal";

        protected internal override ushort DefaultPort => DefaultUniversalPort;
        protected internal override bool HasOptions => true;

        internal const ushort DefaultUniversalPort = 0;

        public override IAcceptor Acceptor(Server server) =>
            throw new NotSupportedException($"endpoint `{this}' cannot accept connections");

        // There is no Equals as it's identical to the base.

        public override Connection CreateDatagramServerConnection(Server server) =>
            throw new NotSupportedException($"endpoint `{this}' cannot accept datagram connections");

        protected internal override void AppendOptions(StringBuilder sb, char optionSeparator)
        {
            sb.Append("transport=");
            sb.Append(TransportName);

            if (Data.Options.Length > 0)
            {
                sb.Append(optionSeparator);
                sb.Append("option=");
                sb.Append(string.Join(",", Data.Options.Select(s => Uri.EscapeDataString(s))));
            }
        }

        protected internal override Task<Connection> ConnectAsync(
            OutgoingConnectionOptions options,
            ILogger logger,
            CancellationToken cancel) =>
            throw new NotSupportedException($"cannot establish a connection to endpoint `{this}'");

        protected internal override Endpoint GetPublishedEndpoint(string publishedHost) =>
            throw new NotSupportedException($"cannot get the published endpoint for endpoint `{this}'");

        protected internal override void WriteOptions11(OutputStream ostr) =>
            Debug.Assert(false); // WriteOptions is only for ice1.

        internal static UniversalEndpoint Create(EndpointData data, Protocol protocol) => new(data, protocol);

        internal static UniversalEndpoint Parse(
            Transport transport,
            string host,
            ushort port,
            Dictionary<string, string> options,
            Protocol protocol)
        {
            string[] endpointDataOptions = Array.Empty<string>();

            if (options.TryGetValue("option", out string? value))
            {
                // Each option must be percent-escaped; we hold it in memory unescaped, and later marshal it unescaped.
                endpointDataOptions = value.Split(",").Select(s => Uri.UnescapeDataString(s)).ToArray();
                options.Remove("option");
            }

            return new UniversalEndpoint(new EndpointData(transport, host, port, endpointDataOptions), protocol);
        }

        // Constructor
        private UniversalEndpoint(EndpointData data, Protocol protocol)
            : base(data, protocol) =>
            Debug.Assert(protocol != Protocol.Ice1);
    }
}
