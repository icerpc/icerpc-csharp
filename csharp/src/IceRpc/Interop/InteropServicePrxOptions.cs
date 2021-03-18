// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Interop
{
    /// <summary>An options class for configuring a service proxy (see <see cref="IServicePrx"/>).</summary>
    public sealed class InteropServicePrxOptions : ServicePrxOptions
    {
        public string Facet { get; set; } = ""; // ice1 only
        public Identity Identity { get; set; } = Identity.Empty; // ice1 only

        public InteropServicePrxOptions()
        {
            Protocol = Protocol.Ice1;
        }
    }
}
