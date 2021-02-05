// Copyright (c) ZeroC, Inc. All rights reserved.
namespace ZeroC.Ice
{
    public enum ColocationScope
    {
        Application,
        Communicator,
        None
    }

    /// <summary>An options class for configuring a <see cref="ObjectAdapter"/>.</summary>
    public sealed class ObjectAdapterOptions
    {
        /// <summary>Indicates under what conditions this object adapter accepts non-secure connections.</summary>
        // TODO: fix default
        public NonSecure AcceptNonSecure { get; set; } = NonSecure.Always;

        public string AdapterId { get; set; } = "";

        public ColocationScope ColocationScope { get; set; }

        // TODO: should it be Endpoint?
        public string Endpoints { get; set; } = "";

        public int? IncomingFrameMaxSize { get; set; } // 0 means "infinite", null means use Communicator's value

        public string? Locator { get; set; } // TODO: should it be a proxy? Only needed for locator registry lookup
                                             // and registration.

        public Protocol Protocol { get; set; } = Protocol.Ice2; // only used if Endpoints is empty

        public string PublishedEndpoints { get; set; } = "";

        public string ReplicaGroupId { get; set; } = "";

        // TODO: fix default
        public string ServerName { get; set; } = "localhost"; // System.Net.Dns.GetHostName();
    }
}
