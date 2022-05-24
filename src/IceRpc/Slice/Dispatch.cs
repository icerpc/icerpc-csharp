// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;

namespace IceRpc.Slice
{
    /// <summary>Holds properties that describe the request being dispatched. You can also set entries in
    /// <see cref="Features"/> to communicate with a middleware "on the way back".</summary>
    public sealed class Dispatch
    {
        /// <summary>The features associated with the request.</summary>
        public IFeatureCollection Features
        {
            get => _request.Features;
            set => _request.Features = value;
        }

        private readonly IncomingRequest _request;

        /// <summary>Constructs a dispatch from an incoming request.</summary>
        public Dispatch(IncomingRequest request) => _request = request;
    }
}
