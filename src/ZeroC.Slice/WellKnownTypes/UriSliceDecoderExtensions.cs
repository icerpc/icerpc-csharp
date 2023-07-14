// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.WellKnownTypes;

/// <summary>Provides an extension method for decoding a WellKnownTypes::Uri.</summary>
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
