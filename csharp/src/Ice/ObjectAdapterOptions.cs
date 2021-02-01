// Copyright (c) ZeroC, Inc. All rights reserved.

namespace ZeroC.Ice
{
    public sealed class ObjectAdapterOptions
    {
        public NonSecure? AcceptNonSecure { get; set; } // null means use communicator's value

        public string? AdapterId { get; set; }

        // TODO: should it be Endpoint?
        public string? Endpoints { get; set; }

        public int? IncomingFrameMaxSize { get; set; } // 0 means "infinite", null means use Communicator's value

        public string? Locator { get; set; } // TODO: should it be a proxy? Only needed for locator registry lookup
                                             // and registration.

        // TODO: locator ice1 proxy options

        public Protocol Protocol { get; set; } = Protocol.Ice2; // only used if Endpoints and PublishedEndpoints are empty

        public string? PublishedEndpoints { get; set; }

        public InvocationMode PublishedInvocationMode { get; set; }

        public string? ReplicaGroupId { get; set; }

        public string? ServerName { get; set; } // null means communicator's default
    }
}
