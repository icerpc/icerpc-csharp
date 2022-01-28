// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>Extensions methods called by the generated code.</summary>
    public static class DispatchExtensions
    {
        /// <summary>Computes the Slice encoding to use when encoding a Slice-generated response.</summary>
        public static SliceEncoding GetSliceEncoding(this Dispatch dispatch) =>
            dispatch.IncomingRequest.GetSliceEncoding();
    }
}
