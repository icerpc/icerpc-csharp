// Copyright (c) ZeroC, Inc. All rights reserved.

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
        public string Operation { get; init; }

        /// <summary>The path of the target service.</summary>
        public string Path { get; init; }

        /// <summary>The invoker assigned to any proxy read from the payload of this request.</summary>
        public IInvoker? ProxyInvoker { get; set; }

        /// <summary>A stream parameter decompressor. Middleware can use this property to decompress a stream
        /// parameter.</summary>
        public Func<CompressionFormat, System.IO.Stream, System.IO.Stream>? StreamDecompressor { get; set; }

        /// <summary>Get the cancellation dispatch source.</summary>
        internal CancellationTokenSource? CancelDispatchSource { get; set; }

        /// <summary>The stream used to receive the request.</summary>
        internal IMultiplexedStream? Stream { get; init; }

        /// <summary>Constructs an incoming request.</summary>
        /// <param name="protocol">The <see cref="Protocol"/> used to send the request.</param>
        /// <param name="path">The path of the request.</param>
        /// <param name="operation">The operation of the request.</param>
        public IncomingRequest(Protocol protocol, string path, string operation) :
            base(protocol)
        {
            Path = path;
            Operation = operation;
        }

        /// <summary>Create an outgoing request from this incoming request. The outgoing request is
        /// constructed to be forwarded with the given proxy. The <see cref="OutgoingRequest.Path"/> is set to
        /// the target <see cref="Proxy.Path"/>.</summary>
        /// <param name="targetProxy">The proxy used to send to the outgoing request.</param>
        /// <returns>The outgoing request to be forwarded.</returns>
        public OutgoingRequest ToOutgoingRequest(Proxy targetProxy) =>
            ToOutgoingRequest(targetConnection: null, targetProxy: targetProxy);

        /// <summary>Create an outgoing request from this incoming request. The outgoing request is
        /// constructed to be forwarded with the given connection. The <see cref="OutgoingRequest.Path"/> is
        /// set to <see cref="Path"/>.</summary>
        /// <param name="targetConnection">The target connection.</param>
        /// <returns>The outgoing request to be forwarded.</returns>
        public OutgoingRequest ToOutgoingRequest(Connection targetConnection) =>
            ToOutgoingRequest(targetConnection: targetConnection, targetProxy: null);

        private OutgoingRequest ToOutgoingRequest(
            Connection? targetConnection,
            Proxy? targetProxy)
        {
            Protocol targetProtocol = targetConnection?.Protocol ?? targetProxy!.Protocol;

            // Fields and context forwarding
            IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields = ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;
            FeatureCollection features = FeatureCollection.Empty;

            if (Protocol == Protocol.Ice2 && targetProtocol == Protocol.Ice2)
            {
                // The context is just another field, features remain empty
                fields = Fields;
            }
            else
            {
                // When Protocol or targetProtocol is Ice1, fields remains empty and we put only the request context
                // in the initial features of the new outgoing request
                features = features.WithContext(Features.GetContext());
            }

            // TODO: forward stream parameters

            return new OutgoingRequest(
                targetProtocol,
                path: targetProxy?.Path ?? Path,
                operation: Operation)
            {
                AltEndpoints = targetProxy?.AltEndpoints ?? ImmutableList<Endpoint>.Empty,
                Connection = targetConnection ?? targetProxy?.Connection,
                Deadline = Deadline,
                Endpoint = targetProxy?.Endpoint,
                Features = features,
                FieldsDefaults = fields,
                IsOneway = IsOneway,
                IsIdempotent = IsIdempotent,
                Proxy = targetProxy,
                PayloadEncoding = PayloadEncoding,
                Payload = new ReadOnlyMemory<byte>[] { Payload }
            };
        }

    }
}
