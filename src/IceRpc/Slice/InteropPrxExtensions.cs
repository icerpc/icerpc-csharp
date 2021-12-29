// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary></summary>
    public static class InteropPrxExtensions
    {
        /// <summary>Converts a proxy into a "stringified proxy" compatible with ZeroC Ice.</summary>
        /// <param name="prx">The proxy.</param>
        /// <param name="mode">Specifies how non-printable ASCII characters are escaped in the resulting string. See
        /// <see cref="ToStringMode"/>.</param>
        /// <returns>The string representation of this proxy.</returns>
        public static string ToIceString(this IPrx prx, ToStringMode mode = default) => prx.Proxy.ToIceString(mode);
    }
}
