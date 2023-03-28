// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using System.IO.Pipelines;
using System.Security.Cryptography;

namespace AuthorizationExample;

/// <summary>An struct that defines an authentication token and methods to encrypt and decrypt the authentication
/// token.</summary>
internal readonly struct AuthenticationToken
{
    /// <summary><c>true if the authenticated client has administrative privilege, <c>false</c> otherwise.</summary>
    internal bool IsAdmin { get; }

    /// <summary>The name of the authenticated client.</summary>
    internal string Name { get; }

    /// <summary>Decrypts an authentication token.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="algorithm">The symmetric algorithm used to decrypt an authentication token.</param>
    /// <returns>The authentication token.</returns>
    internal static AuthenticationToken Decrypt(byte[] buffer, SymmetricAlgorithm algorithm)
    {
        // Decrypt the Slice2 encoded token.
        using var sourceStream = new MemoryStream(buffer);
        using var cryptoStream = new CryptoStream(sourceStream, algorithm.CreateDecryptor(), CryptoStreamMode.Read);
        using var destinationStream = new MemoryStream();
        cryptoStream.CopyTo(destinationStream);

        // Decode the Slice2 encoded token.
        var decoder = new SliceDecoder(destinationStream.ToArray(), SliceEncoding.Slice2);
        return new AuthenticationToken(decoder.DecodeString(), decoder.DecodeBool());
    }

    /// <summary>Constructs a new authentication token.</summary>
    /// <param name="name">The name of the authentication token.</param>
    /// <param name="bool"><c>true if the authenticated client has administrative privilege, <c>false</c>
    /// otherwise.</param>
    internal AuthenticationToken(string name, bool isAdmin)
    {
        Name = name;
        IsAdmin = isAdmin;
    }

    /// <summary>Encodes and encrypts this authentication token.</summary>
    /// <param name="algorithm">The symmetric algorithm used to encrypt this authentication token.</param>
    /// <returns>The encrypted authentication token.</returns>
    internal ReadOnlyMemory<byte> Encrypt(SymmetricAlgorithm algorithm)
    {
        // Encode the token with the Slice2 encoding.
        using var tokenStream = new MemoryStream();
        var writer = PipeWriter.Create(tokenStream, new StreamPipeWriterOptions(leaveOpen: true));
        var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
        encoder.EncodeString(Name);
        encoder.EncodeBool(IsAdmin);
        writer.Complete();
        tokenStream.Seek(0, SeekOrigin.Begin);

        // Crypt and return the Slice2 encoded token.
        using var destinationStream = new MemoryStream();
        using var cryptoStream = new CryptoStream(tokenStream, algorithm.CreateEncryptor(), CryptoStreamMode.Read);
        cryptoStream.CopyTo(destinationStream);
        return destinationStream.ToArray();
    }
}
