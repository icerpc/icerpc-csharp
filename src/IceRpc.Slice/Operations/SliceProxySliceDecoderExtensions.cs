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
    /// <remarks>The decoding context of the decoder, when not <see langword="null" />, is the base proxy: the decoded
    /// proxy inherits the invoker and encode options of this base proxy, and when the original proxy was a relative
    /// proxy, the service address of the decoded proxy is the service address of the base proxy with the path of the
    /// original proxy. Without a decoding context, the decoded proxy has <see cref="InvalidInvoker.Instance" /> as its
    /// invoker, and it is a relative proxy when the original proxy was a relative proxy.</remarks>
    public static TProxy DecodeProxy<TProxy>(this ref SliceDecoder decoder)
        where TProxy : struct, ISliceProxy<TProxy>
    {
        string value = decoder.DecodeString();
        var baseProxy = (ISliceProxy?)decoder.DecodingContext;

        try
        {
            if (value.StartsWith('/', StringComparison.Ordinal))
            {
                return baseProxy is null ?
                    TProxy.FromPath(value) :
                    TProxy.Create(
                        baseProxy.Invoker,
                        baseProxy.ServiceAddress with { Path = value },
                        baseProxy.EncodeOptions);
            }
            else
            {
                var serviceAddress = new ServiceAddress(new Uri(value, UriKind.Absolute));
                return baseProxy is null ?
                    TProxy.Create(InvalidInvoker.Instance, serviceAddress, encodeOptions: null) :
                    TProxy.Create(baseProxy.Invoker, serviceAddress, baseProxy.EncodeOptions);
            }
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("Received an invalid service address.", exception);
        }
    }
}
