// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Web;

namespace IceRpc
{
    /// <summary>Represents a request protocol frame received by the application.</summary>
    public sealed class IncomingRequest : IncomingFrame, IDisposable
    {
        /// <inheritdoc/>
        public override IReadOnlyDictionary<int, ReadOnlyMemory<byte>> BinaryContext { get; } =
            ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <summary>The request context.</summary>
        public SortedDictionary<string, string> Context { get; }

        /// <summary>The deadline corresponds to the request's expiration time. Once the deadline is reached, the
        /// caller is no longer interested in the response and discards the request. The server-side runtime does not
        /// enforce this deadline - it's provided "for information" to the application. The Ice client runtime sets
        /// this deadline automatically using the proxy's invocation timeout and sends it with ice2 requests but not
        /// with ice1 requests. As a result, the deadline for an ice1 request is always <see cref="DateTime.MaxValue"/>
        /// on the server-side even though the invocation timeout is usually not infinite.</summary>
        public DateTime Deadline { get; }

        /// <summary>When true, the operation is idempotent.</summary>
        public bool IsIdempotent { get; }

        /// <summary><c>True</c> for oneway requests, <c>False</c> otherwise.</summary>
        public bool IsOneway => !IsBirectional;

        /// <summary>Returns <c>True</c> if the stream that received this reqest is a bidirectional stream,
        /// <c>False</c> otherwise.</summary>
        public bool IsBirectional => StreamId % 4 < 2;

        /// <summary>The operation called on the service.</summary>
        public string Operation { get; }

        /// <summary>The path of the target service.</summary>
        public string Path { get; }

        /// <inheritdoc/>
        public override Encoding PayloadEncoding { get; }

        /// <summary>The priority of this request.</summary>
        public Priority Priority { get; }

        /// <summary>The facet of the target service. ice1 only.</summary>
        public string Facet { get; } = "";

        /// <summary>The identity of the target service. ice1 only.</summary>
        public Identity Identity { get; } = Identity.Empty;

        /// <summary>Id of the stream used to create this request.</summary>
        internal long StreamId
        {
            get => _streamId ?? throw new InvalidOperationException("stream ID is not set");
            set => _streamId = value;
        }

        // The optional socket stream. The stream is non-null if there's still data to read over the stream
        // after the reading of the request frame.
        internal SocketStream? SocketStream { get; set; }

        private long? _streamId;

        /// <summary>Releases resources used by the request frame.</summary>
        public void Dispose() => SocketStream?.Release();

        /// <summary>Reads the arguments from the request and makes sure this request carries no argument or only
        /// unknown tagged arguments.</summary>
        public void ReadEmptyArgs()
        {
            if (PayloadCompressionFormat != CompressionFormat.Decompressed)
            {
                DecompressPayload();
            }

            if (SocketStream != null)
            {
                throw new InvalidDataException("stream data available for operation without stream parameter");
            }

            Payload.AsReadOnlyMemory().ReadEmptyEncapsulation(Protocol.GetEncoding());
        }

        /// <summary>Reads the arguments from a request.</summary>
        /// <paramtype name="T">The type of the arguments.</paramtype>
        /// <param name="reader">The delegate used to read the arguments.</param>
        /// <returns>The request arguments.</returns>
        public T ReadArgs<T>(InputStreamReader<T> reader)
        {
            if (PayloadCompressionFormat != CompressionFormat.Decompressed)
            {
                DecompressPayload();
            }

            if (SocketStream != null)
            {
                throw new InvalidDataException("stream data available for operation without stream parameter");
            }

            return Payload.AsReadOnlyMemory().ReadEncapsulation(Protocol.GetEncoding(),
                                                                reader,
                                                                connection: Connection,
                                                                proxyOptions: Connection.Server?.ProxyOptions);
        }

