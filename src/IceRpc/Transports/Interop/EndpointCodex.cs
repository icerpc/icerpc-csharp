// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using System;
using System.Collections.Generic;

namespace IceRpc.Transports.Interop
{
    public interface IEndpointDecoder
    {
        Endpoint DecodeEndpoint(TransportCode transportCode, IceDecoder decoder);
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
            private Dictionary<TransportCode, IEndpointEncoder> _endpointEncoders = new(); // temporary, should use name

            internal CompositeEndpointCodex(EndpointCodexBuilder builder)
            {
                foreach (var entry in builder)
                {
                    _endpointDecoders.Add(entry.Key.TransportCode, entry.Value);
                    _endpointEncoders.Add(entry.Key.TransportCode, entry.Value);
                }
            }

            public Endpoint DecodeEndpoint(TransportCode transportCode, IceDecoder decoder)
            {
                if (_endpointDecoders.TryGetValue(transportCode, out IEndpointDecoder? endpointDecoder))
                {
                    return endpointDecoder.DecodeEndpoint(transportCode, decoder);
                }
                else
                {
                    return null!;
                    /*
                    return OpaqueEndpoint.Create(transportCode,
                                                 decoder.Encoding,
                                                 decoder.ReadRemainingBytes());
                    */

                }
            }

            public void EncodeEndpoint(Endpoint endpoint, IceEncoder encoder)
            {
                if (_endpointEncoders.TryGetValue(endpoint.TransportCode, out IEndpointEncoder? endpointEncoder))
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
        public Endpoint DecodeEndpoint(TransportCode transportCode, IceDecoder decoder)
        {
            return null!;
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
        public Endpoint DecodeEndpoint(TransportCode transportCode, IceDecoder decoder)
        {
            return null!;
        }

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
