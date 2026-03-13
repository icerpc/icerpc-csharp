// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Codec;

namespace IceRpc.Slice;

/// <summary>Provides extension methods for <see cref="SliceDecoder" /> to decode service addresses.</summary>
public static class ServiceAddressSliceDecoderExtensions
{
    /// <summary>Decodes a service address.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The decoded service address.</returns>
    public static ServiceAddress DecodeServiceAddress(this ref SliceDecoder decoder)
    {
        string serviceAddressString = decoder.DecodeString();
        try
        {
            if (serviceAddressString.StartsWith('/'))
            {
                // relative service address
                return new ServiceAddress { Path = serviceAddressString };
            }
            else
            {
                return new ServiceAddress(new Uri(serviceAddressString, UriKind.Absolute));
            }
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("Received an invalid service address.", exception);
        }
    }
}
