// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;

namespace AuthorizationExample;

/// <summary>Extension methods to encrypt and decrypt an authentication token.</summary>
public static class AuthenticationTokenExtensions
{
    /// <summary>Decrypts an authentication token.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="algorithm">The symmetric algorithm used to decrypt an authentication token.</param>
    /// <returns>The authentication token.</returns>
    internal static AuthenticationToken DecryptAuthenticationToken(
        this ReadOnlySequence<byte> buffer,
        SymmetricAlgorithm algorithm)
    {
        // Decrypt the Slice2 encoded token.
        using var sourceStream = new MemoryStream(buffer.ToArray());
        using var cryptoStream = new CryptoStream(sourceStream, algorithm.CreateDecryptor(), CryptoStreamMode.Read);
        using var destinationStream = new MemoryStream();
        cryptoStream.CopyTo(destinationStream);

        // Decode the Slice2 encoded token.
        var decoder = new SliceDecoder(destinationStream.ToArray(), SliceEncoding.Slice2);
        return new AuthenticationToken(ref decoder);
    }

    /// <summary>Encrypts an authentication token.</summary>
    /// <param name="token">The authentication token.</param>
    /// <param name="algorithm">The symmetric algorithm used to encrypt this authentication token.</param>
    /// <returns>The encrypted authentication token.</returns>
    internal static ReadOnlyMemory<byte> Encrypt(this AuthenticationToken token, SymmetricAlgorithm algorithm)
    {
        // Encode the token with the Slice2 encoding.
        using var tokenStream = new MemoryStream();
        var writer = PipeWriter.Create(tokenStream, new StreamPipeWriterOptions(leaveOpen: true));
        var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
        token.Encode(ref encoder);
        writer.Complete();
        tokenStream.Seek(0, SeekOrigin.Begin);

        // Crypt and return the Slice2 encoded token.
        using var destinationStream = new MemoryStream();
        using var cryptoStream = new CryptoStream(tokenStream, algorithm.CreateEncryptor(), CryptoStreamMode.Read);
        cryptoStream.CopyTo(destinationStream);
        return destinationStream.ToArray();
    }
}