        /// <summary>Reads a single stream argument from the request.</summary>
        /// <param name="reader">The delegate used to read the argument.</param>
        /// <returns>The request argument.</returns>
        public T ReadArgs<T>(Func<SocketStream, T> reader)
        {
            if (PayloadCompressionFormat != CompressionFormat.Decompressed)
            {
                DecompressPayload();
            }

            if (SocketStream == null)
            {
                throw new InvalidDataException("no stream data available for operation with stream parameter");
            }

            Payload.AsReadOnlyMemory().ReadEmptyEncapsulation(Protocol.GetEncoding());
            T value = reader(SocketStream);
            // Clear the socket stream to ensure it's not disposed with the request frame. It's now the
            // responsibility of the stream parameter object to dispose the socket stream.
            SocketStream = null;
            return value;
        }

        /// <summary>Reads the arguments from a request. The arguments include a stream argument.</summary>
        /// <paramtype name="T">The type of the arguments.</paramtype>
        /// <param name="connection">The current connection.</param>
        /// <param name="reader">The delegate used to read the arguments.</param>
        /// <returns>The request arguments.</returns>
        public T ReadArgs<T>(Connection connection, InputStreamReaderWithStreamable<T> reader)
        {
            if (PayloadCompressionFormat != CompressionFormat.Decompressed)
            {
                DecompressPayload();
            }

            if (SocketStream == null)
            {
                throw new InvalidDataException("no stream data available for operation with stream parameter");
            }

            var istr = new InputStream(Payload.AsReadOnlyMemory(),
                                       Protocol.GetEncoding(),
                                       connection: connection,
                                       proxyOptions: connection.Server?.ProxyOptions,
                                       startEncapsulation: true);
            T value = reader(istr, SocketStream);
            // Clear the socket stream to ensure it's not disposed with the request frame. It's now the
            // responsibility of the stream parameter object to dispose the socket stream.
            SocketStream = null;
            istr.CheckEndOfBuffer(skipTaggedParams: true);
            return value;
        }

        /// <summary>Constructs an incoming request frame.</summary>
        /// <param name="protocol">The protocol of the request</param>
        /// <param name="data">The frame data as an array segment.</param>
        /// <param name="maxSize">The maximum payload size, checked during decompression.</param>
        /// <param name="socketStream">The optional socket stream. The stream is non-null if there's still data to
        /// read on the stream after the reading the request frame.</param>
        internal IncomingRequest(
            Protocol protocol,
            ArraySegment<byte> data,
            int maxSize,
            SocketStream? socketStream)
            : base(protocol, maxSize)
        {
            SocketStream = socketStream;

            var istr = new InputStream(data, Protocol.GetEncoding());

            if (Protocol == Protocol.Ice1)
            {
                var requestHeader = new Ice1RequestHeader(istr);
                Identity = requestHeader.Identity;
                Path = Identity.ToPath();
                Facet = Ice1Definitions.GetFacet(requestHeader.FacetPath);
                Operation = requestHeader.Operation;
                IsIdempotent = requestHeader.OperationMode != OperationMode.Normal;
                Context = requestHeader.Context;
                Priority = default;
                Deadline = DateTime.MaxValue;

                if (Identity.Name.Length == 0)
                {
                    throw new InvalidDataException("received request with null identity");
                }
            }
            else
            {
                int headerSize = istr.ReadSize();
                int startPos = istr.Pos;

                // We use the generated code for the header body and read the rest of the header "by hand".
                var requestHeaderBody = new Ice2RequestHeaderBody(istr);
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
                Context = requestHeaderBody.Context ?? new SortedDictionary<string, string>();

                BinaryContext = istr.ReadBinaryContext();

                if (istr.Pos - startPos != headerSize)
                {
                    throw new InvalidDataException(
                        @$"received invalid request header: expected {headerSize} bytes but read {istr.Pos - startPos
                        } bytes");
                }
            }

            if (Operation.Length == 0)
            {
                throw new InvalidDataException("received request with empty operation name");
            }

            Payload = data.Slice(istr.Pos);

            PayloadEncoding = istr.ReadEncapsulationHeader(checkFullBuffer: true).Encoding;
            if (PayloadEncoding == Encoding.V20)
            {
                PayloadCompressionFormat = istr.ReadCompressionFormat();
            }
        }

