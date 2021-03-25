// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Interop
{
    /// <summary>An options class for configuring a service proxy (see <see cref="IServicePrx"/>).</summary>
    public sealed class InteropServicePrxOptions : ServicePrxOptions
    {
        /// <summary>The facet of the proxy. Its default value is the empty string. This property is not inherited when
        /// unmarshaling a proxy because a marshaled ice1 proxy always specifies its facet.</summary>
        public string Facet { get; set; } = "";

        /// <summary>The identity of the proxy. This property is not inherited when unmarshaling a proxy because a
        /// marshaled ice1 proxy always specifies its identity.</summary>
        public Identity Identity { get; set; } = Identity.Empty;

        public InteropServicePrxOptions()
        {
            Protocol = Protocol.Ice1;
        }
    }
}
