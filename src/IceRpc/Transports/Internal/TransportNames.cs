// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    /// <summary>Transport names used by the IceRpc assembly.</summary>
    internal static class TransportNames
    {
        internal const string Loc = "loc";        // ice1 only (i.e. no special handling with ice2)
        internal const string Opaque = "opaque";  // ice1 only (i.e. no special handling with ice2)
        internal const string Ssl = "ssl";
        internal const string Tcp = "tcp";
        internal const string Udp = "udp";
    }
}