        /// <summary>Constructs an incoming request frame from an outgoing request frame. Used for colocated calls.
        /// </summary>
        /// <param name="request">The outgoing request frame.</param>
        internal IncomingRequest(OutgoingRequest request)
            : base(request.Protocol, int.MaxValue)
        {
            if (Protocol == Protocol.Ice1)
            {
                Facet = request.Facet;
                Identity = request.Identity;
            }
            Path = request.Path;

            Operation = request.Operation;
            IsIdempotent = request.IsIdempotent;
            Context = new SortedDictionary<string, string>((IDictionary<string, string>)request.Context);

            Priority = default;
            Deadline = request.Deadline;

            if (Protocol == Protocol.Ice2)
            {
                BinaryContext = request.GetBinaryContext();
            }

            PayloadEncoding = request.PayloadEncoding;

            Payload = request.Payload.AsArraySegment();
            PayloadCompressionFormat = request.PayloadCompressionFormat;
        }

        internal void RestoreActivityContext(Activity activity)
        {
            if (Protocol == Protocol.Ice1)
            {
                if (Context.TryGetValue("traceparent", out string? parentId))
                {
                    activity.SetParentId(parentId);
                    if (Context.TryGetValue("tracestate", out string? traceState))
                    {
                        activity.TraceStateString = traceState;
                    }

                    if (Context.TryGetValue("baggage", out string? baggage))
                    {
                        string[] baggageItems = baggage.Split(",", StringSplitOptions.RemoveEmptyEntries);
                        for (int i = baggageItems.Length - 1; i >= 0; i--)
                        {
                            if (NameValueHeaderValue.TryParse(baggageItems[i], out NameValueHeaderValue? baggageItem))
                            {
                                if (baggageItem.Value?.Length > 0)
                                {
                                    activity.AddBaggage(baggageItem.Name.ToString(),
                                                        HttpUtility.UrlDecode(baggageItem.Value));
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                if (BinaryContext.TryGetValue((int)BinaryContextKey.TraceContext, out ReadOnlyMemory<byte> buffer))
                {
                    // Read W3C traceparent binary encoding (1 byte version, 16 bytes trace Id, 8 bytes span Id,
                    // 1 byte flags) https://www.w3.org/TR/trace-context/#traceparent-header-field-values
                    int i = 0;
                    byte traceIdVersion = buffer.Span[i++];
                    var traceId = ActivityTraceId.CreateFromBytes(buffer.Span.Slice(i, 16));
                    i += 16;
                    var spanId = ActivitySpanId.CreateFromBytes(buffer.Span.Slice(i, 8));
                    i += 8;
                    var traceFlags = (ActivityTraceFlags)buffer.Span[i++];

                    activity.SetParentId(traceId, spanId, traceFlags);

                    // Read tracestate encoded as a string
                    var istr = new InputStream(buffer[i..], Encoding.V20);
                    activity.TraceStateString = istr.ReadString();

                    // The min element size is 2 bytes empty string is 1 byte, and null value string
                    // 1 byte for the bit sequence.
                    IEnumerable<(string key, string value)> baggage = istr.ReadSequence(
                        minElementSize: 2,
                        istr =>
                        {
                            string key = istr.ReadString();
                            string value = istr.ReadString();
                            return (key, value);
                        });

                    // Restore in reverse order to keep the order in witch the peer add baggage entries,
                    // this is important when there are duplicate keys.
                    foreach ((string key, string value) in baggage.Reverse())
                    {
                        activity.AddBaggage(key, value);
                    }
                }
            }
        }
    }
}
