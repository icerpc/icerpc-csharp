// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    /// <summary>Extension methods to decode the payload of an incoming frame when this payload is encoded with a Slice
    /// encoding.</summary>
    public static class IncomingFrameExtensions
    {
        /// <summary>Creates an async enumerable over the payload reader of an incoming frame.</summary>
        /// <param name="incoming">The incoming frame.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="decodeFunc">The function used to decode the streamed member.</param>
        public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            this IncomingFrame incoming,
            IActivator defaultActivator,
            DecodeFunc<T> decodeFunc) =>
            incoming.Payload.ToAsyncEnumerable(
                incoming.GetSlicePayloadEncoding(),
                incoming.Connection,
                incoming.ProxyInvoker,
                incoming.Features.Get<IActivator>() ?? defaultActivator,
                incoming.Features.GetClassGraphMaxDepth(),
                decodeFunc,
                incoming.Features.Get<StreamDecoderOptions>() ?? StreamDecoderOptions.Default);
    }
}
