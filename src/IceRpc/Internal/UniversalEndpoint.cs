// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace IceRpc.Internal
{
    /// <summary>Describes an endpoint with a transport or protocol that has not been registered with the IceRPC
    /// runtime. The IceRPC runtime cannot send a request to this endpoint; it can however marshal this endpoint
    /// (within a proxy) and send this proxy to another application that may know this transport. This class is used
    /// only for protocol ice2 or greater.</summary>
    internal sealed class UniversalEndpoint : Endpoint
    {
        /// <inherit-doc/>
        public override string? this[string option] =>
            option switch
            {
                "option" => Data.Options.Count > 0 ?
                                string.Join(",", Data.Options.Select(s => Uri.EscapeDataString(s))) : null,
                "transport" => TransportName,
                _ => null
            };

        public override string Scheme => "ice+universal";

        protected internal override bool HasOptions => true;

        protected internal override void AppendOptions(StringBuilder sb, char optionSeparator)
        {
            sb.Append("transport=");
            sb.Append(TransportName);

            if (Protocol != Protocol.Ice2)
            {
                sb.Append(optionSeparator);
                sb.Append("protocol=");
                sb.Append(Protocol.GetName());
            }

            if (Data.Options.Count > 0)
            {
                sb.Append(optionSeparator);
                sb.Append("option=");
                sb.Append(string.Join(",", Data.Options.Select(s => Uri.EscapeDataString(s))));
            }
        }

        protected internal override void EncodeOptions11(IceEncoder encoder) =>
            Debug.Assert(false); // WriteOptions is only for ice1.

        internal static UniversalEndpoint Create(EndpointData data, Protocol protocol) => new(data, protocol);

        internal static UniversalEndpoint Parse(string host, ushort port, Dictionary<string, string> options)
        {
            TransportCode transport;
            if (options.TryGetValue("transport", out string? value))
            {
                // Enumerator names are only used for "well-known" transports.
                transport = Enum.Parse<TransportCode>(value, ignoreCase: true);
                options.Remove("transport");
            }
            else
            {
                throw new FormatException("ice+universal endpoint does not have required 'transport' option");
            }

            Protocol protocol = Protocol.Ice2;
            if (options.TryGetValue("protocol", out value))
            {
                protocol = ProtocolExtensions.Parse(value);
                if (protocol == Protocol.Ice1)
                {
                    throw new FormatException("ice+universal does not support protocol ice1");
                }
                options.Remove("protocol");
            }

            var endpointDataOptions = ImmutableList<string>.Empty;
            if (options.TryGetValue("option", out value))
            {
                // Each option must be percent-escaped; we hold it in memory unescaped, and later marshal it unescaped.
                endpointDataOptions = value.Split(",").Select(s => Uri.UnescapeDataString(s)).ToImmutableList();
                options.Remove("option");
            }

            return new UniversalEndpoint(new EndpointData(protocol, "universal", transport, host, port, endpointDataOptions), protocol);
        }

        // Constructor
        private UniversalEndpoint(EndpointData data, Protocol protocol)
            : base(data, protocol) =>
            Debug.Assert(protocol != Protocol.Ice1);
    }
}
