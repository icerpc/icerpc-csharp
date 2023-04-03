// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace AuthorizationExample;

/// <summary>A bearer authentication handler to create and validate an identity token.</summary>
public interface IBearerAuthenticationHandler
{
    /// <summary>Creates a binary identity token.</summary>
    /// <param name="name">The user name</param>
    /// <param name="isAdmin"><c>true</c> if the user has administrative privilege, <c>false</c> otherwise.</param>
    /// <returns>The binary identity token.</returns>
    ReadOnlyMemory<byte> CreateIdentityToken(string name, bool isAdmin);

    /// <summary>Validates a binary identity token.</summary>
    /// <param name="identityTokenBytes">The binary identity token.</param>
    /// <returns>A task that returns the decoded identity token as an identity feature.</returns>
    /// <exception cref="DispatchException">Thrown is the validation failed.</exception>
    Task<IIdentityFeature> ValidateIdentityTokenAsync(ReadOnlySequence<byte> identityTokenBytes);
}
