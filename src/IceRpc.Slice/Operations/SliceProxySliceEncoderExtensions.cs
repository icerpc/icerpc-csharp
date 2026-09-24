// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Codec;

namespace IceRpc.Slice.Operations;

/// <summary>Provides extension methods for <see cref="SliceEncoder" /> to encode proxies.</summary>
public static class SliceProxySliceEncoderExtensions
{
    /// <summary>Encodes a proxy struct.</summary>
    /// <typeparam name="TProxy">The type of the proxy struct to encode.</typeparam>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The proxy to encode.</param>
    /// <remarks>A proxy is encoded as its service address URI, or as the path of its service address when it's a
    /// relative proxy.</remarks>
    public static void EncodeProxy<TProxy>(this ref SliceEncoder encoder, TProxy value)
        where TProxy : struct, ISliceProxy =>
        encoder.EncodeString(value.IsRelative ? value.ServiceAddress.Path : value.ServiceAddress.ToString());
}
