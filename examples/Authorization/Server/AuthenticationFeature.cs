// Copyright (c) ZeroC, Inc.

namespace AuthorizationExample;

/// <summary>A feature that provides the name and administrative privilege decoded from an authentication token
/// field.</summary>
internal interface IAuthenticationFeature
{
    /// <summary><c>true</c> if the authenticated client has administrative privilege, <c>false</c> otherwise.</summary>
    bool IsAdmin { get; }

    /// <summary>The name of the authenticated client.</summary>
    string Name { get; }
}

/// <summary>The implementation of <see cref="IAuthenticationFeature" />.</summary>
internal class AuthenticationFeature : IAuthenticationFeature
{
    /// <inheritdoc/>
    public bool IsAdmin { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Constructs an authentication feature from an authentication token.</summary>
    internal AuthenticationFeature(AuthenticationToken token)
    {
        Name = token.Name;
        IsAdmin = token.IsAdmin;
    }
}
