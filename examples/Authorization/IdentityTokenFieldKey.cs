// Copyright (c) ZeroC, Inc.

using IceRpc;

namespace AuthorizationExample;

/// <summary>The shared <see cref="RequestFieldKey" /> used by the client and server to carry the identity
/// token.</summary>
public static class IdentityTokenFieldKey
{
    public const RequestFieldKey Value = (RequestFieldKey)100;
}
