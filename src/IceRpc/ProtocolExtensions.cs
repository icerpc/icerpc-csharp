// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Provides public extensions methods for <see cref="Protocol"/>.</summary>
    public static class ProtocolExtensions
    {
        /// <summary>Returns the encoding that this protocol uses for its headers.</summary>
        /// <param name="protocol">The protocol.</param>
        public static Encoding GetEncoding(this Protocol protocol) =>
            protocol switch
            {
                Protocol.Ice1 => Encoding.Ice11,
                Protocol.Ice2 => Encoding.Ice20,
                _ => Encoding.Unknown
            };

        /// <summary>Returns the name of this protocol in lowercase, e.g. "ice1" or "ice2".</summary>
        public static string GetName(this Protocol protocol) =>
            protocol.ToString().ToLowerInvariant();

        /// <summary>Returns <c>true</c> if the protocol support fields with protocol frame headers.</summary>
        public static bool HasFieldSupport(this Protocol protocol) =>
            protocol switch
            {
                Protocol.Ice1 => false,
                Protocol.Ice2 => true,
                _ => false
            };
    }
}
