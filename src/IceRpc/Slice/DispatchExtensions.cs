// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>Extensions methods called by the generated code.</summary>
    public static class DispatchExtensions
    {
        /// <summary>The generated code calls this method to ensure that when an operation is _not_ declared
        /// idempotent, the request is not marked idempotent. If the request is marked idempotent, it means the caller
        /// incorrectly believes this operation is idempotent.</summary>
        public static void CheckNonIdempotent(this Dispatch dispatch)
        {
            if (dispatch.IsIdempotent)
            {
                throw new InvalidDataException(
                    $@"idempotent mismatch for operation '{dispatch.Operation
                    }': received request marked idempotent for a non-idempotent operation");
            }
        }

        /// <summary>Computes the Ice encoding to use when encoding a Slice-generated response.</summary>
        public static IceEncoding GetIceEncoding(this Dispatch dispatch) =>
            dispatch.Encoding as IceEncoding ?? dispatch.Protocol.GetIceEncoding() ??
                throw new NotSupportedException($"unknown protocol {dispatch.Protocol.GetName()}");

        /// <summary>The generated code calls this method to ensure that streaming is aborted if the operation
        /// doesn't specify a stream parameter.</summary>
        public static void StreamReadingComplete(this Dispatch dispatch) =>
            dispatch.IncomingRequest.Stream.AbortRead(IceRpc.Transports.RpcStreamError.UnexpectedStreamData);
    }
}
