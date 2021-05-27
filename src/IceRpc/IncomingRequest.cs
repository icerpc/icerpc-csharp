// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace IceRpc
{
    /// <summary>Represents a request protocol frame received by the application.</summary>
    public sealed class IncomingRequest : IncomingFrame, IDisposable
    {
        /// <summary>The request context stored in <see cref="Features"/>.</summary>
        public IDictionary<string, string> Context =>
            Features.Get<IDictionary<string, string>>() ?? ImmutableSortedDictionary<string, string>.Empty;

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

        /// <summary>When true, the operation is idempotent.</summary>
        public bool IsIdempotent { get; }

        /// <summary><c>True</c> for oneway requests, <c>False</c> otherwise.</summary>
        public bool IsOneway => !IsBidirectional;

        /// <summary>Returns <c>True</c> if the stream that received this request is a bidirectional stream,
        /// <c>False</c> otherwise.</summary>
        public bool IsBidirectional => StreamId % 4 < 2;

        /// <summary>The operation called on the service.</summary>
        public string Operation { get; }

        /// <summary>The path of the target service.</summary>
        public string Path { get; }

        /// <inheritdoc/>
        public override ArraySegment<byte> Payload
        {
            get => _payload;
            set
            {
                // reset the payload encoding and compression format values
                var istr = new InputStream(value, Protocol.GetEncoding());
                (int _, Encoding payloadEncoding) = istr.ReadEncapsulationHeader(checkFullBuffer: true);
                PayloadCompressionFormat = payloadEncoding == Encoding.V20 ?
                    istr.ReadCompressionFormat() : CompressionFormat.Decompressed;
                PayloadEncoding = payloadEncoding;
                _payload = value;
            }
        }

        /// <inheritdoc/>
        public override CompressionFormat PayloadCompressionFormat { get; private protected set; }

        /// <inheritdoc/>
        public override Encoding PayloadEncoding { get; private protected set; }

        /// <summary>The priority of this request.</summary>
        public Priority Priority { get; set; }

        /// <summary>The invoker assigned to any proxy read from the payload of this request.</summary>
        public IInvoker? ProxyInvoker { get; set; }

        /// <summary>The facet of the target service. ice1 only.</summary>
        internal string Facet { get; } = "";

        /// <summary>The identity of the target service. ice1 only.</summary>
        internal Identity Identity { get; } = Identity.Empty;

        /// <summary>Id of the stream used to create this request.</summary>
        internal long StreamId
        {
            get => _streamId ?? throw new InvalidOperationException("stream ID is not set");
            set => _streamId = value;
        }

        // The optional socket stream. The stream is non-null if there's still data to read over the stream
        // after the reading of the request frame.
        internal SocketStream? SocketStream { get; set; }

        ArraySegment<byte> _payload;
        private long? _streamId;

        /// <summary>Releases resources used by the request frame.</summary>
        public void Dispose() => SocketStream?.Release();

        /*
        /// <summary>Reads a single stream argument from the request.</summary>
        /// <param name="reader">The delegate used to read the argument.</param>
        /// <returns>The request argument.</returns>
        public T ReadArgs<T>(Func<SocketStream, T> reader)
        {
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
            if (SocketStream == null)
            {
                throw new InvalidDataException("no stream data available for operation with stream parameter");
            }

            var istr = new InputStream(Payload.AsReadOnlyMemory(),
                                       Protocol.GetEncoding(),
                                       connection: connection,
                                       invoker: connection.Server?.Invoker,
                                       startEncapsulation: true);
            T value = reader(istr, SocketStream);
            // Clear the socket stream to ensure it's not disposed with the request frame. It's now the
            // responsibility of the stream parameter object to dispose the socket stream.
            SocketStream = null;
            istr.CheckEndOfBuffer(skipTaggedParams: true);
            return value;
        }
        */

        /// <summary>Constructs an incoming request frame.</summary>
        /// <param name="protocol">The protocol of the request</param>
        /// <param name="data">The frame data as an array segment.</param>
        /// <param name="socketStream">The optional socket stream. The stream is non-null if there's still data to
        /// read on the stream after the reading the request frame.</param>
        internal IncomingRequest(
            Protocol protocol,
            ArraySegment<byte> data,
            SocketStream? socketStream)
            : base(protocol)
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
                if (requestHeader.Context.Count > 0)
                {
                    Features = new FeatureCollection();
                    Features.Set<IDictionary<string, string>>(requestHeader.Context);
                }
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

                Fields = istr.ReadFields();

                // TODO: temporary, read from fields
                if (requestHeaderBody.Context is IDictionary<string, string> context)
                {
                    Features = new FeatureCollection();
                    Features.Set<IDictionary<string, string>>(context);
                }

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

            _payload = data.Slice(istr.Pos);

            PayloadEncoding = istr.ReadEncapsulationHeader(checkFullBuffer: true).Encoding;
            if (PayloadEncoding == Encoding.V20)
            {
                PayloadCompressionFormat = istr.ReadCompressionFormat();
            }
        }

        /// <summary>Constructs an incoming request from an outgoing request. Used for colocated calls.</summary>
        /// <param name="request">The outgoing request.</param>
        internal IncomingRequest(OutgoingRequest request)
            : base(request.Protocol)
        {
            if (Protocol == Protocol.Ice1)
            {
                Facet = request.Facet;
                Identity = request.Identity;
            }
            Path = request.Path;

            Operation = request.Operation;
            IsIdempotent = request.IsIdempotent;

            if (request.Context.Count > 0)
            {
                Features = new FeatureCollection();
                Features.Set<IDictionary<string, string>>(request.Context);
            }

            Priority = default;
            Deadline = request.Deadline;

            if (Protocol == Protocol.Ice2)
            {
                Fields = request.GetFields();
            }

            PayloadEncoding = request.PayloadEncoding;

            Payload = request.Payload.AsArraySegment();
            PayloadCompressionFormat = request.PayloadCompressionFormat;
        }
    }
}
