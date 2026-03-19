// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Codec;

namespace IceRpc.Slice.Operations;

/// <summary>Provides extension methods for <see cref="SliceDecoder" /> to decode proxies.</summary>
public static class SliceProxySliceDecoderExtensions
{
    /// <summary>Decodes a proxy struct.</summary>
    /// <typeparam name="TProxy">The type of the proxy struct to decode.</typeparam>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The decoded proxy struct.</returns>
    public static TProxy DecodeProxy<TProxy>(this ref SliceDecoder decoder) where TProxy : struct, ISliceProxy =>
        CreateProxy<TProxy>(decoder.DecodeServiceAddress(), decoder.DecodingContext);

    private static TProxy CreateProxy<TProxy>(ServiceAddress serviceAddress, object? decodingContext)
        where TProxy : struct, ISliceProxy
    {
        if (decodingContext is null)
        {
            return new TProxy { Invoker = InvalidInvoker.Instance, ServiceAddress = serviceAddress };
        }
        else
        {
            var baseProxy = (ISliceProxy)decodingContext;
            if (serviceAddress.Protocol is null && baseProxy.ServiceAddress is not null)
            {
                // Convert the relative service address to an absolute service address:
                serviceAddress = baseProxy.ServiceAddress with { Path = serviceAddress.Path };
            }

            return new TProxy
            {
                EncodeOptions = baseProxy.EncodeOptions,
                Invoker = baseProxy.Invoker,
                ServiceAddress = serviceAddress
            };
        }
    }
}
