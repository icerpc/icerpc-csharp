// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>Holds properties that describe the request being dispatched. You can also set entries in
    /// <see cref="Features"/> to communicate with a middleware "on the way back".</summary>
    public sealed class Dispatch
    {
        /// <summary>The <see cref="Connection"/> over which the request was dispatched.</summary>
        public Connection Connection => IncomingRequest.Connection;

        /// <summary>The deadline corresponds to the request's expiration time. Once the deadline is reached, the
        /// caller is no longer interested in the response and discards the request. The server-side runtime does not
        /// enforce this deadline - it's provided "for information" to the application. The IceRPC client runtime sets
        /// this deadline automatically using the proxy's invocation timeout and sends it with icerpc requests but not
        /// with ice requests. As a result, the deadline for an ice request is always <see cref="DateTime.MaxValue"/>
        /// on the server-side even though the invocation timeout is usually not infinite.</summary>
        public DateTime Deadline
        {
            get
            {
                if (_deadline == null)
                {
                    long value = IncomingRequest.Fields.DecodeValue(
                        (int)FieldKey.Deadline,
                        (ref SliceDecoder decoder) => decoder.DecodeVarLong());

                    // unset or <= 0 results in DateTime.MaxValue
                    _deadline = value > 0 ? DateTime.UnixEpoch + TimeSpan.FromMilliseconds(value) : DateTime.MaxValue;
                }
                return _deadline.Value;
            }
        }

        /// <summary>The encoding used by the request.</summary>
        public Encoding Encoding => IncomingRequest.PayloadEncoding;

        /// <summary>The features associated with the request.</summary>
        public FeatureCollection Features
        {
            get => IncomingRequest.Features;
            set => IncomingRequest.Features = value;
        }

        /// <summary><c>True</c> for oneway requests, <c>False</c> otherwise.</summary>
        public bool IsOneway => IncomingRequest.IsOneway;

        /// <summary>The operation name.</summary>
        public string Operation => IncomingRequest.Operation;

        /// <summary>The path (percent-escaped).</summary>
        public string Path => IncomingRequest.Path;

        /// <summary>The protocol used by the request.</summary>
        public Protocol Protocol => IncomingRequest.Protocol;

        /// <summary>The incoming request frame.</summary>
        internal IncomingRequest IncomingRequest { get; }

        private DateTime? _deadline;

        /// <summary>Constructs a dispatch from an incoming request.</summary>
        public Dispatch(IncomingRequest request) => IncomingRequest = request;
    }
}
