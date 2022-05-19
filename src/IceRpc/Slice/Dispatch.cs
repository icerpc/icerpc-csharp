// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>Holds properties that describe the request being dispatched. You can also set entries in
    /// <see cref="Features"/> to communicate with a middleware "on the way back".</summary>
    public sealed class Dispatch
    {
        /// <summary>The <see cref="IConnection"/> over which the request was dispatched.</summary>
        public IConnection Connection => _request.Connection;

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
                    long value = _request.Fields.DecodeValue(
                        RequestFieldKey.Deadline,
                        (ref SliceDecoder decoder) => decoder.DecodeVarInt62());

                    // unset or <= 0 results in DateTime.MaxValue
                    _deadline = value > 0 ? DateTime.UnixEpoch + TimeSpan.FromMilliseconds(value) : DateTime.MaxValue;
                }
                return _deadline.Value;
            }
        }

        /// <summary>The feature to use when encoding the payload of the response.</summary>
        public ISliceEncodeFeature? EncodeFeature => _request.GetFeature<ISliceEncodeFeature>();

        /// <summary>The features associated with the request.</summary>
        public IFeatureCollection Features
        {
            get => _request.Features;
            set => _request.Features = value;
        }

        /// <summary><c>True</c> for oneway requests, <c>False</c> otherwise.</summary>
        public bool IsOneway => _request.IsOneway;

        /// <summary>The operation name.</summary>
        public string Operation => _request.Operation;

        /// <summary>The path (percent-escaped).</summary>
        public string Path => _request.Path;

        /// <summary>The protocol used by the request.</summary>
        public Protocol Protocol => _request.Protocol;

        private DateTime? _deadline;

        private readonly IncomingRequest _request;

        /// <summary>Constructs a dispatch from an incoming request.</summary>
        public Dispatch(IncomingRequest request) => _request = request;
    }
}
