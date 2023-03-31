// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;

namespace AuthorizationExample;

public sealed class AesAuthenticationBearer : IAuthenticationBearer, IDisposable
{
    private readonly Aes _aes;

    public async Task<IIdentityFeature> DecodeAndValidateIdentityTokenAsync(ReadOnlySequence<byte> identityTokenBytes)
    {
        // Decrypt the Slice2 encoded token.
        using var sourceStream = new MemoryStream(identityTokenBytes.ToArray());
        using var cryptoStream = new CryptoStream(sourceStream, _aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var destinationStream = new MemoryStream();
        await cryptoStream.CopyToAsync(destinationStream);

        // Decode the Slice2 encoded token and return the feature.
        AesIdentityToken identityToken = DecodeIdentityToken(destinationStream.ToArray());
        return new IdentityFeature(identityToken.Name, identityToken.IsAdmin);

        AesIdentityToken DecodeIdentityToken(ReadOnlyMemory<byte> buffer)
        {
            var decoder = new SliceDecoder(destinationStream.ToArray(), SliceEncoding.Slice2);
            return new AesIdentityToken(ref decoder);
        }
    }

    public void Dispose() => _aes.Dispose();

    public ReadOnlyMemory<byte> EncodeIdentityToken(string name, bool isAdmin)
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

    internal AesAuthenticationBearer()
    {
        _aes = Aes.Create();
        _aes.Padding = PaddingMode.Zeros;
    }
}
