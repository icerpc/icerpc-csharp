// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;

// Make internals visible to the tests assembly, to allow writing unit tests for the internal classes
[assembly: InternalsVisibleTo("IceRpc.Tests.Internal")]
[assembly: InternalsVisibleTo("IceRpc.Tests.Encoding")]

namespace IceRpc
{
    /// <summary>Provides global configuration for IceRPC in the current process.</summary>
    public static class Runtime
    {
        /// <summary>The IceRPC version in semver format.</summary>
        public const string StringVersion = "0.0.1-alpha";

        /// <summary>Gets or sets the logger factory used by IceRPC classes when no logger factory is explicitly
        /// configured.</summary>
        public static ILoggerFactory DefaultLoggerFactory { get; set; } = NullLoggerFactory.Instance;

        static Runtime()
        {
            // Register the ice and ice+universal schemes with the system UriParser.
            UriParser.RegisterTransport("universal", defaultPort: 0);
            UriParser.RegisterIceScheme();
            TransportRegistry.Add(new LocEndpointFactory());
        }

        // Must be called before parsing a Uri to make sure the static constructors of Runtime and TransportRegistry
        // executed and registered the URI schemes for the built-in transports.
        internal static void UriInitialize() => TransportRegistry.UriInitialize();
    }
}
