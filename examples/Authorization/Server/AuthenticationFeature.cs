// Copyright (c) ZeroC, Inc.

namespace AuthorizationExample;

/// <summary>A feature that stores the authenticated client's name and whether or not it has the administrative
/// privilege.</summary>
public interface IAuthenticationFeature
{
    string Name { get; }

    bool IsAdmin { get; }
}

/// <summary>The implementation of <see cref="IAuthenticationFeature" />.</summary>
internal class AuthenticationFeature : IAuthenticationFeature
{
    public string Name { get; }

    public bool IsAdmin { get; }

    public AuthenticationFeature(AuthenticationToken token)
    {
        Name = token.Name;
        IsAdmin = token.IsAdmin;
    }
}
