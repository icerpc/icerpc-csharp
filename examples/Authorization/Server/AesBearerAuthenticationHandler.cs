// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;

namespace AuthorizationExample;

/// <summary>This is an implementation of <see cref="IBearerAuthenticationHandler" /> to create and validate a Slice
/// based identity token encrypted with Aes.</summary>
internal sealed class AesBearerAuthenticationHandler : IBearerAuthenticationHandler, IDisposable
{
    private readonly Aes _aes;

    public async Task<IIdentityFeature> ValidateIdentityTokenAsync(ReadOnlySequence<byte> identityTokenBytes)
    {
        // Decrypt the Slice2 encoded token.
        using var sourceStream = new MemoryStream(identityTokenBytes.ToArray());
        using var cryptoStream = new CryptoStream(sourceStream, _aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var destinationStream = new MemoryStream();
        await cryptoStream.CopyToAsync(destinationStream);

        // Decode the Slice2 encoded token and return the feature.
        AesIdentityToken identityToken = DecodeIdentityToken(destinationStream.ToArray());

        Console.WriteLine(
            $"Decoded Aes identity token {{ name = '{identityToken.Name}' isAdmin = '{identityToken.IsAdmin}' }}");

        return new IdentityFeature(identityToken.Name, identityToken.IsAdmin);

        AesIdentityToken DecodeIdentityToken(ReadOnlyMemory<byte> buffer)
        {
            var decoder = new SliceDecoder(destinationStream.ToArray(), SliceEncoding.Slice2);
            return new AesIdentityToken(ref decoder);
        }
    }

    public void Dispose() => _aes.Dispose();

    public ReadOnlyMemory<byte> CreateIdentityToken(string name, bool isAdmin)
    {
        // Encode the token with the Slice2 encoding.
        using var tokenStream = new MemoryStream();
        var writer = PipeWriter.Create(tokenStream, new StreamPipeWriterOptions(leaveOpen: true));
        var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
        new AesIdentityToken(isAdmin, name).Encode(ref encoder);
        writer.Complete();
        tokenStream.Seek(0, SeekOrigin.Begin);

        // Crypt and return the Slice2 encoded token.
        using var destinationStream = new MemoryStream();
        using var cryptoStream = new CryptoStream(tokenStream, _aes.CreateEncryptor(), CryptoStreamMode.Read);
        cryptoStream.CopyTo(destinationStream);
        return destinationStream.ToArray();
    }

    internal AesBearerAuthenticationHandler()
    {
        _aes = Aes.Create();
        _aes.Padding = PaddingMode.Zeros;
    }
}
