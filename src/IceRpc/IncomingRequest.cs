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
        public DateTime Deadline { get; init; }

        /// <summary>When <c>true</c>, the operation is idempotent.</summary>
        public bool IsIdempotent { get; init; }

        /// <summary><c>True</c> for oneway requests, <c>false</c> otherwise.</summary>
        public bool IsOneway { get; init; }

        /// <summary>The operation called on the service.</summary>
        public string Operation { get; init; } = "";

        /// <summary>The path of the target service.</summary>
        public string Path { get; init; } = "";

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

        /// <summary>Create an outgoing request from this incoming request. The outgoing request is constructed
        /// to be forwarded with the given proxy.</summary>
        /// <param name="proxy">The proxy used to send to the outgoing request.</param>
        /// <returns>The outgoing request to be forwarded.</returns>
        public OutgoingRequest ToOutgoingRequest(Proxy proxy)
        {
            IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields;
            if (proxy.Protocol == Protocol && proxy.Protocol == Protocol.Ice2)
            {
                fields = Fields;
            }
            else
            {
                fields = ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;
            }

            // TODO: forward stream parameters

            return new OutgoingRequest
            {
                AltEndpoints = proxy.AltEndpoints,
                Connection = Connection,
                Deadline = Deadline,
                Endpoint = proxy.Endpoint,
                Features = Features,
                FieldsDefaults = fields,
                IsOneway = IsOneway,
                IsIdempotent = IsIdempotent,
                Operation = Operation,
                Path = proxy.Path,
                Protocol = proxy.Protocol,
                Proxy = proxy,
                PayloadEncoding = PayloadEncoding,
                Payload = new ReadOnlyMemory<byte>[] { Payload } // TODO: temporary, should use GetPayloadAsync()
            };
        }

        private RpcStream? _stream;
    }
}
