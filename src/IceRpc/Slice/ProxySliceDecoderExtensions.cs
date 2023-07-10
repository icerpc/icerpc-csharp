// Copyright (c) ZeroC, Inc.

using Slice;

namespace IceRpc.Slice;

/// <summary>Provides extension methods for decoding proxies.</summary>
public static class ProxySliceDecoderExtensions
{
    /// <summary>Decodes a nullable proxy struct (Slice1 only).</summary>
    /// <typeparam name="TProxy">The type of the proxy struct to decode.</typeparam>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The decoded proxy, or <see langword="null" />.</returns>
    public static TProxy? DecodeNullableProxy<TProxy>(this ref SliceDecoder decoder) where TProxy : struct, IProxy =>
        decoder.DecodeNullableServiceAddress() is ServiceAddress serviceAddress ?
            CreateProxy<TProxy>(serviceAddress, decoder.DecodingContext) : null;

    /// <summary>Decodes a proxy struct.</summary>
    /// <typeparam name="TProxy">The type of the proxy struct to decode.</typeparam>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The decoded proxy struct.</returns>
    public static TProxy DecodeProxy<TProxy>(this ref SliceDecoder decoder) where TProxy : struct, IProxy =>
        decoder.Encoding == SliceEncoding.Slice1 ?
            decoder.DecodeNullableProxy<TProxy>() ??
                throw new InvalidDataException("Decoded null for a non-nullable proxy.") :
           CreateProxy<TProxy>(decoder.DecodeServiceAddress(), decoder.DecodingContext);

    private static TProxy CreateProxy<TProxy>(ServiceAddress serviceAddress, object? decodingContext)
        where TProxy : struct, IProxy
    {
        if (decodingContext is Func<ServiceAddress, GenericProxy> proxyFactory)
        {
            GenericProxy proxy = proxyFactory(serviceAddress);
            return new TProxy
            {
                EncodeOptions = proxy.EncodeOptions,
                Invoker = proxy.Invoker,
                ServiceAddress = proxy.ServiceAddress
            };
        }
        else
        {
            return new TProxy { ServiceAddress = serviceAddress };
        }
    }
}
