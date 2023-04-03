// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace AuthorizationExample;

/// <summary>An interface to encode, decode and validate the authentication identity token. The identity token is sent
/// in the request header using the <see cref="IdentityTokenFieldKey" /> field.</summary>
public interface IAuthenticationBearer
{
    /// <summary>Decodes and validates a binary identity token.</summary>
    /// <param name="identityTokenBytes">The binary identity token.</param>
    /// <returns>A task that provides the decoded identity token as an identity feature.</returns>
    /// <exception cref="DispatchException">Thrown is the decoding or the validation failed.</exception>
    Task<IIdentityFeature> DecodeAndValidateIdentityTokenAsync(ReadOnlySequence<byte> identityTokenBytes);

    /// <summary>Encodes the fields of an identity token.</summary>
    /// <param name="name">The user name</param>
    /// <param name="isAdmin"><c>true</c> if the user has administrative privilege, <c>false</c> otherwise.</param>
    /// <returns>The binary identity token.</returns>
    ReadOnlyMemory<byte> EncodeIdentityToken(string name, bool isAdmin);
}
