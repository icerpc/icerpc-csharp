// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Web;

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

        /// <summary>The context of this request.</summary>
        public IDictionary<string, string> Context { get; set; }

        /// <summary>The deadline corresponds to the request's expiration time. Once the deadline is reached, the
        /// caller is no longer interested in the response and discards the request. This deadline is sent with ice2
        /// requests but not with ice1 requests.</summary>
        /// <remarks>The source of the cancellation token given to an invoker alongside this outgoing request is
        /// expected to enforce this deadline.</remarks>
        public DateTime Deadline { get; set; }

        /// <inheritdoc/>
        public override IReadOnlyDictionary<int, ReadOnlyMemory<byte>> InitialBinaryContext { get; } =
            ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

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
        public override IList<ArraySegment<byte>> Payload
        {
            get => _payload;
            set
            {
                if (value.Count == 0 || value[0].Count == 0)
                {
                    throw new ArgumentException("the request payload cannot be empty");
                }

                // the payload encapsulation header is always in the payload first segment
                var istr = new InputStream(value[0], Protocol.GetEncoding());
                int _ = Protocol == Protocol.Ice1 ? istr.ReadInt() : istr.ReadSize();
                var payloadEncoding = new Encoding(istr);
                CompressionFormat payloadCompressionFormat = payloadEncoding == Encoding.V11 ?
                    CompressionFormat.Decompressed : istr.ReadCompressionFormat();
                PayloadCompressionFormat = payloadCompressionFormat;
                PayloadEncoding = payloadEncoding;
                _payload = value;
                _payloadSize = -1;
            }
        }

        /// <inheritdoc/>
        public override CompressionFormat PayloadCompressionFormat { get; private protected set; }

        /// <inheritdoc/>
        public override Encoding PayloadEncoding { get; private protected set; }

        /// <inheritdoc/>
        public override int PayloadSize
        {
            get
            {
                if (_payloadSize == -1)
                {
                    _payloadSize = Payload.GetByteCount();
                }
                return _payloadSize;
            }
        }

        /// <summary>The progress provider.</summary>
        public IProgress<bool>? Progress { get; set; }

        /// <summary>The proxy that is sending this request.</summary>
        public IServicePrx Proxy { get; }

        /// <summary>The facet of the target service. ice1 only.</summary>
        internal string Facet { get; } = "";

        /// <summary>The identity of the target service. ice1 only.</summary>
        internal Identity Identity { get; private set; }

        private IList<ArraySegment<byte>> _payload;
        private int _payloadSize = -1;
        private string _path = "";

        /*
        /// <summary>Creates a new <see cref="OutgoingRequest"/> for an operation with a single stream
        /// parameter.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="proxy">A proxy to the target service. This method uses the communicator, identity, facet,
        /// encoding and context of this proxy to create the request frame.</param>
        /// <param name="operation">The operation to invoke on the target service.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="args">The argument(s) to write into the frame.</param>
        /// <param name="writer">The delegate that will send the streamable.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A new OutgoingRequestFrame.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.Performance",
            "CA1801: Review unused parameters",
            Justification = "TODO")]
        public static OutgoingRequest WithArgs<T>(
            IServicePrx proxy,
            string operation,
            Invocation? invocation,
            T args,
            Action<SocketStream, T, CancellationToken> writer,
            CancellationToken cancel = default)
        {
            OutgoingRequest request = WithEmptyArgs(proxy, operation, invocation, cancel);
            // TODO: deal with compress, format, and cancel parameters
            request.StreamDataWriter = socketStream => writer(socketStream, args, cancel);
            return request;
        }
        */

        /*
        /// <summary>Creates a new <see cref="OutgoingRequest"/> for an operation with multiple parameters where
        /// one of the parameter is a stream parameter.</summary>
        /// <typeparam name="T">The type of the operation's parameters; it's a tuple type for an operation with multiple
        /// parameters.</typeparam>
        /// <param name="proxy">A proxy to the target service.</param>
        /// <param name="operation">The operation to invoke on the target service.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="args">The argument(s) to write into the frame.</param>
        /// <param name="writer">The delegate that writes the arguments into the frame.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A new OutgoingRequestFrame.</returns>
        public static OutgoingRequest WithArgs<T>(
            IServicePrx proxy,
            string operation,
            Invocation? invocation,
            in T args,
            OutputStreamValueWriterWithStreamable<T> writer,
            CancellationToken cancel = default) where T : struct
        {
            var request = new OutgoingRequest(proxy, operation, invocation, cancel);
            var ostr = new OutputStream(proxy.Protocol.GetEncoding(),
                                        request.Payload,
                                        startAt: default,
                                        request.PayloadEncoding,
                                        invocation?.ClassFormat ?? default);
            // TODO: deal with compress, format, and cancel parameters
            request.StreamDataWriter = writer(ostr, in args, cancel);
            ostr.Finish();
            if ((invocation?.CompressRequestPayload ?? false) && proxy.Encoding == Encoding.V20)
            {
                request.CompressPayload();
            }
            return request;
        }
        */

        /// <summary>Constructs an outgoing request from the given incoming request.</summary>
        /// <param name="proxy">The proxy sending the outgoing request.</param>
        /// <param name="request">The incoming request from which to create an outgoing request.</param>
        /// <param name="forwardBinaryContext">When true (the default), the new outgoing request uses the incoming
        /// request frame's binary context as a fallback - all the entries in this binary context are added before the
        /// request is sent, except for entries previously added by interceptors.</param>
        public OutgoingRequest(
            IServicePrx proxy,
            IncomingRequest request,
            bool forwardBinaryContext = true)
            : this(proxy, request.Operation, request.Context, request.Features)
        {
            Deadline = request.Deadline;
            IsIdempotent = request.IsIdempotent;
            IsOneway = request.IsOneway;
            PayloadEncoding = request.PayloadEncoding;

            if (request.Protocol == Protocol)
            {
                Payload.Add(request.Payload);

                if (Protocol == Protocol.Ice2 && forwardBinaryContext)
                {
                    InitialBinaryContext = request.BinaryContext;
                }
            }
            else
            {
                // We forward the payload (encapsulation) after rewriting the encapsulation header. The encoded bytes
                // of the encapsulation must remain the same since we cannot transcode the encoded bytes.

                int sizeLength = request.Protocol == Protocol.Ice1 ? 4 : request.Payload[0].ReadSizeLength20();

                var ostr = new OutputStream(Protocol.GetEncoding(), Payload);
                ostr.WriteEncapsulationHeader(request.Payload.Count - sizeLength, request.PayloadEncoding);
                ostr.Finish();

                // "2" below corresponds to the encoded length of the encoding.
                if (request.Payload.Count > sizeLength + 2)
                {
                    // Add encoded bytes, not including the encapsulation header (size + encoding).
                    Payload.Add(request.Payload.Slice(sizeLength + 2));
                }
            }
        }

        /// <summary>Constructs an outgoing request.</summary>
        internal OutgoingRequest(
            IServicePrx proxy,
            string operation,
            IList<ArraySegment<byte>> args,
            DateTime deadline,
            Invocation? invocation = null,
            bool idempotent = false,
            bool oneway = false)
            : this(proxy,
                   operation,
                   invocation?.Context ?? ImmutableSortedDictionary<string, string>.Empty,
                   invocation?.RequestFeatures)
        {
            Deadline = deadline;
            IsOneway = oneway || (invocation?.IsOneway ?? false);
            IsIdempotent = idempotent || (invocation?.IsIdempotent ?? false);
            Payload = args;
            Progress = invocation?.Progress;
        }

        /// <inheritdoc/>
        internal override IncomingFrame ToIncoming() => new IncomingRequest(this);

        internal void WriteActivityContext(Activity activity)
        {
            if (activity.IdFormat != ActivityIdFormat.W3C)
            {
                throw new ArgumentException("only W3C ID format is supported with IceRpc", nameof(activity));
            }

            if (activity.Id == null)
            {
                throw new ArgumentException("invalid null activity ID", nameof(activity));
            }

            if (Protocol == Protocol.Ice1)
            {
                // For Ice1 the Activity context is write to the request Context using the standard keys
                // traceparent, tracestate and baggage.

                if (Context.IsReadOnly)
                {
                    // Upgrade to read-write copy
                    Context = new SortedDictionary<string, string>(Context);
                }

                Context["traceparent"] = activity.Id;
                if (activity.TraceStateString != null)
                {
                    Context["tracestate"] = activity.TraceStateString;
                }

                using IEnumerator<KeyValuePair<string, string?>> e = activity.Baggage.GetEnumerator();
                if (e.MoveNext())
                {
                    var baggage = new List<string>();
                    do
                    {
                        baggage.Add(new NameValueHeaderValue(HttpUtility.UrlEncode(e.Current.Key),
                                                             HttpUtility.UrlEncode(e.Current.Value)).ToString());
                    }
                    while (e.MoveNext());
                    Context["baggage"] = string.Join(',', baggage);
                }
            }
            else
            {
                // For Ice2 the activity context is written to the binary context as if it has the following Slice
                // definition
                //
                // struct BaggageEntry
                // {
                //    string key;
                //    string value;
                // }
                // sequence<BaggageEntry> Baggage;
                //
                // struct ActivityContext
                // {
                //    // ActivityID version 1 byte
                //    byte version;
                //    // ActivityTraceId 16 bytes
                //    ulong activityTraceId0;
                //    ulong activityTraceId1;
                //    // ActivitySpanId 8 bytes
                //    ulong activitySpanId
                //    // ActivityTraceFlags 1 byte
                //    byte ActivityTraceFlags;
                //    string traceStateString;
                //    Baggage baggage;
                // }

                BinaryContextOverride.Add(
                    (int)BinaryContextKey.TraceContext,
                    ostr =>
                    {
                        // W3C traceparent binary encoding (1 byte version, 16 bytes trace Id, 8 bytes span Id,
                        // 1 byte flags) https://www.w3.org/TR/trace-context/#traceparent-header-field-values
                        ostr.WriteByte(0);
                        Span<byte> buffer = stackalloc byte[16];
                        activity.TraceId.CopyTo(buffer);
                        ostr.WriteByteSpan(buffer);
                        activity.SpanId.CopyTo(buffer[0..8]);
                        ostr.WriteByteSpan(buffer[0..8]);
                        ostr.WriteByte((byte)activity.ActivityTraceFlags);

                        // Tracestate encoded as an string
                        ostr.WriteString(activity.TraceStateString ?? "");

                        // Baggage encoded as a sequence<BaggageEntry>
                        ostr.WriteSequence(activity.Baggage, (ostr, entry) =>
                        {
                            ostr.WriteString(entry.Key);
                            ostr.WriteString(entry.Value ?? "");
                        });
                    });
            }
        }

        /// <inheritdoc/>
        internal override void WriteHeader(OutputStream ostr)
        {
            Debug.Assert(ostr.Encoding == Protocol.GetEncoding());

            if (Protocol == Protocol.Ice2)
            {
                OutputStream.Position start = ostr.StartFixedLengthSize(2);
                ostr.WriteIce2RequestHeaderBody(Path,
                                                Operation,
                                                IsIdempotent,
                                                Deadline,
                                                Context);

                WriteBinaryContext(ostr);
                ostr.EndFixedLengthSize(start, 2);
            }
            else
            {
                Debug.Assert(Protocol == Protocol.Ice1);
                ostr.WriteIce1RequestHeader(Identity, Facet, Operation, IsIdempotent, Context);
            }
        }

        private OutgoingRequest(
            IServicePrx proxy,
            string operation,
            IDictionary<string, string> context,
            FeatureCollection? features)
            : base(proxy.Protocol, features)
        {
            AltEndpoints = proxy.AltEndpoints;
            Connection = proxy.Connection;
            Context = context;
            Endpoint = proxy.Endpoint;
            Proxy = proxy;

            if (Protocol == Protocol.Ice1)
            {
                Facet = proxy.Impl.Facet;
                Identity = proxy.Impl.Identity;
            }

            Operation = operation;
            Path = proxy.Path;
            PayloadEncoding = proxy.Encoding; // TODO: extract from payload instead

            _payload = new List<ArraySegment<byte>>();
        }
    }
}
