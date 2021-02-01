// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice.Discovery
{
    public sealed class DiscoveryServerOptions
    {
        public const string DefaultIPv4Endpoint = "udp -h 239.255.0.1 -p 4061";
        public const string DefaultIPv6Endpoint = "udp -h \"ff15::1\" -p 4061";

        public string DomainId { get; set; } = "";

        public int LatencyMultiplier { get; set; } = 1;

        public string Lookup { get; set; } = "";

        public string MulticastEndpoints { get; set; } = $"{DefaultIPv4Endpoint}:{DefaultIPv6Endpoint}";

        public string ReplyEndpoints { get; set; } = "udp -h \"::0\" -p 0";

        public string ReplyServerName { get; set; } = "";

        public int RetryCount { get; set; } = 3;

        public TimeSpan Timeout { get; set; } = TimeSpan.FromMilliseconds(300);
    }

    public sealed class DiscoveryServer : IAsyncDisposable
    {
        private readonly Locator _locator;

        public DiscoveryServer(Communicator communicator, DiscoveryServerOptions? options = null)
        {
            _locator = new Locator(communicator, options ?? new DiscoveryServerOptions());
        }

        public Task ActivateAsync(CancellationToken cancel = default) => _locator.ActivateAsync(cancel);

        public ValueTask DisposeAsync() => new(_locator.ShutdownAsync());
    }
}
