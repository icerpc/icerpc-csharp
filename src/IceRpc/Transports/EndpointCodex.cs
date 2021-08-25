// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Transports.Internal;
using System.Collections.Immutable;
using System.Globalization;

namespace IceRpc.Transports
{
    /// <summary>The encoding of ice1 endpoints with the Ice 1.1 encoding is transport-specific. This interface provides
    /// an abstraction to plug-in decoders for such endpoints.</summary>
    public interface IEndpointDecoder
    {
        /// <summary>Decodes an ice1 endpoint encoded using the Ice 1.1 encoding.</summary>
        /// <param name="transportCode">The transport code.</param>
        /// <param name="decoder">The Ice decoder.</param>
        /// <returns>The decoded endpoint, or null if this endpoint decoder does not handle this transport code.
        /// </returns>
        internal Endpoint? DecodeEndpoint(TransportCode transportCode, Ice11Decoder decoder);
    }

    /// <summary>The encoding of ice1 endpoints with the Ice 1.1 encoding is transport-specific. This interface provides
    /// an abstraction to plug-in encoders for such endpoints.</summary>
    public interface IEndpointEncoder
    {
        /// <summary>Encodes an ice1 endpoint with the Ice 1.1 encoding.</summary>
        /// <param name="endpoint">The ice1 endpoint to encode.</param>
        /// <param name="encoder">The Ice encoder.</param>
        internal void EncodeEndpoint(Endpoint endpoint, Ice11Encoder encoder);
    }

    /// <summary>Composes the <see cref="IEndpointEncoder"/> and <see cref="IEndpointDecoder"/> interfaces.</summary>
    public interface IEndpointCodex : IEndpointDecoder, IEndpointEncoder
    {
    }

    /// <summary>Builds a composite endpoint codex out of endpoint codexes for specific transports.</summary>
    public class EndpointCodexBuilder : Dictionary<(string TransportName, TransportCode TransportCode), IEndpointCodex>
    {
        /// <summary>Builds a new endpoint codex.</summary>
        /// <returns>The new endpoint codex.</returns>
        public IEndpointCodex Build() => new CompositeEndpointCodex(this);

        private class CompositeEndpointCodex : IEndpointCodex
        {
            private readonly IReadOnlyDictionary<TransportCode, IEndpointDecoder> _endpointDecoders;
            private readonly IReadOnlyDictionary<string, IEndpointEncoder> _endpointEncoders;

            internal CompositeEndpointCodex(EndpointCodexBuilder builder)
            {
                _endpointDecoders =
                    builder.ToDictionary(entry => entry.Key.TransportCode, entry => entry.Value as IEndpointDecoder);

                _endpointEncoders =
                    builder.ToDictionary(entry => entry.Key.TransportName, entry => entry.Value as IEndpointEncoder);
            }

            Endpoint? IEndpointDecoder.DecodeEndpoint(TransportCode transportCode, Ice11Decoder decoder) =>
                _endpointDecoders.TryGetValue(transportCode, out IEndpointDecoder? endpointDecoder) ?
                    endpointDecoder.DecodeEndpoint(transportCode, decoder) : null;

            void IEndpointEncoder.EncodeEndpoint(Endpoint endpoint, Ice11Encoder encoder)
            {
                if (_endpointEncoders.TryGetValue(endpoint.Transport, out IEndpointEncoder? endpointEncoder))
                {
                    endpointEncoder.EncodeEndpoint(endpoint, encoder);
                }
                else if (endpoint.Transport == TransportNames.Opaque)
                {
                    (TransportCode transportCode, ReadOnlyMemory<byte> bytes) = endpoint.ParseOpaqueParams();

                    encoder.EncodeEndpoint(
                        endpoint,
                        transportCode,
                        (encoder, _) => encoder.BufferWriter.WriteByteSpan(bytes.Span));
                }
                else
                {
                    throw new UnknownTransportException(endpoint.Transport, endpoint.Protocol);
                }
            }
        }
    }

    /// <summary>Extension methods for class <see cref="EndpointCodexBuilder"/>.</summary>
    public static class EndpointCodexBuilderExtensions
    {
        /// <summary>Adds the ssl endpoint codex to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <returns>The builder.</returns>
        public static EndpointCodexBuilder AddSsl(this EndpointCodexBuilder builder)
        {
            builder.Add((TransportNames.Ssl, TransportCode.SSL), new TcpEndpointCodex());
            return builder;
        }

