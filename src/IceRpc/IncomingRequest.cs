// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Transports;
using System.Collections.Immutable;

namespace IceRpc
{
    /// <summary>Represents a request protocol frame received by the application.</summary>
    public sealed class IncomingRequest : IncomingFrame
    {
        /// <summary>The deadline corresponds to the request's expiration time. Once the deadline is reached, the
        /// caller is no longer interested in the response and discards the request. The server-side runtime does not
        /// enforce this deadline - it's provided "for information" to the application. The Ice client runtime sets
        /// this deadline automatically using the proxy's invocation timeout and sends it with ice2 requests but not
        /// with ice1 requests. As a result, the deadline for an ice1 request is always <see cref="DateTime.MaxValue"/>
        /// on the server-side even though the invocation timeout is usually not infinite.</summary>
        public DateTime Deadline { get; }

        /// <inheritdoc/>
        public override IReadOnlyDictionary<int, ReadOnlyMemory<byte>> Fields { get; } =
            ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <summary>When <c>true</c>, the operation is idempotent.</summary>
        public bool IsIdempotent { get; }

        /// <summary><c>True</c> for oneway requests, <c>false</c> otherwise.</summary>
        public bool IsOneway { get; }

        /// <summary>The operation called on the service.</summary>
        public string Operation { get; }

        /// <summary>The path of the target service.</summary>
        public string Path { get; }

        /// <inheritdoc/>
        public override Encoding PayloadEncoding { get; }

        /// <summary>The priority of this request.</summary>
        public Priority Priority { get; set; }

        /// <summary>The invoker assigned to any proxy read from the payload of this request.</summary>
        public IInvoker? ProxyInvoker { get; set; }

        /// <summary>A stream parameter decompressor. Middleware can use this property to decompress a stream
        /// parameter.</summary>
        public Func<CompressionFormat, System.IO.Stream, System.IO.Stream>? StreamDecompressor { get; set; }

        /// <summary>The stream used to receive the request.</summary>
        internal RpcStream Stream
        {
            get => _stream ?? throw new InvalidOperationException("stream not set");
            set => _stream = value;
        }

        private RpcStream? _stream;

        /// <summary>Constructs an incoming request frame.</summary>
        /// <param name="protocol">The protocol of the request</param>
        /// <param name="data">The frame data.</param>
        /// <param name="isOneway">Specifies if the request is a oneway request.</param>
        internal IncomingRequest(Protocol protocol, ReadOnlyMemory<byte> data, bool isOneway)
            : base(protocol)
        {
            int payloadStart;

            if (Protocol == Protocol.Ice1)
            {
                var decoder = new Ice11Decoder(data);
                var requestHeader = new Ice1RequestHeader(decoder);
                if (requestHeader.IdentityAndFacet.Identity.Name.Length == 0)
                {
                    throw new InvalidDataException("received ice1 request with empty identity name");
                }

                Path = requestHeader.IdentityAndFacet.ToPath();
                Operation = requestHeader.Operation;
                IsIdempotent = requestHeader.OperationMode != OperationMode.Normal;
                if (requestHeader.Context.Count > 0)
                {
                    Features = new FeatureCollection();
                    Features.Set(new Context { Value = requestHeader.Context });
                }

                // The payload size is the encapsulation size less the 6 bytes of the encapsulation header.
                PayloadSize = requestHeader.EncapsulationSize - 6;
                PayloadEncoding =
                    Encoding.FromMajorMinor(requestHeader.PayloadEncodingMajor, requestHeader.PayloadEncodingMinor);

                Priority = default;
                Deadline = DateTime.MaxValue;
                payloadStart = decoder.Pos;
            }
            else
            {
                var decoder = new Ice20Decoder(data);
                int headerSize = decoder.DecodeSize();
                int startPos = decoder.Pos;

                // We use the generated code for the header body and read the rest of the header "by hand".
                var requestHeaderBody = new Ice2RequestHeaderBody(decoder);
                Path = requestHeaderBody.Path;
                Operation = requestHeaderBody.Operation;
                IsIdempotent = requestHeaderBody.Idempotent ?? false;
                Priority = requestHeaderBody.Priority ?? default;
                if (requestHeaderBody.Deadline < -1 || requestHeaderBody.Deadline == 0)
                {
                    throw new InvalidDataException($"received invalid deadline value {requestHeaderBody.Deadline}");
                }
                // The infinite deadline is encoded as -1 and converted to DateTime.MaxValue
                Deadline = requestHeaderBody.Deadline == -1 ?
                    DateTime.MaxValue : DateTime.UnixEpoch + TimeSpan.FromMilliseconds(requestHeaderBody.Deadline);

                PayloadEncoding = requestHeaderBody.PayloadEncoding is string payloadEncoding ?
                    Encoding.FromString(payloadEncoding) : Ice2Definitions.Encoding;

                Fields = decoder.DecodeFieldDictionary();
                PayloadSize = decoder.DecodeSize();

                if (decoder.Pos - startPos != headerSize)
                {
                    throw new InvalidDataException(
                        @$"received invalid request header: expected {headerSize} bytes but read {decoder.Pos - startPos
                        } bytes");
                }

                // Decode Context from Fields and set corresponding feature.
                if (Fields.TryGetValue((int)Ice2FieldKey.Context, out ReadOnlyMemory<byte> value))
                {
                    Features = new FeatureCollection();
                    Features.Set(new Context
                    {
                        Value = Ice20Decoder.DecodeFieldValue(
                            value,
                            decoder => decoder.DecodeDictionary(
                                minKeySize: 1,
                                minValueSize: 1,
                                keyDecodeFunc: decoder => decoder.DecodeString(),
                                valueDecodeFunc: decoder => decoder.DecodeString()))
                    });
                }
                payloadStart = decoder.Pos;
            }

            if (Operation.Length == 0)
            {
                throw new InvalidDataException("received request with empty operation name");
            }

            IsOneway = isOneway;
            Payload = data[payloadStart..];
            if (PayloadSize != Payload.Length)
            {
                throw new InvalidDataException(
                    $"request payload size mismatch: expected {PayloadSize} bytes, read {Payload.Length} bytes");
            }
        }

        /// <summary>Constructs an incoming request from an outgoing request. Used for colocated calls.</summary>
        /// <param name="request">The outgoing request.</param>
        internal IncomingRequest(OutgoingRequest request)
            : base(request.Protocol)
        {
            Path = request.Path;

            Operation = request.Operation;
            IsIdempotent = request.IsIdempotent;
            IsOneway = request.IsOneway;

            if (Protocol == Protocol.Ice2)
            {
                Fields = request.GetAllFields();
            }

            // For coloc calls, the context (and more generally the header) is not marshaled so we need to copy the
            // context from the request features.
            IDictionary<string, string>? context = request.Features.Get<Context>()?.Value;

            if (context?.Count > 0)
            {
                Features = new FeatureCollection();
                Features.Set(new Context { Value = context.ToImmutableSortedDictionary() }); // clone context value
            }

            Priority = default;
            Deadline = request.Deadline;
            PayloadEncoding = request.PayloadEncoding;

            Payload = request.Payload.ToSingleBuffer();
        }
    }
}
