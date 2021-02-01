// Copyright (c) ZeroC, Inc. All rights reserved.

namespace ZeroC.Ice
{
    /// <summary>An options class for configuring a <see cref="ObjectAdapter"/>.</summary>
    public sealed class ObjectAdapterOptions
    {
        public NonSecure? AcceptNonSecure { get; set; } // null means use communicator's value

        public string? AdapterId { get; set; }

        // TODO: should it be Endpoint?
        public string? Endpoints { get; set; }

        public int? IncomingFrameMaxSize { get; set; } // 0 means "infinite", null means use Communicator's value

        public string? Locator { get; set; } // TODO: should it be a proxy? Only needed for locator registry lookup
                                             // and registration.

        public Protocol Protocol { get; set; } = Protocol.Ice2; // only used if Endpoints is empty

        public string? PublishedEndpoints { get; set; }

        // TODO: primarily useful when Endpoints is udp. Should this be automatic?
        public InvocationMode PublishedInvocationMode { get; set; }

        public string? ReplicaGroupId { get; set; }

        // TODO: should it be PublishedServerName?
        public string? ServerName { get; set; } // null means use communicator's default
    }
}
