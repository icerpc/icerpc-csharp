// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Codec;

/// <summary>Provides an extension method for <see cref="IceDecoder" /> to decode a <c>WellKnownTypes::Uri</c> into a
/// <see cref="Uri" />.</summary>
public static class UriIceDecoderExtensions
{
    /// <summary>Decodes a URI.</summary>
    /// <param name="decoder">The Ice decoder.</param>
    /// <returns>The URI decoded as a <see cref="Uri"/>.</returns>
    public static Uri DecodeUri(this ref IceDecoder decoder)
    {
        string value = decoder.DecodeString();
        try
        {
            return new Uri(value, UriKind.RelativeOrAbsolute);
        }
        catch (UriFormatException exception)
        {
            throw new InvalidDataException(message: null, exception);
        }
    }
}
