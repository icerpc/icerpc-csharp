// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace IceRpc.Internal
{
    /// <summary>Describes an endpoint with a transport or protocol that has not been registered with the IceRPC
    /// runtime. The IceRPC runtime cannot send a request to this endpoint; it can however marshal this endpoint
    /// (within a proxy) and send this proxy to another application that may know this transport. This class is used
    /// only with the ice1 protocol.</summary>
    internal sealed class OpaqueEndpoint : Endpoint
    {
        /// <inherit-doc/>
        public override string? this[string option] =>
            option switch
            {
                "transport" => ((short)TransportCode).ToString(CultureInfo.InvariantCulture),
                "value" => Value.IsEmpty ? null : Convert.ToBase64String(Value.Span),
                "value-encoding" => ValueEncoding.ToString(),
                _ => null,
            };

        public override string Scheme => "opaque";

        protected internal override bool HasOptions => true;

        internal ReadOnlyMemory<byte> Value { get; }

        internal Encoding ValueEncoding { get; }

        protected internal override void AppendOptions(StringBuilder sb, char optionSeparator)
        {
            sb.Append(" -t ");
            sb.Append(((short)TransportCode).ToString(CultureInfo.InvariantCulture));

            sb.Append(" -e ");
            sb.Append(ValueEncoding.ToString());
            if (!Value.IsEmpty)
            {
                sb.Append(" -v ");
                sb.Append(Convert.ToBase64String(Value.Span));
            }
        }

        public override bool Equals(Endpoint? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return other is OpaqueEndpoint opaqueEndpoint &&
                ValueEncoding == opaqueEndpoint.ValueEncoding &&
                Value.Span.SequenceEqual(opaqueEndpoint.Value.Span) &&
                base.Equals(other);
        }

        protected internal override void EncodeOptions11(IceEncoder encoder)
        {
            Debug.Assert(false);
            throw new NotImplementedException("cannot write the options of an opaque endpoint");
        }

        internal static OpaqueEndpoint Create(
            TransportCode transport,
            Encoding valueEncoding,
            ReadOnlyMemory<byte> value) =>
            new(new EndpointData(Protocol.Ice1, "opaque", transport, host: "", port: 0, ImmutableList<string>.Empty), valueEncoding, value);

        internal static OpaqueEndpoint Parse(Dictionary<string, string?> options, string endpointString)
        {
            TransportCode transport;

            if (options.TryGetValue("-t", out string? argument))
            {
                if (argument == null)
                {
                    throw new FormatException($"no argument provided for -t option in endpoint '{endpointString}'");
                }
                short t;
                try
                {
                    t = short.Parse(argument, CultureInfo.InvariantCulture);
                }
                catch (FormatException ex)
                {
                    throw new FormatException(
                        $"invalid transport value '{argument}' in endpoint '{endpointString}'", ex);
                }

                if (t < 0)
                {
                    throw new FormatException(
                        $"transport value '{argument}' out of range in endpoint '{endpointString}'");
                }

                transport = (TransportCode)t;
                options.Remove("-t");
            }
            else
            {
                throw new FormatException($"no -t option in endpoint '{endpointString}'");
            }

            Encoding valueEncoding;

            if (options.TryGetValue("-e", out argument))
            {
                if (argument == null)
                {
                    throw new FormatException($"no argument provided for -e option in endpoint '{endpointString}'");
                }
                try
                {
                    valueEncoding = Encoding.Parse(argument);
                }
                catch (FormatException ex)
                {
                    throw new FormatException($"invalid encoding version '{argument}' in endpoint '{endpointString}'",
                        ex);
                }
                options.Remove("-e");
            }
            else
            {
                valueEncoding = Encoding.V11;
            }

            ReadOnlyMemory<byte> value;

            if (options.TryGetValue("-v", out argument))
            {
                if (argument == null)
                {
                    throw new FormatException($"no argument provided for -v option in endpoint '{endpointString}'");
                }

                try
                {
                    value = Convert.FromBase64String(argument);
                }
                catch (FormatException ex)
                {
                    throw new FormatException($"invalid Base64 input in endpoint '{endpointString}'", ex);
                }
                options.Remove("-v");
            }
            else
            {
                throw new FormatException($"no -v option in endpoint '{endpointString}'");
            }

            return Create(transport, valueEncoding, value);
        }

        private OpaqueEndpoint(
            EndpointData data,
            Encoding valueEncoding,
            ReadOnlyMemory<byte> value)
            : base(data, Protocol.Ice1)
        {
            ValueEncoding = valueEncoding;
            Value = value;
        }
    }
}
