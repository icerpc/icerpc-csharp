// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;

namespace AuthorizationExample;

/// <summary>This is an implementation of <see cref="IBearerAuthenticationHandler" /> to create and validate a Slice
/// based identity token encrypted with AES.</summary>
internal sealed class AesBearerAuthenticationHandler : IBearerAuthenticationHandler, IDisposable
{
    private readonly Aes _aes;

    public async Task<IIdentityFeature> ValidateIdentityTokenAsync(ReadOnlySequence<byte> identityTokenBytes)
    {
        // Decrypt the identity token buffer.
        using var sourceStream = new MemoryStream(identityTokenBytes.ToArray());
        using var cryptoStream = new CryptoStream(sourceStream, _aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var destinationStream = new MemoryStream();
        await cryptoStream.CopyToAsync(destinationStream);

        // Decode the identity token.
        AesIdentityToken identityToken = DecodeIdentityToken(destinationStream.ToArray());

        Console.WriteLine(
            $"Decoded AES identity token {{ name = '{identityToken.Name}' isAdmin = '{identityToken.IsAdmin}' }}");

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
        // Setup the stream to encrypt the encoded identity token.
        using var destinationStream = new MemoryStream();
        using var cryptoStream = new CryptoStream(destinationStream, _aes.CreateEncryptor(), CryptoStreamMode.Write);

        // Encode the identity token.
        var writer = PipeWriter.Create(cryptoStream, new StreamPipeWriterOptions(leaveOpen: true));
        var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
        new AesIdentityToken(isAdmin, name).Encode(ref encoder);
        writer.Complete();

        cryptoStream.FlushFinalBlock();
        return destinationStream.ToArray();
    }

    internal AesBearerAuthenticationHandler()
    {
        _aes = Aes.Create();
        _aes.Padding = PaddingMode.Zeros;
    }
}
