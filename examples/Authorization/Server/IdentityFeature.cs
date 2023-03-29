// Copyright (c) ZeroC, Inc.

namespace AuthorizationExample;

/// <summary>A feature that provides the name and administrative privilege decoded from an identity token
/// field.</summary>
internal interface IIdentityFeature
{
    /// <summary><c>true</c> if the authenticated client has administrative privilege, <c>false</c> otherwise.</summary>
    bool IsAdmin { get; }

    /// <summary>The name of the authenticated client.</summary>
    string Name { get; }
}

/// <summary>The implementation of <see cref="IIdentityFeature" />.</summary>
internal class IdentityFeature : IIdentityFeature
{
    /// <inheritdoc/>
    public bool IsAdmin { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Constructs an identity feature from an identity token.</summary>
    internal IdentityFeature(IdentityToken token)
    {
        Name = token.Name;
        IsAdmin = token.IsAdmin;
    }
}
