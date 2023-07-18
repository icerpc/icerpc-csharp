// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to decode a <c>WellKnownTypes::Uri</c> into a
/// <see cref="Uri" />.</summary>
public static class UriSliceDecoderExtensions
{
    /// <summary>Decodes a URI.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The URI decoded as a <see cref="Uri"/>.</returns>
    public static Uri DecodeUri(this ref SliceDecoder decoder)
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
