// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Codec;

namespace IceRpc.Slice.Operations;

/// <summary>Provides extension methods for <see cref="SliceEncoder" /> to encode service addresses.</summary>
public static class ServiceAddressSliceEncoderExtensions
{
    /// <summary>Encodes a service address.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The value to encode.</param>
    public static void EncodeServiceAddress(this ref SliceEncoder encoder, ServiceAddress value) =>
        encoder.EncodeString(value.ToString()); // a URI or an absolute path
}
