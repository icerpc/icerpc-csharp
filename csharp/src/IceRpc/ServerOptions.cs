// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Net.Security;
using System.Threading.Tasks;

namespace IceRpc
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
        public IncomingConnectionOptions? ConnectionOptions { get; set; } = new();

        public ColocationScope ColocationScope { get; set; }

        // TODO: should it be Endpoint?
        public string Endpoints { get; set; } = "";

        public string Name { get; set; } = "";

        public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

        public Protocol Protocol { get; set; } = Protocol.Ice2; // only used if Endpoints is empty

        public string PublishedEndpoints { get; set; } = "";

        // TODO: fix default
        public string PublishedHost { get; set; } = "localhost"; // System.Net.Dns.GetHostName();

        public TaskScheduler? TaskScheduler { get; set; }
    }
}
