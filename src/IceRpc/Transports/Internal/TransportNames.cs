// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    /// <summary>Transport names used by the IceRpc assembly.</summary>
    internal static class TransportNames
    {
        internal const string Loc = "loc";        // ice only (i.e. no special handling with icerpc)
        internal const string Opaque = "opaque";  // ice only (i.e. no special handling with icerpc)
        internal const string Ssl = "ssl";
        internal const string Tcp = "tcp";
        internal const string Udp = "udp";
    }
}
