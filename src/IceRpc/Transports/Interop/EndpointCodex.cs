// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace IceRpc.Transports.Interop
{
    public interface IEndpointDecoder
    {
        Endpoint CreateEndpoint(EndpointData data, Protocol protocol); // temporary

        /// <summary>Decodes an endpoint encoded using the Ice 1.1 encoding.</summary>
        /// <param name="transportCode">The transport code.</param>
        /// <param name="decoder">The Ice decoder.</param>
        /// <returns>The decoded endpoint, or null if this endpoint decoder does not handle this transport code.
        /// </returns>
        Endpoint? DecodeEndpoint(TransportCode transportCode, IceDecoder decoder);
    }

    public interface IEndpointEncoder
    {
        void EncodeEndpoint(Endpoint endpoint, IceEncoder encoder);
    }

    public interface IEndpointCodex : IEndpointDecoder, IEndpointEncoder
    {
    }

    public class EndpointCodexBuilder : Dictionary<(string TransportName, TransportCode TransportCode), IEndpointCodex>
    {
        public IEndpointCodex Build() => new CompositeEndpointCodex(this);

        private class CompositeEndpointCodex : IEndpointCodex
        {
            private Dictionary<TransportCode, IEndpointDecoder> _endpointDecoders = new();
            private Dictionary<string, IEndpointEncoder> _endpointEncoders = new();

            internal CompositeEndpointCodex(EndpointCodexBuilder builder)
            {
                foreach (var entry in builder)
                {
                    _endpointDecoders.Add(entry.Key.TransportCode, entry.Value);
                    _endpointEncoders.Add(entry.Key.TransportName, entry.Value);
                }
            }

            public Endpoint CreateEndpoint(EndpointData data, Protocol protocol)
            {
                if (_endpointDecoders.TryGetValue(data.TransportCode, out IEndpointDecoder? endpointDecoder))
                {
                    return endpointDecoder.CreateEndpoint(data, protocol);
                }
                else
                {
                    throw new UnknownTransportException(data.TransportCode);
                }
            }

            public Endpoint? DecodeEndpoint(TransportCode transportCode, IceDecoder decoder)
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

            public void EncodeEndpoint(Endpoint endpoint, IceEncoder encoder)
            {
                if (_endpointEncoders.TryGetValue(endpoint.TransportName, out IEndpointEncoder? endpointEncoder))
                {
                    endpointEncoder.EncodeEndpoint(endpoint, encoder);
                }
                else if (endpoint is OpaqueEndpoint) // will become TransportName == "opaque"
                {
                    encoder.EncodeEndpoint11(endpoint,
                                             endpoint.TransportCode,
                                             static (encoder, endpoint) =>
                                             {
                                                 var opaqueEndpoint = (OpaqueEndpoint)endpoint;

                                                 opaqueEndpoint.ValueEncoding.Encode(encoder);
                                                     // WriteByteSpan is not encoding-sensitive
                                                     encoder.WriteByteSpan(opaqueEndpoint.Value.Span);
                                             });
                }
                else
                {
                    throw new UnknownTransportException(endpoint.TransportCode);
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
        public Endpoint CreateEndpoint(EndpointData data, Protocol protocol) =>
            TcpEndpoint.CreateEndpoint(data, protocol);

        public Endpoint? DecodeEndpoint(TransportCode transportCode, IceDecoder decoder)
        {
            // protocol is ice1
            if (transportCode == TransportCode.TCP || transportCode == TransportCode.SSL)
            {
                return new TcpEndpoint(new EndpointData(Protocol.Ice1,
                                                        transportCode.ToString().ToLowerInvariant(),
                                                        transportCode,
                                                        host: decoder.DecodeString(),
                                                        port: checked((ushort)decoder.DecodeInt()),
                                                        ImmutableList<string>.Empty),
                                       timeout: TimeSpan.FromMilliseconds(decoder.DecodeInt()),
                                       compress: decoder.DecodeBool());
            }
            return null;
        }

        public void EncodeEndpoint(Endpoint endpoint, IceEncoder encoder)
        {
            if (endpoint is TcpEndpoint)
            {
                encoder.EncodeEndpoint11(endpoint,
                                         endpoint.TransportCode, // tcp or ssl
                                         static (encoder, endpoint) =>
                                         {
                                            encoder.Encoding.Encode(encoder); // encaps header
                                            if (endpoint.Protocol == Protocol.Ice1)
                                            {
                                                endpoint.EncodeOptions11(encoder);
                                            }
                                            else
                                            {
                                                encoder.EncodeString(endpoint.Data.Host);
                                                encoder.EncodeUShort(endpoint.Data.Port);
                                                encoder.EncodeSequence(endpoint.Data.Options,
                                                                       BasicEncodeActions.StringEncodeAction);
                                            }
                                         });

            }
            else
            {
                throw new ArgumentException("endpoint is not a tcp endpoint", nameof(endpoint));
            }
        }
    }

    public sealed class UdpEndpointCodex : IEndpointCodex
    {
        public Endpoint CreateEndpoint(EndpointData data, Protocol protocol)
        {
            if (protocol != Protocol.Ice1)
            {
                throw new ArgumentException($"cannot create UDP endpoint for protocol {protocol.GetName()}",
                                            nameof(protocol));
            }

            if (data.Options.Count > 0)
            {
                // Drop all options since we don't understand any.
                data = new EndpointData(Protocol.Ice1, "udp", data.TransportCode, data.Host, data.Port, ImmutableList<string>.Empty);
            }
            return new UdpEndpoint(data);
        }

        public Endpoint? DecodeEndpoint(TransportCode transportCode, IceDecoder decoder) =>
            new UdpEndpoint(new EndpointData(Protocol.Ice1,
                                             "udp",
                                             transportCode,
                                             host: decoder.DecodeString(),
                                             port: checked((ushort)decoder.DecodeInt()),
                                             ImmutableList<string>.Empty),
                            compress: decoder.DecodeBool());

        public void EncodeEndpoint(Endpoint endpoint, IceEncoder encoder)
        {
            if (endpoint is UdpEndpoint)
            {
                encoder.EncodeEndpoint11(endpoint,
                                         TransportCode.UDP,
                                         static (encoder, endpoint) =>
                                         {
                                            encoder.Encoding.Encode(encoder); // encaps header
                                            if (endpoint.Protocol == Protocol.Ice1)
                                            {
                                                endpoint.EncodeOptions11(encoder);
                                            }
                                            else
                                            {
                                                encoder.EncodeString(endpoint.Data.Host);
                                                encoder.EncodeUShort(endpoint.Data.Port);
                                                encoder.EncodeSequence(endpoint.Data.Options,
                                                                       BasicEncodeActions.StringEncodeAction);
                                            }
                                         });

            }
            else
            {
                throw new ArgumentException("endpoint is not a udp endpoint", nameof(endpoint));
            }
        }
    }
}
