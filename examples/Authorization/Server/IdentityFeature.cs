// Copyright (c) ZeroC, Inc.

namespace AuthorizationExample;

/// <summary>A feature that provides the name and administrative privilege of the request.</summary>
public interface IIdentityFeature
{
    /// <summary><c>true</c> if the authenticated user has administrative privilege, <c>false</c> otherwise.</summary>
    bool IsAdmin { get; }

    /// <summary>The name of the authenticated user.</summary>
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
    internal IdentityFeature(string name, bool isAdmin)
    {
        Name = name;
        IsAdmin = isAdmin;
    }
}
