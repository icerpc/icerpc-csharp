// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace IceRpc.Transports.Interop
{
    public interface IEndpointDecoder
    {
        /// <summary>Decodes an endpoint encoded using the Ice 1.1 encoding.</summary>
        /// <param name="transportCode">The transport code.</param>
        /// <param name="decoder">The Ice decoder.</param>
        /// <returns>The decoded endpoint, or null if this endpoint decoder does not handle this transport code.
        /// </returns>
        EndpointRecord? DecodeEndpoint(TransportCode transportCode, IceDecoder decoder);
    }

    public interface IEndpointEncoder
    {
        void EncodeEndpoint(EndpointRecord endpoint, IceEncoder encoder);
    }

    public interface IEndpointCodex : IEndpointDecoder, IEndpointEncoder
    {
    }

    public class EndpointCodexBuilder : Dictionary<(string TransportId, TransportCode TransportCode), IEndpointCodex>
    {
        public IEndpointCodex Build() => new CompositeEndpointCodex(this);

        private class CompositeEndpointCodex : IEndpointCodex
        {
            private Dictionary<TransportCode, IEndpointDecoder> _endpointDecoders = new();
            private Dictionary<TransportId, IEndpointEncoder> _endpointEncoders = new();

            internal CompositeEndpointCodex(EndpointCodexBuilder builder)
            {
                foreach (var entry in builder)
                {
                    _endpointDecoders.Add(entry.Key.TransportCode, entry.Value);
                    _endpointEncoders.Add(entry.Key.TransportId, entry.Value);
                }
            }

            public EndpointRecord? DecodeEndpoint(TransportCode transportCode, IceDecoder decoder)
            {
                if (_endpointDecoders.TryGetValue(transportCode, out IEndpointDecoder? endpointDecoder))
                {
                    return endpointDecoder.DecodeEndpoint(transportCode, decoder);
                }
                else
                {
                    return null!;
                }
            }

            public void EncodeEndpoint(EndpointRecord endpoint, IceEncoder encoder)
            {
                if (_endpointEncoders.TryGetValue(endpoint.Transport, out IEndpointEncoder? endpointEncoder))
                {
                    endpointEncoder.EncodeEndpoint(endpoint, encoder);
                }
                else if (endpoint.Transport == TransportId.Opaque)
                {
                    (TransportCode transportCode, ReadOnlyMemory<byte> bytes) =
                        OpaqueUtils.ParseOpaqueParameters(endpoint);

                    encoder.EncodeEndpoint11(endpoint,
                                             transportCode,
                                             (encoder, _) => encoder.WriteByteSpan(bytes.Span));
                }
                else
                {
                    throw new UnknownTransportException(endpoint.Transport);
                }
            }
        }
    }

    public static class EndpointCodexBuilderExtensions
    {
        public static EndpointCodexBuilder AddSsl(this EndpointCodexBuilder builder)
        {
            builder.Add(("ssl", TransportCode.SSL), new TcpEndpointCodex());
            return builder;
        }

        public static EndpointCodexBuilder AddTcp(this EndpointCodexBuilder builder)
        {
            builder.Add(("tcp", TransportCode.TCP), new TcpEndpointCodex());
            return builder;
        }

        public static EndpointCodexBuilder AddUdp(this EndpointCodexBuilder builder)
        {
            builder.Add(("udp", TransportCode.UDP), new UdpEndpointCodex());
            return builder;
        }
    }

    public sealed class TcpEndpointCodex : IEndpointCodex
    {
        public EndpointRecord? DecodeEndpoint(TransportCode transportCode, IceDecoder decoder)
        {
            if (transportCode == TransportCode.TCP || transportCode == TransportCode.SSL)
            {
                string host = decoder.DecodeString();
                ushort port = checked((ushort)decoder.DecodeInt());
                int timeout = decoder.DecodeInt();
                bool compress = decoder.DecodeBool();

                var parameters =
                    ImmutableList.Create(new EndpointParameter("-t", timeout.ToString(CultureInfo.InvariantCulture)));
                if (compress)
                {
                    parameters = parameters.Add(new EndpointParameter("-z", ""));
                }

                return new EndpointRecord(Protocol.Ice1,
                                          transportCode == TransportCode.SSL ? "ssl" : "tcp",
                                          host,
                                          port,
                                          parameters,
                                          ImmutableList<EndpointParameter>.Empty);
            }
            return null;
        }

        public void EncodeEndpoint(EndpointRecord endpoint, IceEncoder encoder)
        {
            TransportCode transportCode = endpoint.Protocol == Protocol.Ice1 ?
                (endpoint.Transport == "ssl" ? TransportCode.SSL : TransportCode.TCP) :
                TransportCode.Any;

            encoder.EncodeEndpoint11(endpoint,
                                     transportCode,
                                     static (encoder, endpoint) =>
                                     {
                                        if (endpoint.Protocol == Protocol.Ice1)
                                        {
                                            (bool compress, int timeout) = TcpUtils.ParseTcpParameters(endpoint);

                                            encoder.EncodeString(endpoint.Host);
                                            encoder.EncodeInt(endpoint.Port);
                                            encoder.EncodeInt(timeout);
                                            encoder.EncodeBool(compress);
                                        }
                                        else
                                        {
                                            endpoint.ToEndpointData().Encode(encoder);
                                        }
                                     });
       }
    }

    public sealed class UdpEndpointCodex : IEndpointCodex
    {
        public EndpointRecord? DecodeEndpoint(TransportCode transportCode, IceDecoder decoder)
        {
            if (transportCode == TransportCode.UDP)
            {
                string host = decoder.DecodeString();
                ushort port = checked((ushort)decoder.DecodeInt());
                bool compress = decoder.DecodeBool();

                var parameters = compress ? ImmutableList.Create(new EndpointParameter("-z", "")) :
                    ImmutableList<EndpointParameter>.Empty;

                return new EndpointRecord(Protocol.Ice1,
                                          "udp",
                                          host,
                                          port,
                                          parameters,
                                          ImmutableList<EndpointParameter>.Empty);
            }
            return null;
        }

        public void EncodeEndpoint(EndpointRecord endpoint, IceEncoder encoder)
        {
            encoder.EncodeEndpoint11(endpoint,
                                     TransportCode.UDP,
                                     static (encoder, endpoint) =>
                                     {
                                        bool compress = UdpUtils.ParseUdpParameters(endpoint);
                                        encoder.EncodeString(endpoint.Host);
                                        encoder.EncodeInt(endpoint.Port);
                                        encoder.EncodeBool(compress);
                                     });
        }
    }
}
