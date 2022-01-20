// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    /// <summary>Definitions for the "opaque" transport. Used only with the ice protocol.</summary>
    internal static class OpaqueTransport
    {
        internal const string Host = "opaque"; // the "pseudo host" for transport opaque
        internal static ushort Port => (ushort)Protocol.Ice.DefaultUriPort; // the "pseudo port" for transport opaque
    }
}
