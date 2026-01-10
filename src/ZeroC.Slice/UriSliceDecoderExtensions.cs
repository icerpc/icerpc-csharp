// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Provides an extension method for <see cref="SliceDecoder" /> to decode a <c>WellKnownTypes::Uri</c> into a
/// <see cref="Uri" />.</summary>
public static class UriSliceDecoderExtensions
{
    /// <summary>Extension methods for <see cref="SliceDecoder" />.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    extension(ref SliceDecoder decoder)
    {
        /// <summary>Decodes a URI.</summary>
        /// <returns>The URI decoded as a <see cref="Uri"/>.</returns>
        public Uri DecodeUri()
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
}
