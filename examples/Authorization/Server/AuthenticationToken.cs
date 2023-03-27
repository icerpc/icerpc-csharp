// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Text;

namespace AuthorizationExample;

internal readonly struct AuthenticationToken
{
    public string Name { get; }

    public bool IsAdmin { get; }

    public AuthenticationToken(string name, bool isAdmin)
    {
        Name = name;
        IsAdmin = isAdmin;
    }

    public ReadOnlyMemory<byte> Encrypt(SymmetricAlgorithm algorithm)
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

    public static AuthenticationToken Decrypt(byte[] buffer, SymmetricAlgorithm algorithm)
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
}
