// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>The common interface of all concrete typed proxies. It gives access to an untyped proxy object that can
    /// send requests to a remote IceRPC service.</summary>
    public interface IPrx
    {
        /// <summary>Gets the proxy object, or sets this proxy object during initialization.</summary>
        Proxy Proxy { get; init; }
    }

    /// <summary>Provides extension methods for SliceDecoder.</summary>
    public static class SliceDecoderPrxExtensions
    {
        /// <summary>Decodes a nullable typed proxy.</summary>
        /// <param name="decoder">The decoder.</param>
        /// <returns>The decoded proxy, or null.</returns>
        public static T? DecodeNullablePrx<T>(ref this SliceDecoder decoder) where T : struct, IPrx =>
            decoder.DecodeNullableProxy() is Proxy proxy ? new T { Proxy = proxy } : null;
    }
}
