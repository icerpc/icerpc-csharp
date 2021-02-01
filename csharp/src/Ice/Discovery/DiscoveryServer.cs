// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice.Discovery
{
    public sealed class DiscoveryServerOptions
    {
        public string DomainId { get; set; } = "";

        public int LatencyMultiplier { get; set; } = 1;

        public string Lookup { get; set; } = "";

        public string MulticastEndpoints { get; set; } = "";

        public string ReplyEndpoints { get; set; } = "";

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
