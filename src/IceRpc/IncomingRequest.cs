// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Interop;
using IceRpc.Transports;
using System;
using System.Collections.Generic;
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
        public bool IsOneway => !IsBidirectional;

        /// <summary>Returns <c>true</c> if the stream that received this request is a bidirectional stream,
        /// <c>false</c> otherwise.</summary>
        public bool IsBidirectional => Stream.Id % 4 < 2;

        /// <summary>The operation called on the service.</summary>
        public string Operation { get; }

        /// <summary>The path of the target service.</summary>
        public string Path { get; }

        /// <inheritdoc/>
        public override Encoding PayloadEncoding { get; private protected set; }

        /// <summary>The priority of this request.</summary>
        public Priority Priority { get; set; }

        /// <summary>The invoker assigned to any proxy read from the payload of this request.</summary>
        public IInvoker? ProxyInvoker { get; set; }

        /// <summary>The facet path of the target service. ice1 only.</summary>
        internal IList<string> FacetPath { get; } = ImmutableList<string>.Empty;

        /// <summary>The identity of the target service. ice1 only.</summary>
        internal Identity Identity { get; } = Identity.Empty;

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
        internal IncomingRequest(Protocol protocol, ReadOnlyMemory<byte> data)
            : base(protocol)
        {
            var iceDecoder = new IceDecoder(data, Protocol.GetEncoding());

            if (Protocol == Protocol.Ice1)
            {
                var requestHeader = new Ice1RequestHeader(iceDecoder);
                Identity = requestHeader.Identity;
                Path = Identity.ToPath();
                FacetPath = requestHeader.FacetPath;
                Operation = requestHeader.Operation;
                IsIdempotent = requestHeader.OperationMode != OperationMode.Normal;
                if (requestHeader.Context.Count > 0)
                {
                    Features = new FeatureCollection();
                    Features.Set(new Context { Value = requestHeader.Context });
                }

                // The payload size is the encapsulation size less the 6 bytes of the encapsulation header.
                PayloadSize = requestHeader.EncapsulationSize - 6;
                PayloadEncoding = requestHeader.PayloadEncoding;

                Priority = default;
                Deadline = DateTime.MaxValue;

                if (Identity.Name.Length == 0)
                {
                    throw new InvalidDataException("received request with null identity");
                }
            }
            else
            {
                int headerSize = iceDecoder.DecodeSize();
                int startPos = iceDecoder.Pos;

                // We use the generated code for the header body and read the rest of the header "by hand".
                var requestHeaderBody = new Ice2RequestHeaderBody(iceDecoder);
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

                Fields = iceDecoder.DecodeFieldDictionary();

                PayloadEncoding = new Encoding(iceDecoder);
                PayloadSize = iceDecoder.DecodeSize();

                if (iceDecoder.Pos - startPos != headerSize)
                {
                    throw new InvalidDataException(
                        @$"received invalid request header: expected {headerSize} bytes but read {iceDecoder.Pos - startPos
                        } bytes");
                }

                // Decode Context from Fields and set corresponding feature.
                if (Fields.TryGetValue((int)Ice2FieldKey.Context, out ReadOnlyMemory<byte> value))
                {
                    Features = new FeatureCollection();
                    Features.Set(new Context
                    {
                        Value = value.DecodeFieldValue(iceDecoder => iceDecoder.DecodeDictionary(
                            minKeySize: 1,
                            minValueSize: 1,
                            keyDecodeFunc: BasicIceDecodeFuncs.StringIceDecodeFunc,
                            valueDecodeFunc: BasicIceDecodeFuncs.StringIceDecodeFunc))
                    });
                }
            }

            if (Operation.Length == 0)
            {
                throw new InvalidDataException("received request with empty operation name");
            }

            Payload = data[iceDecoder.Pos..];
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
            if (Protocol == Protocol.Ice1)
            {
                FacetPath = request.FacetPath;
                Identity = request.Identity;
            }
            Path = request.Path;

            Operation = request.Operation;
            IsIdempotent = request.IsIdempotent;

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
