// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    internal static class NetworkStreamExtensions
    {
        /// <summary>Aborts the stream.</summary>
        /// <param name="stream">The stream to abort.</param>
        /// <param name="errorCode">The reason of the abort.</param>
        public static void Abort(this INetworkStream stream, StreamError errorCode)
        {
            stream.AbortRead(errorCode);
            stream.AbortWrite(errorCode);
        }
    }
}
