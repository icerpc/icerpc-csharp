// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Interop;
using IceRpc.Transports;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Represents an ice1 or ice2 request frame sent by the application.</summary>
    public sealed class OutgoingRequest : OutgoingFrame
    {
        /// <summary>The alternatives to <see cref="Endpoint"/>. It should be empty when Endpoint is null.</summary>
        public IEnumerable<Endpoint> AltEndpoints { get; set; } = ImmutableList<Endpoint>.Empty;

        /// <summary>The main target endpoint for this request.</summary>
        public Endpoint? Endpoint { get; set; }

        /// <summary>A list of endpoints this request does not want to establish a connection to, typically because a
        /// previous attempt asked the request not to.</summary>
        public IEnumerable<Endpoint> ExcludedEndpoints { get; set; } = ImmutableList<Endpoint>.Empty;

        /// <summary>The connection that will be used (or was used ) to send this request.</summary>
        public Connection? Connection { get; set; }

        /// <summary>The deadline corresponds to the request's expiration time. Once the deadline is reached, the
        /// caller is no longer interested in the response and discards the request. This deadline is sent with ice2
        /// requests but not with ice1 requests.</summary>
        /// <remarks>The source of the cancellation token given to an invoker alongside this outgoing request is
        /// expected to enforce this deadline.</remarks>
        public DateTime Deadline { get; set; }

        /// <summary>When true, the operation is idempotent.</summary>
        public bool IsIdempotent { get; }

        /// <summary>When true and the operation returns void, the request is sent as a oneway request. Otherwise, the
        /// request is sent as a twoway request.</summary>
        public bool IsOneway { get; set; }

        /// <summary>Indicates whether or not this request has been sent.</summary>
        /// <value>When <c>true</c>, the request was sent. When <c>false</c> the request was not sent yet.</value>
        public bool IsSent { get; set; }

        /// <summary>The operation called on the service.</summary>
        public string Operation { get; set; }

        /// <summary>The path of the target service.</summary>
        public string Path
        {
            get => _path;
            set
            {
                if (Protocol == Protocol.Ice1)
                {
                    Identity = Identity.FromPath(value);
                }
                _path = value;
            }
        }

        /// <inheritdoc/>
        public override Encoding PayloadEncoding { get; private protected set; }

        /// <summary>The proxy that is sending this request.</summary>
        public Proxy Proxy { get; }

        /// <summary>A stream parameter decompressor. Middleware or interceptors can use this property to
        /// decompress a stream return value.</summary>
        public Func<CompressionFormat, System.IO.Stream, System.IO.Stream>? StreamDecompressor { get; set; }

        /// <summary>The facet path of the target service. ice1 only.</summary>
        internal IList<string> FacetPath { get; } = ImmutableList<string>.Empty;

        /// <summary>The identity of the target service. ice1 only.</summary>
        internal Identity Identity { get; private set; }

        /// <summary>The retry policy for the request. The policy is used by the retry invoker if the request fails
        /// with a local exception. It is set by the connection code based on the context of the failure.</summary>
        internal RetryPolicy RetryPolicy { get; set; } = RetryPolicy.NoRetry;

        /// <summary>The stream used to send the request.</summary>
        internal RpcStream Stream
        {
            get => _stream ?? throw new InvalidOperationException("stream not set");
            set => _stream = value;
        }

        private string _path = "";
        private RpcStream? _stream;

        /// <summary>Constructs an outgoing request from the given incoming request.</summary>
        /// <param name="proxy">The proxy sending the outgoing request.</param>
        /// <param name="request">The incoming request from which to create an outgoing request.</param>
        /// <param name="forwardFields">When true (the default), the new outgoing request uses the incoming request's
        /// fields as defaults for its fields.</param>
        public OutgoingRequest(
            Proxy proxy,
            IncomingRequest request,
            bool forwardFields = true)
            // TODO: support stream param forwarding
            : this(proxy, request.Operation, request.Features, null)
        {
            Deadline = request.Deadline;
            IsIdempotent = request.IsIdempotent;
            IsOneway = request.IsOneway;
            PayloadEncoding = request.PayloadEncoding;

            // We forward the payload as is.
            Payload = new ReadOnlyMemory<byte>[] { request.Payload }; // TODO: temporary

            if (request.Protocol == Protocol && Protocol == Protocol.Ice2 && forwardFields)
            {
                FieldsDefaults = request.Fields;
            }
        }

        /// <summary>Constructs an outgoing request.</summary>
        internal OutgoingRequest(
            Proxy proxy,
            string operation,
            ReadOnlyMemory<ReadOnlyMemory<byte>> args,
            RpcStreamWriter? streamWriter,
            DateTime deadline,
            Invocation? invocation = null,
            bool idempotent = false,
            bool oneway = false)
            : this(proxy,
                   operation,
                   invocation?.RequestFeatures ?? FeatureCollection.Empty,
                   streamWriter)
        {
            Deadline = deadline;
            IsOneway = oneway || (invocation?.IsOneway ?? false);
            IsIdempotent = idempotent || (invocation?.IsIdempotent ?? false);
            Payload = args;
        }

        /// <inheritdoc/>
        internal override IncomingFrame ToIncoming() => new IncomingRequest(this);

        /// <inheritdoc/>
        internal override void EncodeHeader(IceEncoder encoder)
        {
            Debug.Assert(encoder.Encoding == Protocol.GetEncoding());

            IDictionary<string, string> context = Features.GetContext();

            if (Protocol == Protocol.Ice2)
            {
                IceEncoder.Position start = encoder.StartFixedLengthSize(2);

                // DateTime.MaxValue represents an infinite deadline and it is encoded as -1
                var requestHeaderBody = new Ice2RequestHeaderBody(
                    Path,
                    Operation,
                    idempotent: IsIdempotent ? true : null,
                    priority: null,
                    deadline: Deadline == DateTime.MaxValue ? -1 :
                        (long)(Deadline - DateTime.UnixEpoch).TotalMilliseconds,
                    payloadEncoding: PayloadEncoding == Ice2Definitions.Encoding ? null : PayloadEncoding.ToString());

                requestHeaderBody.Encode(encoder);

                if (FieldsDefaults.ContainsKey((int)Ice2FieldKey.Context) || context.Count > 0)
                {
                    // Encodes context
                    Fields[(int)Ice2FieldKey.Context] =
                        encoder => encoder.EncodeDictionary(context,
                                                            (encoder, value) => encoder.EncodeString(value),
                                                            (encoder, value) => encoder.EncodeString(value));
                }
                // else context remains empty (not set)

                EncodeFields(encoder);
                encoder.EncodeSize(PayloadSize);
                encoder.EndFixedLengthSize(start, 2);
            }
            else
            {
                Debug.Assert(Protocol == Protocol.Ice1);
                (byte encodingMajor, byte encodingMinor) = PayloadEncoding.ToMajorMinor();
                var requestHeader = new Ice1RequestHeader(
                    Identity,
                    FacetPath,
                    Operation,
                    IsIdempotent ? OperationMode.Idempotent : OperationMode.Normal,
                    context,
                    encapsulationSize: PayloadSize + 6,
                    encodingMajor,
                    encodingMinor);
                requestHeader.Encode(encoder);
            }
        }

        private OutgoingRequest(
            Proxy proxy,
            string operation,
            FeatureCollection features,
            RpcStreamWriter? streamWriter)
            : base(proxy.Protocol, features, streamWriter)
        {
            AltEndpoints = proxy.AltEndpoints;
            Connection = proxy.Connection;
            Endpoint = proxy.Endpoint;
            Proxy = proxy;

            if (Protocol == Protocol.Ice1)
            {
                FacetPath = proxy.FacetPath;
                Identity = proxy.Identity;
            }

            Operation = operation;
            Path = proxy.Path;
            PayloadEncoding = proxy.Encoding;
            Payload = ReadOnlyMemory<ReadOnlyMemory<byte>>.Empty;
        }
    }
}