        /// <summary>Adds the tcp endpoint codex to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <returns>The builder.</returns>
        public static EndpointCodexBuilder AddTcp(this EndpointCodexBuilder builder)
        {
            builder.Add((TransportNames.Tcp, TransportCode.TCP), new TcpEndpointCodex());
            return builder;
        }

        /// <summary>Adds the udp endpoint codex to this builder.</summary>
        /// <param name="builder">The builder being configured.</param>
        /// <returns>The builder.</returns>
        public static EndpointCodexBuilder AddUdp(this EndpointCodexBuilder builder)
        {
            builder.Add((TransportNames.Udp, TransportCode.UDP), new UdpEndpointCodex());
            return builder;
        }
    }

    /// <summary>Implements <see cref="IEndpointCodex"/> for the tcp transport.</summary>
    public sealed class TcpEndpointCodex : IEndpointCodex
    {
        Endpoint? IEndpointDecoder.DecodeEndpoint(TransportCode transportCode, Ice11Decoder decoder)
        {
            if (transportCode == TransportCode.TCP || transportCode == TransportCode.SSL)
            {
                string host = decoder.DecodeString();
                ushort port = checked((ushort)decoder.DecodeInt());
                int timeout = decoder.DecodeInt();
                bool compress = decoder.DecodeBool();

                var endpointParams = ImmutableList<EndpointParam>.Empty;

                if (timeout != EndpointParseExtensions.DefaultTcpTimeout)
                {
                    endpointParams =
                        endpointParams.Add(new EndpointParam("-t", timeout.ToString(CultureInfo.InvariantCulture)));
                }
                if (compress)
                {
                    endpointParams = endpointParams.Add(new EndpointParam("-z", ""));
                }

                return new Endpoint(Protocol.Ice1,
                                    transportCode == TransportCode.SSL ? TransportNames.Ssl : TransportNames.Tcp,
                                    host,
                                    port,
                                    endpointParams);
            }
            return null;
        }

        void IEndpointEncoder.EncodeEndpoint(Endpoint endpoint, Ice11Encoder encoder)
        {
            if (endpoint.Protocol != Protocol.Ice1)
            {
                throw new InvalidOperationException();
            }

            TransportCode transportCode =
                endpoint.Transport == TransportNames.Ssl ? TransportCode.SSL : TransportCode.TCP;

            encoder.EncodeEndpoint(
                endpoint,
                transportCode,
                static (encoder, endpoint) =>
                {
                    (bool compress, int timeout, bool? _) = endpoint.ParseTcpParams();
                    encoder.EncodeString(endpoint.Host);
                    encoder.EncodeInt(endpoint.Port);
                    encoder.EncodeInt(timeout);
                    encoder.EncodeBool(compress);
                });
        }
    }

    /// <summary>Implements <see cref="IEndpointCodex"/> for the udp transport.</summary>
    public sealed class UdpEndpointCodex : IEndpointCodex
    {
        Endpoint? IEndpointDecoder.DecodeEndpoint(TransportCode transportCode, Ice11Decoder decoder)
        {
            if (transportCode == TransportCode.UDP)
            {
                string host = decoder.DecodeString();
                ushort port = checked((ushort)decoder.DecodeInt());
                bool compress = decoder.DecodeBool();

                var endpointParams = compress ? ImmutableList.Create(new EndpointParam("-z", "")) :
                    ImmutableList<EndpointParam>.Empty;

                return new Endpoint(Protocol.Ice1,
                                    TransportNames.Udp,
                                    host,
                                    port,
                                    endpointParams);
            }
            return null;
        }

        void IEndpointEncoder.EncodeEndpoint(Endpoint endpoint, Ice11Encoder encoder)
        {
            if (endpoint.Protocol != Protocol.Ice1)
            {
                throw new InvalidOperationException();
            }

            encoder.EncodeEndpoint(
                endpoint,
                TransportCode.UDP,
                static (encoder, endpoint) =>
                {
                    bool compress = endpoint.ParseUdpParams().Compress;
                    encoder.EncodeString(endpoint.Host);
                    encoder.EncodeInt(endpoint.Port);
                    encoder.EncodeBool(compress);
                });
        }
    }
}
