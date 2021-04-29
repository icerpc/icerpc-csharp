// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Extension class for OutputStream to marshal ProxyData11 without creating a ProxyData11instance. This
    /// implementation is slightly more efficient than the generated code because it avoids the allocation of a string[]
    /// to write the facet.</summary>
    internal static class ProxyDataExtensions
    {
        internal static void WriteProxyData11(
            this OutputStream ostr,
            string facet,
            InvocationMode invocationMode,
            Protocol protocol,
            Encoding encoding)
        {
            Debug.Assert(ostr.Encoding == Encoding.V11);
            ostr.WriteIce1Facet(facet);
            ostr.Write(invocationMode);
            ostr.WriteBool(false); // "secure"
            ostr.Write(protocol);
            ostr.WriteByte(0); // protocol minor
            encoding.IceWrite(ostr);
        }
    }
}
