// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>Extensions methods called by the generated code.</summary>
    public static class DispatchExtensions
    {
        /// <summary>Computes the Ice encoding to use when encoding a Slice-generated response.</summary>
        public static IceEncoding GetIceEncoding(this Dispatch dispatch) =>
            dispatch.Encoding as IceEncoding ?? dispatch.Protocol.GetIceEncoding() ??
                throw new NotSupportedException($"unknown protocol {dispatch.Protocol.GetName()}");
    }
}
