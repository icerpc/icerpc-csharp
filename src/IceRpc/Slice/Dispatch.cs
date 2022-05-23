// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;

namespace IceRpc.Slice
{
    /// <summary>Holds properties that describe the request being dispatched. You can also set entries in
    /// <see cref="Features"/> to communicate with a middleware "on the way back".</summary>
    public sealed class Dispatch
    {
        /// <summary>The <see cref="IConnection"/> over which the request was dispatched.</summary>
        public IConnection Connection => _request.Connection;

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

        private readonly IncomingRequest _request;

        /// <summary>Constructs a dispatch from an incoming request.</summary>
        public Dispatch(IncomingRequest request) => _request = request;
    }
}
