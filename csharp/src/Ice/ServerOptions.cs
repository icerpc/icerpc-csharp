// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    public enum ColocationScope
    {
        Process,
        Communicator,
        None
    }

    /// <summary>An options class for configuring a <see cref="Server"/>.</summary>
    public sealed class ServerOptions
    {
        /// <summary>Indicates under what conditions this server accepts non-secure connections.</summary>
        // TODO: fix default
        public NonSecure AcceptNonSecure { get; set; } = NonSecure.Always;

        public string AdapterId { get; set; } = ""; // ice1 only

        public ColocationScope ColocationScope { get; set; }

        // TODO: should it be Endpoint?
        public string Endpoints { get; set; } = "";

        public int? IncomingFrameMaxSize { get; set; } // 0 means "infinite", null means use Communicator's value

        public ILocatorRegistryPrx? LocatorRegistry { get; set; } // only for ice1 servers

        public string Name { get; set; } = "";

        public Protocol Protocol { get; set; } = Protocol.Ice2; // only used if Endpoints is empty

        public string PublishedEndpoints { get; set; } = "";

        // TODO: fix default
        public string PublishedHost { get; set; } = "localhost"; // System.Net.Dns.GetHostName();

        public string ReplicaGroupId { get; set; } = ""; // ice1 only

        public bool SerializeDispatch { get; set; }

        public TaskScheduler? TaskScheduler { get; set; }

        public SslServerAuthenticationOptions? TlsOptions { get; set; }
    }
}
