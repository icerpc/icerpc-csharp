// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice.Discovery
{
    /// <summary>An options class for configuring a <see cref="DiscoveryServer"/>.</summary>
    public sealed class DiscoveryServerOptions
    {
        /// <summary>The default IPv4 multicast endpoint using by DiscoveryServer.</summary>
        public const string DefaultIPv4Endpoint = "udp -h 239.255.0.1 -p 4061";

        /// <summary>The default IPv6 multicast endpoint using by DiscoveryServer.</summary>
        public const string DefaultIPv6Endpoint = "udp -h \"ff15::1\" -p 4061";

        /// <summary>The DiscoveryServer's domain ID. Applications using different domain IDs don't interfere with one
        /// another even if they share the same multicast endpoints.</summary>
        public string DomainId { get; set; } = "";

        public int LatencyMultiplier { get; set; } = 1;

        public string Lookup { get; set; } = "";

        public string MulticastEndpoints { get; set; } = $"{DefaultIPv4Endpoint}:{DefaultIPv6Endpoint}";

        public string ReplyEndpoints { get; set; } = "udp -h \"::0\" -p 0";

        public string ReplyServerName { get; set; } = "";

        public int RetryCount { get; set; } = 3;

        public TimeSpan Timeout { get; set; } = TimeSpan.FromMilliseconds(300);
    }

    /// <summary>Implements the Discovery locator. A DiscoveryServer must be created in all applications (clients and
    /// servers) that locate objects using Discovery or that host discoverable objects.</summary>
    public sealed class DiscoveryServer : IAsyncDisposable
    {
        private readonly Locator _locator;

        /// <summary>Constructs a DiscoveryServer.</summary>
        /// <param name="communicator">The communicator. <c>communicator's</c> default locator must be a well-known ice2
        /// proxy with discovery as its identity name, for example `ice:discovery'.</param>
        /// <param name="options">The <see cref="DiscoveryServerOptions"/>.</param>
        public DiscoveryServer(Communicator communicator, DiscoveryServerOptions? options = null) =>
            _locator = new(communicator, options ?? new DiscoveryServerOptions());

        /// <summary>Activates the object adapters used by DiscoveryServer.</summary>
        /// <param name="cancel">The cancellation token.</param>
        /// <return>A task that completes when the activation is complete.</return>
        public Task ActivateAsync(CancellationToken cancel = default) => _locator.ActivateAsync(cancel);

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => new(_locator.ShutdownAsync());
    }
}
